namespace RedisLib.Models;

/// <summary>
/// Connection health monitoring settings
/// </summary>
public class HealthMonitoringSettings
{
    /// <summary>
    /// Enable health monitoring
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval for health checks
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for health check operations
    /// </summary>
    public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Enable automatic reconnection on health failure
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Number of consecutive failures before marking unhealthy
    /// </summary>
    public int ConsecutiveFailuresThreshold { get; set; } = 3;
}