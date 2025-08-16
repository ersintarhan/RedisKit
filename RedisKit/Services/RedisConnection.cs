using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Exceptions;
using RedisKit.Models;
using RedisKit.Utilities;
using StackExchange.Redis;

namespace RedisKit.Services
{
    /// <summary>
    /// Manages the Redis connection with advanced retry logic, circuit breaker, and health monitoring
    /// </summary>
    internal class RedisConnection : IDisposable
    {
        private readonly ILogger<RedisConnection> _logger;
        private readonly RedisOptions _options;
        private readonly RedisCircuitBreaker _circuitBreaker;
        private readonly ConnectionHealthStatus _healthStatus;
        private readonly Timer? _healthCheckTimer;
        
        private ConnectionMultiplexer? _connection;
        private bool _disposed = false;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private TimeSpan? _lastRetryDelay;

        public RedisConnection(
            ILogger<RedisConnection> logger,
            IOptions<RedisOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            
            // Initialize circuit breaker
            var circuitBreakerLogger = logger is ILoggerFactory factory 
                ? factory.CreateLogger<RedisCircuitBreaker>() 
                : new Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisCircuitBreaker>();
            
            _circuitBreaker = new RedisCircuitBreaker(circuitBreakerLogger, _options.CircuitBreaker);
            
            // Initialize health status
            _healthStatus = new ConnectionHealthStatus
            {
                IsHealthy = false,
                LastCheckTime = DateTime.UtcNow
            };

            // Setup health monitoring if enabled
            if (_options.HealthMonitoring.Enabled)
            {
                _healthCheckTimer = new Timer(
                    HealthCheckCallback,
                    null,
                    _options.HealthMonitoring.CheckInterval,
                    _options.HealthMonitoring.CheckInterval);
            }
        }

        /// <summary>
        /// Gets the current connection health status
        /// </summary>
        public ConnectionHealthStatus GetHealthStatus() => _healthStatus;

        /// <summary>
        /// Gets the Redis connection multiplexer with advanced retry and circuit breaker
        /// </summary>
        public async Task<ConnectionMultiplexer> GetConnection()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RedisConnection));

            // Check circuit breaker
            if (!await _circuitBreaker.CanExecuteAsync())
            {
                var stats = _circuitBreaker.GetStats();
                throw new RedisCircuitOpenException($"Circuit breaker is open. Time until half-open: {stats.TimeUntilHalfOpen}");
            }

            // First check without locking
            if (_connection != null && _connection.IsConnected)
            {
                await _circuitBreaker.RecordSuccessAsync();
                UpdateHealthStatus(true, TimeSpan.Zero);
                return _connection;
            }

            // Use semaphore for connection creation
            await _connectionLock.WaitAsync();
            try
            {
                // Double-check pattern
                if (_connection != null && _connection.IsConnected)
                {
                    await _circuitBreaker.RecordSuccessAsync();
                    UpdateHealthStatus(true, TimeSpan.Zero);
                    return _connection;
                }

                _logger.LogInformation("Creating Redis connection to: {ConnectionString}", _options.ConnectionString);

                // Configure connection with advanced timeout settings
                var config = ConfigurationOptions.Parse(_options.ConnectionString);
                ApplyTimeoutSettings(config);

                // Retry with advanced backoff strategy
                var connection = await ConnectWithRetryAsync(config);
                
                if (connection == null || !connection.IsConnected)
                {
                    await _circuitBreaker.RecordFailureAsync();
                    UpdateHealthStatus(false, TimeSpan.Zero, "Failed to establish connection");
                    throw new InvalidOperationException("Failed to establish Redis connection after all retry attempts");
                }

                _connection = connection;
                await _circuitBreaker.RecordSuccessAsync();
                UpdateHealthStatus(true, TimeSpan.Zero);
                
                // Setup connection event handlers
                SetupConnectionEventHandlers(_connection);
                
                _logger.LogInformation("Successfully connected to Redis at: {ConnectionString}", _options.ConnectionString);
                return _connection;
            }
            catch (Exception ex)
            {
                await _circuitBreaker.RecordFailureAsync(ex);
                UpdateHealthStatus(false, TimeSpan.Zero, ex.Message);
                throw;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task<ConnectionMultiplexer?> ConnectWithRetryAsync(ConfigurationOptions config)
        {
            var retryConfig = _options.RetryConfiguration;
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 0; attempt < retryConfig.MaxAttempts; attempt++)
            {
                try
                {
                    _logger.LogDebug("Connection attempt {Attempt}/{MaxAttempts}", attempt + 1, retryConfig.MaxAttempts);
                    
                    var attemptStopwatch = Stopwatch.StartNew();
                    var connection = await ConnectionMultiplexer.ConnectAsync(config);
                    attemptStopwatch.Stop();
                    
                    if (connection.IsConnected)
                    {
                        _logger.LogInformation("Connected to Redis on attempt {Attempt} after {Duration}ms", 
                            attempt + 1, attemptStopwatch.ElapsedMilliseconds);
                        
                        _healthStatus.TotalRequests++;
                        return connection;
                    }
                }
                catch (Exception ex)
                {
                    _healthStatus.TotalRequests++;
                    _healthStatus.FailedRequests++;
                    
                    _logger.LogWarning(ex, "Failed to connect to Redis (attempt {Attempt}/{MaxAttempts})", 
                        attempt + 1, retryConfig.MaxAttempts);

                    if (attempt < retryConfig.MaxAttempts - 1)
                    {
                        // Calculate delay with advanced backoff strategy
                        var delay = BackoffCalculator.CalculateDelay(attempt, retryConfig, _lastRetryDelay);
                        _lastRetryDelay = delay;
                        
                        _logger.LogDebug("Waiting {DelayMs}ms before retry (strategy: {Strategy})", 
                            delay.TotalMilliseconds, retryConfig.Strategy);
                        
                        await Task.Delay(delay);
                    }
                    else
                    {
                        _logger.LogError(ex, "Failed to connect to Redis after {Attempts} attempts in {Duration}ms", 
                            retryConfig.MaxAttempts, stopwatch.ElapsedMilliseconds);
                    }
                }
            }

            return null;
        }

        private void ApplyTimeoutSettings(ConfigurationOptions config)
        {
            var timeouts = _options.TimeoutSettings;
            
            config.ConnectTimeout = (int)timeouts.ConnectTimeout.TotalMilliseconds;
            config.SyncTimeout = (int)timeouts.SyncTimeout.TotalMilliseconds;
            config.AsyncTimeout = (int)timeouts.AsyncTimeout.TotalMilliseconds;
            config.KeepAlive = (int)timeouts.KeepAlive.TotalSeconds;
            // ResponseTimeout is obsolete in StackExchange.Redis 2.7+ and will be removed in 3.0
            // Removed: config.ResponseTimeout = (int)timeouts.ResponseTimeout.TotalMilliseconds;
            config.ConfigCheckSeconds = (int)timeouts.ConfigCheckSeconds.TotalSeconds;
            
            // Additional configuration
            config.ConnectRetry = _options.RetryConfiguration.MaxAttempts;
            config.ReconnectRetryPolicy = new ExponentialRetry((int)_options.RetryConfiguration.InitialDelay.TotalMilliseconds);
            config.AbortOnConnectFail = false; // Allow retry logic to handle failures
        }

        private void SetupConnectionEventHandlers(ConnectionMultiplexer connection)
        {
            connection.ConnectionFailed += (sender, args) =>
            {
                _logger.LogError("Redis connection failed: {FailureType} - {Exception}", 
                    args.FailureType, args.Exception?.Message);
                
                _healthStatus.ConsecutiveFailures++;
                _healthStatus.LastError = args.Exception?.Message;
                UpdateHealthStatus(false, TimeSpan.Zero, args.Exception?.Message);
            };

            connection.ConnectionRestored += (sender, args) =>
            {
                _logger.LogInformation("Redis connection restored: {EndPoint}", args.EndPoint);
                _healthStatus.ConsecutiveFailures = 0;
                UpdateHealthStatus(true, TimeSpan.Zero);
            };

            connection.ErrorMessage += (sender, args) =>
            {
                _logger.LogError("Redis error: {Message} from {EndPoint}", args.Message, args.EndPoint);
            };

            connection.InternalError += (sender, args) =>
            {
                _logger.LogError(args.Exception, "Redis internal error: {Origin}", args.Origin);
            };
        }

        private void UpdateHealthStatus(bool isHealthy, TimeSpan responseTime, string? error = null)
        {
            _healthStatus.IsHealthy = isHealthy;
            _healthStatus.LastCheckTime = DateTime.UtcNow;
            _healthStatus.ResponseTime = responseTime;
            _healthStatus.CircuitState = _circuitBreaker.State;
            
            if (error != null)
            {
                _healthStatus.LastError = error;
            }
            
            if (!isHealthy)
            {
                _healthStatus.ConsecutiveFailures++;
            }
            else
            {
                _healthStatus.ConsecutiveFailures = 0;
            }
        }

        private async void HealthCheckCallback(object? state)
        {
            if (_disposed || !_options.HealthMonitoring.Enabled)
                return;

            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                // Try to ping Redis
                if (_connection != null && _connection.IsConnected)
                {
                    var db = _connection.GetDatabase();
                    var cts = new CancellationTokenSource(_options.HealthMonitoring.CheckTimeout);
                    
                    await db.PingAsync();
                    stopwatch.Stop();
                    
                    UpdateHealthStatus(true, stopwatch.Elapsed);
                    _logger.LogDebug("Health check succeeded in {Duration}ms", stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    // Connection is not available
                    UpdateHealthStatus(false, TimeSpan.Zero, "Connection not available");
                    
                    // Auto-reconnect if enabled and threshold reached
                    if (_options.HealthMonitoring.AutoReconnect && 
                        _healthStatus.ConsecutiveFailures >= _options.HealthMonitoring.ConsecutiveFailuresThreshold)
                    {
                        _logger.LogWarning("Health check failed {Failures} times, attempting reconnection", 
                            _healthStatus.ConsecutiveFailures);
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await GetConnection();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Auto-reconnection failed");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateHealthStatus(false, TimeSpan.Zero, ex.Message);
                _logger.LogWarning(ex, "Health check failed");
            }
        }

        /// <summary>
        /// Gets the Redis database asynchronously
        /// </summary>
        public async Task<IDatabaseAsync> GetDatabaseAsync()
        {
            var connection = await GetConnection();
            return connection.GetDatabase();
        }

        /// <summary>
        /// Gets the Redis subscriber
        /// </summary>
        public async Task<ISubscriber> GetSubscriberAsync()
        {
            var connection = await GetConnection();
            return connection.GetSubscriber();
        }

        /// <summary>
        /// Manually triggers a health check
        /// </summary>
        public async Task<ConnectionHealthStatus> CheckHealthAsync()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var connection = await GetConnection();
                
                if (connection.IsConnected)
                {
                    var db = connection.GetDatabase();
                    await db.PingAsync();
                    stopwatch.Stop();
                    
                    UpdateHealthStatus(true, stopwatch.Elapsed);
                }
                else
                {
                    UpdateHealthStatus(false, TimeSpan.Zero, "Connection not established");
                }
            }
            catch (Exception ex)
            {
                UpdateHealthStatus(false, TimeSpan.Zero, ex.Message);
            }

            return _healthStatus;
        }

        /// <summary>
        /// Resets the circuit breaker
        /// </summary>
        public async Task ResetCircuitBreakerAsync()
        {
            await _circuitBreaker.ResetAsync();
            _logger.LogInformation("Circuit breaker has been reset");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _healthCheckTimer?.Dispose();
                _connectionLock?.Dispose();
                
                try
                {
                    _connection?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing Redis connection");
                }
                
                _disposed = true;
            }
        }

        ~RedisConnection()
        {
            Dispose(false);
        }
    }
}