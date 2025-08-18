namespace RedisKit.Models;

/// <summary>
///     Represents the health status of a connection to Redis.
/// </summary>
public class ConnectionHealthStatus
{
    /// <summary>
    ///     Indicates whether the connection is currently healthy.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    ///     The last time the connection health was checked.
    /// </summary>
    public DateTime LastCheckTime { get; set; }

    /// <summary>
    ///     The total time taken for recent connection operations.
    /// </summary>
    public TimeSpan ResponseTime { get; set; }

    /// <summary>
    ///     The time taken for the last connection operation.
    /// </summary>
    public TimeSpan? LastResponseTime { get; set; }

    /// <summary>
    ///     The last error that occurred during a connection operation, if any.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    ///     The number of consecutive times the connection check has failed.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    ///     The current state of the connection circuit breaker.
    /// </summary>
    public CircuitState CircuitState { get; set; }

    /// <summary>
    ///     The total number of requests made to the connection.
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    ///     The number of requests that have failed.
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    ///     The success rate of requests to the connection.
    /// </summary>
    public double SuccessRate => TotalRequests > 0 ? (double)(TotalRequests - FailedRequests) / TotalRequests : 0;
}