using RedisKit.Models;
using RedisKit.Services;

namespace RedisKit.Interfaces;

/// <summary>
///     Interface for circuit breaker pattern implementation to handle Redis connection failures
/// </summary>
/// <remarks>
///     The circuit breaker pattern prevents cascading failures by temporarily blocking operations
///     when a service is experiencing issues. It has three states:
///     - Closed: Normal operation, requests pass through
///     - Open: Service is down, requests fail immediately
///     - HalfOpen: Testing if service has recovered
/// </remarks>
public interface IRedisCircuitBreaker
{
    /// <summary>
    ///     Gets the current state of the circuit breaker
    /// </summary>
    CircuitState State { get; }

    /// <summary>
    ///     Gets statistics about the circuit breaker's operation
    /// </summary>
    RedisCircuitBreakerStats GetStats();

    /// <summary>
    ///     Checks if an operation can be executed based on current circuit state
    /// </summary>
    /// <returns>True if the operation can proceed, false if circuit is open</returns>
    Task<bool> CanExecuteAsync();

    /// <summary>
    ///     Records a successful operation execution
    /// </summary>
    Task RecordSuccessAsync();

    /// <summary>
    ///     Records a failed operation execution
    /// </summary>
    /// <param name="exception">The exception that caused the failure (optional)</param>
    Task RecordFailureAsync(Exception? exception = null);

    /// <summary>
    ///     Manually resets the circuit breaker to closed state
    /// </summary>
    Task ResetAsync();

    /// <summary>
    ///     Manually opens the circuit breaker
    /// </summary>
    Task OpenAsync();

    /// <summary>
    ///     Gets the time when the circuit breaker will attempt to transition from Open to HalfOpen
    /// </summary>
    DateTime? GetNextRetryTime();
}