using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedisKit.Exceptions;
using RedisKit.Interfaces;
using RedisKit.Logging;
using RedisKit.Models;
using RedisKit.Utilities;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     Manages the Redis connection with advanced retry logic, circuit breaker, and health monitoring.
///     Provides a robust connection management layer with automatic recovery and health monitoring capabilities.
/// </summary>
/// <remarks>
///     This class provides enterprise-grade connection management for Redis with:
///     - Automatic retry with configurable backoff strategies
///     - Circuit breaker pattern for fault tolerance
///     - Health monitoring with auto-reconnection
///     - Connection pooling through ConnectionMultiplexer
///     - Comprehensive event handling and logging
///     Thread Safety: This class is thread-safe and designed to be used as a singleton.
///     Key Features:
///     - Connection resilience with exponential backoff retry
///     - Circuit breaker to prevent cascading failures
///     - Health checks with configurable intervals
///     - Auto-reconnection on health check failures
///     - Connection event monitoring and logging
///     - Semaphore-based connection locking for thread safety
///     Retry Strategies:
///     - Exponential: Delay doubles with each attempt
///     - Linear: Fixed delay between attempts
///     - Polynomial: Delay grows polynomially
///     - ExponentialWithJitter: Exponential with random jitter
///     Circuit Breaker States:
///     - Closed: Normal operation, requests pass through
///     - Open: Failures exceeded threshold, requests blocked
///     - HalfOpen: Testing if service recovered
///     Health Monitoring:
///     - Periodic health checks via PING command
///     - Auto-reconnection on consecutive failures
///     - Response time tracking
///     - Failure rate monitoring
///     Usage Pattern:
///     <code>
/// // Configure connection
/// var options = new RedisOptions
/// {
///     ConnectionString = "localhost:6379",
///     RetryConfiguration = new RetryConfiguration
///     {
///         MaxAttempts = 5,
///         Strategy = BackoffStrategy.ExponentialWithJitter
///     },
///     CircuitBreaker = new CircuitBreakerOptions
///     {
///         FailureThreshold = 5,
///         BreakDuration = TimeSpan.FromSeconds(30)
///     },
///     HealthMonitoring = new HealthMonitoringOptions
///     {
///         Enabled = true,
///         CheckInterval = TimeSpan.FromMinutes(1),
///         AutoReconnect = true
///     }
/// };
/// 
/// var connection = new RedisConnection(logger, Options.Create(options));
/// var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
/// </code>
/// </remarks>
public class RedisConnection : IRedisConnection, IDisposable
{
    private readonly IRedisCircuitBreaker _circuitBreaker;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly Timer? _healthCheckTimer;
    private readonly ConnectionHealthStatus _healthStatus;
    private readonly ILogger<RedisConnection> _logger;
    private readonly RedisOptions _options;

    private ConnectionMultiplexer? _connection;
    private bool _disposed;
    private TimeSpan? _lastRetryDelay;

    public RedisConnection(
        ILogger<RedisConnection> logger,
        IOptions<RedisOptions> options)
        : this(logger, options, null)
    {
    }

    public RedisConnection(
        ILogger<RedisConnection> logger,
        IOptions<RedisOptions> options,
        IRedisCircuitBreaker? circuitBreaker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Validate connection string
        if (string.IsNullOrWhiteSpace(_options.ConnectionString)) throw new ArgumentException("Redis connection string cannot be null or empty", nameof(options));

        // Validate timeout settings
        if (_options.TimeoutSettings.ConnectTimeout.TotalMilliseconds < 0 ||
            _options.TimeoutSettings.SyncTimeout.TotalMilliseconds < 0 ||
            _options.TimeoutSettings.AsyncTimeout.TotalMilliseconds < 0)
            throw new ArgumentException("Redis timeout settings must be non-negative", nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Use provided circuit breaker or create a new one
        if (circuitBreaker != null)
        {
            _circuitBreaker = circuitBreaker;
        }
        else
        {
            // Initialize circuit breaker with its own logger
            var circuitBreakerLogger = logger is ILoggerFactory factory
                ? factory.CreateLogger<RedisCircuitBreaker>()
                : new NullLogger<RedisCircuitBreaker>();

            _circuitBreaker = new RedisCircuitBreaker(circuitBreakerLogger, Options.Create(_options.CircuitBreaker));
        }

        // Initialize health status
        _healthStatus = new ConnectionHealthStatus
        {
            IsHealthy = false,
            LastCheckTime = DateTime.UtcNow
        };

        // Setup health monitoring if enabled
        if (_options.HealthMonitoring.Enabled)
            _healthCheckTimer = new Timer(
                HealthCheckCallback,
                null,
                _options.HealthMonitoring.CheckInterval,
                _options.HealthMonitoring.CheckInterval);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Gets the Redis database instance asynchronously.
    /// </summary>
    /// <returns>
    ///     An IDatabaseAsync instance for executing Redis commands.
    /// </returns>
    /// <exception cref="RedisCircuitOpenException">Thrown when circuit breaker is preventing connections.</exception>
    /// <exception cref="InvalidOperationException">Thrown when connection cannot be established.</exception>
    /// <remarks>
    ///     This method ensures a healthy connection before returning the database instance.
    ///     The returned database uses the default database index (0) unless configured otherwise.
    /// </remarks>
    public async Task<IDatabaseAsync> GetDatabaseAsync()
    {
        var connection = await GetConnection();
        return connection.GetDatabase();
    }

    /// <summary>
    ///     Gets the Redis subscriber for pub/sub operations.
    /// </summary>
    /// <returns>
    ///     An ISubscriber instance for pub/sub operations.
    /// </returns>
    /// <exception cref="RedisCircuitOpenException">Thrown when circuit breaker is preventing connections.</exception>
    /// <exception cref="InvalidOperationException">Thrown when connection cannot be established.</exception>
    /// <remarks>
    ///     The subscriber is used for pub/sub messaging patterns.
    ///     Multiple calls return the same subscriber instance from the underlying connection.
    /// </remarks>
    public async Task<ISubscriber> GetSubscriberAsync()
    {
        var connection = await GetConnection();
        return connection.GetSubscriber();
    }

    /// <summary>
    ///     Gets the underlying ConnectionMultiplexer instance.
    /// </summary>
    /// <returns>
    ///     The IConnectionMultiplexer instance for advanced operations.
    /// </returns>
    /// <exception cref="RedisCircuitOpenException">Thrown when circuit breaker is preventing connections.</exception>
    /// <exception cref="InvalidOperationException">Thrown when connection cannot be established.</exception>
    /// <remarks>
    ///     Use this method when you need direct access to ConnectionMultiplexer features
    ///     not exposed through the higher-level abstractions.
    ///     The returned instance should not be disposed by the caller.
    /// </remarks>
    public async Task<IConnectionMultiplexer> GetMultiplexerAsync()
    {
        return await GetConnection();
    }

    /// <summary>
    ///     Gets the current connection health status.
    /// </summary>
    /// <returns>
    ///     A snapshot of the current health status including:
    ///     - Connection state (healthy/unhealthy)
    ///     - Last check time
    ///     - Response time
    ///     - Circuit breaker state
    ///     - Failure statistics
    /// </returns>
    /// <remarks>
    ///     This method returns a snapshot and is safe to call frequently.
    ///     Use this for monitoring dashboards and health endpoints.
    /// </remarks>
    public ConnectionHealthStatus GetHealthStatus()
    {
        return _healthStatus;
    }

    /// <summary>
    ///     Gets the Redis connection multiplexer with advanced retry and circuit breaker protection.
    /// </summary>
    /// <returns>
    ///     A connected ConnectionMultiplexer instance ready for use.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown when the connection has been disposed.</exception>
    /// <exception cref="RedisCircuitOpenException">Thrown when circuit breaker is open due to failures.</exception>
    /// <exception cref="InvalidOperationException">Thrown when connection cannot be established after all retries.</exception>
    /// <remarks>
    ///     This method implements the following behavior:
    ///     1. Checks circuit breaker state before attempting connection
    ///     2. Returns existing connection if healthy
    ///     3. Uses semaphore locking for thread-safe connection creation
    ///     4. Implements retry logic with configurable backoff
    ///     5. Records success/failure metrics for circuit breaker
    ///     6. Sets up event handlers for connection monitoring
    ///     The method is idempotent and safe to call concurrently.
    /// </remarks>
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
            await _circuitBreaker.RecordSuccessAsync().ConfigureAwait(false);
            UpdateHealthStatus(true, TimeSpan.Zero);
            return _connection;
        }

        // Use semaphore for connection creation
        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check pattern
            if (_connection != null && _connection.IsConnected)
            {
                await _circuitBreaker.RecordSuccessAsync().ConfigureAwait(false);
                UpdateHealthStatus(true, TimeSpan.Zero);
                return _connection;
            }

            _logger.LogConnectionCreating(RedactConnectionString(_options.ConnectionString));

            // Configure connection with advanced timeout settings
            var config = ConfigurationOptions.Parse(_options.ConnectionString);
            ApplyTimeoutSettings(config);

            // Retry with advanced backoff strategy
            var connection = await ConnectWithRetryAsync(config).ConfigureAwait(false);

            if (connection == null || !connection.IsConnected)
            {
                await _circuitBreaker.RecordFailureAsync().ConfigureAwait(false);
                UpdateHealthStatus(false, TimeSpan.Zero, "Failed to establish connection");
                throw new InvalidOperationException("Failed to establish Redis connection after all retry attempts");
            }

            _connection = connection;
            await _circuitBreaker.RecordSuccessAsync().ConfigureAwait(false);
            UpdateHealthStatus(true, TimeSpan.Zero);

            // Setup connection event handlers
            SetupConnectionEventHandlers(_connection);

            _logger.LogConnectionSuccess(_options.ConnectionString);
            return _connection;
        }
        catch (Exception ex)
        {
            await _circuitBreaker.RecordFailureAsync(ex).ConfigureAwait(false);
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

        for (var attempt = 0; attempt < retryConfig.MaxAttempts; attempt++)
            try
            {
                _logger.LogConnectionAttempt(attempt + 1, retryConfig.MaxAttempts);

                var attemptStopwatch = Stopwatch.StartNew();
                var connection = await ConnectionMultiplexer.ConnectAsync(config).ConfigureAwait(false);
                attemptStopwatch.Stop();

                if (connection.IsConnected)
                {
                    _logger.LogConnectionSuccessWithRetry(attempt + 1, attemptStopwatch.ElapsedMilliseconds);

                    _healthStatus.TotalRequests++;
                    return connection;
                }
            }
            catch (Exception ex)
            {
                _healthStatus.TotalRequests++;
                _healthStatus.FailedRequests++;

                _logger.LogConnectionAttemptFailed(attempt + 1, retryConfig.MaxAttempts, ex);

                if (attempt < retryConfig.MaxAttempts - 1)
                {
                    // Calculate delay with advanced backoff strategy
                    var delay = BackoffCalculator.CalculateDelay(attempt, retryConfig, _lastRetryDelay);
                    _lastRetryDelay = delay;

                    _logger.LogConnectionRetryDelay((int)delay.TotalMilliseconds, retryConfig.Strategy.ToString());

                    await Task.Delay(delay);
                }
                else
                {
                    _logger.LogConnectionFailed(retryConfig.MaxAttempts, stopwatch.ElapsedMilliseconds, ex);
                }
            }

        return null;
    }

    private void ApplyTimeoutSettings(ConfigurationOptions config)
    {
        var timeouts = _options.TimeoutSettings;

        // Use OperationTimeout if it's been explicitly set (different from default 5s)
        // This provides backwards compatibility with code that sets OperationTimeout
        if (_options.OperationTimeout != TimeSpan.FromSeconds(5))
        {
            timeouts.ConnectTimeout = _options.OperationTimeout;
            timeouts.SyncTimeout = _options.OperationTimeout;
            timeouts.AsyncTimeout = _options.OperationTimeout;
        }

        // Ensure reasonable timeout values (prevent extremely small or large settings)
        var connectTimeout = Math.Max(100, Math.Min(60000, (int)timeouts.ConnectTimeout.TotalMilliseconds));
        var syncTimeout = Math.Max(100, Math.Min(60000, (int)timeouts.SyncTimeout.TotalMilliseconds));
        var asyncTimeout = Math.Max(100, Math.Min(60000, (int)timeouts.AsyncTimeout.TotalMilliseconds));

        config.ConnectTimeout = connectTimeout;
        config.SyncTimeout = syncTimeout;
        config.AsyncTimeout = asyncTimeout;

        // KeepAlive should be reasonable (5-30 minutes)
        var keepAlive = Math.Max(30, Math.Min(1800, (int)timeouts.KeepAlive.TotalSeconds));
        config.KeepAlive = keepAlive;

        // ConfigCheckSeconds (should be 30-60 seconds)
        var configCheck = Math.Max(30, Math.Min(120, (int)timeouts.ConfigCheckSeconds.TotalSeconds));
        config.ConfigCheckSeconds = configCheck;

        // Additional configuration
        config.ConnectRetry = Math.Min(10, _options.RetryConfiguration.MaxAttempts);
        config.ReconnectRetryPolicy = new ExponentialRetry((int)_options.RetryConfiguration.InitialDelay.TotalMilliseconds);
        config.AbortOnConnectFail = false; // Allow retry logic to handle failures

        _logger.LogConnectionTimeoutSettings(connectTimeout, syncTimeout, asyncTimeout);
    }

    private void SetupConnectionEventHandlers(ConnectionMultiplexer connection)
    {
        connection.ConnectionFailed += (sender, args) =>
        {
            _logger.LogConnectionFailure(args.FailureType.ToString(), args.Exception?.Message ?? "No exception details");

            _healthStatus.ConsecutiveFailures++;
            _healthStatus.LastError = args.Exception?.Message;
            UpdateHealthStatus(false, TimeSpan.Zero, args.Exception?.Message);
        };

        connection.ConnectionRestored += (sender, args) =>
        {
            _logger.LogConnectionRestored(args.EndPoint?.ToString() ?? "Unknown");
            _healthStatus.ConsecutiveFailures = 0;
            UpdateHealthStatus(true, TimeSpan.Zero);
        };

        connection.ErrorMessage += (sender, args) => { _logger.LogRedisError(args.Message, args.EndPoint?.ToString() ?? "Unknown"); };

        connection.InternalError += (sender, args) => { _logger.LogRedisInternalError(args.Origin?.ToString() ?? "Unknown", args.Exception); };
    }

    private void UpdateHealthStatus(bool isHealthy, TimeSpan responseTime, string? error = null)
    {
        _healthStatus.IsHealthy = isHealthy;
        _healthStatus.LastCheckTime = DateTime.UtcNow;
        _healthStatus.ResponseTime = responseTime;
        _healthStatus.CircuitState = _circuitBreaker.State;

        if (error != null) _healthStatus.LastError = error;

        if (!isHealthy)
            _healthStatus.ConsecutiveFailures++;
        else
            _healthStatus.ConsecutiveFailures = 0;
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
                using var cts = new CancellationTokenSource(_options.HealthMonitoring.CheckTimeout);

                await db.PingAsync().ConfigureAwait(false);
                stopwatch.Stop();

                UpdateHealthStatus(true, stopwatch.Elapsed);
                _logger.LogHealthCheckSuccess(stopwatch.ElapsedMilliseconds);
            }
            else
            {
                // Connection is not available
                UpdateHealthStatus(false, TimeSpan.Zero, "Connection not available");

                // Auto-reconnect if enabled and threshold reached
                if (_options.HealthMonitoring.AutoReconnect &&
                    _healthStatus.ConsecutiveFailures >= _options.HealthMonitoring.ConsecutiveFailuresThreshold)
                {
                    _logger.LogHealthCheckFailureWithReconnect(_healthStatus.ConsecutiveFailures);

                    // Use Task.Run with proper error handling to prevent unobserved exceptions
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await GetConnection().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogAutoReconnectionFailed(ex);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            UpdateHealthStatus(false, TimeSpan.Zero, ex.Message);
            _logger.LogHealthCheckFailed(ex);
        }
    }

    /// <summary>
    ///     Manually triggers a health check and returns the updated status.
    /// </summary>
    /// <returns>
    ///     The updated ConnectionHealthStatus after performing the health check.
    /// </returns>
    /// <remarks>
    ///     This method performs a PING command to verify connectivity.
    ///     Use this for on-demand health verification or custom monitoring.
    ///     The method updates internal health metrics and circuit breaker state.
    ///     Health check includes:
    ///     - Connection state verification
    ///     - PING command execution
    ///     - Response time measurement
    ///     - Health status update
    /// </remarks>
    public async Task<ConnectionHealthStatus> CheckHealthAsync()
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var connection = await GetConnection().ConfigureAwait(false);

            if (connection.IsConnected)
            {
                var db = connection.GetDatabase();
                await db.PingAsync().ConfigureAwait(false);
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
    ///     Resets the circuit breaker to closed state, allowing connections to proceed.
    /// </summary>
    /// <returns>A task representing the asynchronous reset operation.</returns>
    /// <remarks>
    ///     Use this method to manually recover from circuit breaker open state.
    ///     This is useful when you know the underlying issue has been resolved.
    ///     The circuit breaker will return to closed state and reset all failure counters.
    ///     Warning: Only reset the circuit breaker when you're confident the issue is resolved,
    ///     otherwise it may immediately open again due to continued failures.
    /// </remarks>
    public async Task ResetCircuitBreakerAsync()
    {
        await _circuitBreaker.ResetAsync().ConfigureAwait(false);
        _logger.LogCircuitBreakerReset();
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
                _logger.LogConnectionDisposeError(ex);
            }

            _disposed = true;
        }
    }

    ~RedisConnection()
    {
        Dispose(false);
    }

    private static string RedactConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;

        // Use regex to find and replace the password value
        // This pattern looks for "password=" followed by any characters that are not a comma
        return Regex.Replace(
            connectionString,
            @"password=[^,]+",
            "password=*****",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
    }
}