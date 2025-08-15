namespace RedisLib.Models;

/// <summary>
/// Circuit breaker configuration
/// </summary>
public class CircuitBreakerSettings
{
    /// <summary>
    /// Enable circuit breaker pattern
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of failures before opening circuit
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Time window for counting failures
    /// </summary>
    public TimeSpan FailureWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Duration to keep circuit open
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of successful calls to close circuit
    /// </summary>
    public int SuccessThreshold { get; set; } = 2;
}