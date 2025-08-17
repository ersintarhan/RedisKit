using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RedisKit.Services;
using StackExchange.Redis;

namespace RedisKit.HealthChecks;

/// <summary>
///     Health check implementation for Redis connectivity
/// </summary>
/// <remarks>
///     This health check performs the following validations:
///     - Connection availability
///     - PING command execution
///     - Response time measurement
///     - Circuit breaker state verification
/// </remarks>
public class RedisHealthCheck : IHealthCheck
{
    private readonly RedisConnection _connection;
    private readonly ILogger<RedisHealthCheck> _logger;
    private readonly TimeSpan _responseTimeThreshold;

    public RedisHealthCheck(
        RedisConnection connection,
        ILogger<RedisHealthCheck> logger,
        TimeSpan? responseTimeThreshold = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _responseTimeThreshold = responseTimeThreshold ?? TimeSpan.FromSeconds(1);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Check if connection is available
            var multiplexer = await _connection.GetMultiplexerAsync().ConfigureAwait(false);
            if (multiplexer == null || !multiplexer.IsConnected)
            {
                _logger.LogWarning("Redis health check failed: Connection not available");
                return HealthCheckResult.Unhealthy("Redis connection is not available");
            }

            // Get database and perform PING
            var database = await _connection.GetDatabaseAsync().ConfigureAwait(false);
            var pingResult = await database.PingAsync().ConfigureAwait(false);
            
            stopwatch.Stop();
            
            // Check response time
            if (pingResult > _responseTimeThreshold)
            {
                _logger.LogWarning("Redis health check degraded: Response time {ResponseTime}ms exceeds threshold {Threshold}ms",
                    pingResult.TotalMilliseconds, _responseTimeThreshold.TotalMilliseconds);
                
                return HealthCheckResult.Degraded(
                    $"Redis response time ({pingResult.TotalMilliseconds:F2}ms) exceeds threshold ({_responseTimeThreshold.TotalMilliseconds:F2}ms)",
                    data: new Dictionary<string, object>
                    {
                        ["ResponseTime"] = pingResult.TotalMilliseconds,
                        ["Threshold"] = _responseTimeThreshold.TotalMilliseconds
                    });
            }

            // Get connection health status
            var healthStatus = await _connection.CheckHealthAsync().ConfigureAwait(false);
            
            // Build health check data
            var data = new Dictionary<string, object>
            {
                ["ResponseTime"] = pingResult.TotalMilliseconds,
                ["IsConnected"] = multiplexer.IsConnected,
                ["EndPoints"] = multiplexer.GetEndPoints().Length,
                ["HealthStatus"] = healthStatus.IsHealthy ? "Healthy" : "Unhealthy",
                ["LastCheckTime"] = healthStatus.LastCheckTime,
                ["ConsecutiveFailures"] = healthStatus.ConsecutiveFailures
            };

            if (healthStatus.LastResponseTime.HasValue)
            {
                data["LastResponseTime"] = healthStatus.LastResponseTime.Value.TotalMilliseconds;
            }

            _logger.LogDebug("Redis health check passed: Response time {ResponseTime}ms", pingResult.TotalMilliseconds);
            
            return HealthCheckResult.Healthy(
                $"Redis is healthy (Response: {pingResult.TotalMilliseconds:F2}ms)",
                data);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis health check failed due to timeout");
            return HealthCheckResult.Unhealthy("Redis operation timed out", ex);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis health check failed due to connection error");
            return HealthCheckResult.Unhealthy("Redis connection failed", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed with unexpected error");
            return HealthCheckResult.Unhealthy("Redis health check failed", ex);
        }
    }
}