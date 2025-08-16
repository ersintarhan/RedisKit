using RedisKit.Models;

namespace RedisKit.Services;

/// <summary>
/// Circuit breaker statistics
/// </summary>
internal class RedisCircuitBreakerStats
{
    public CircuitState State { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public DateTime LastFailureTime { get; set; }
    public DateTime OpenedAt { get; set; }
    public TimeSpan? TimeUntilHalfOpen { get; set; }
}