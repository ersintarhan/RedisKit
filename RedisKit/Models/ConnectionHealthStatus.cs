namespace RedisKit.Models;

/// <summary>
///     Connection health status
/// </summary>
public class ConnectionHealthStatus
{
    public bool IsHealthy { get; set; }
    public DateTime LastCheckTime { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public TimeSpan? LastResponseTime { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public CircuitState CircuitState { get; set; }
    public long TotalRequests { get; set; }
    public long FailedRequests { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (double)(TotalRequests - FailedRequests) / TotalRequests : 0;
}