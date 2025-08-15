namespace RedisLib.Models;

/// <summary>
/// Connection state for circuit breaker
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed, normal operation
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open, rejecting requests
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is half-open, testing if service recovered
    /// </summary>
    HalfOpen
}