using RedisKit.Models;
using StackExchange.Redis;

namespace RedisKit.Interfaces;

/// <summary>
///     Defines the contract for a managed Redis connection provider.
/// </summary>
public interface IRedisConnection
{
    /// <summary>
    ///     Gets the Redis database instance asynchronously.
    /// </summary>
    Task<IDatabaseAsync> GetDatabaseAsync();

    /// <summary>
    ///     Gets the Redis subscriber for pub/sub operations.
    /// </summary>
    Task<ISubscriber> GetSubscriberAsync();

    /// <summary>
    ///     Gets the underlying ConnectionMultiplexer instance.
    /// </summary>
    Task<IConnectionMultiplexer> GetMultiplexerAsync();

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
    ConnectionHealthStatus GetHealthStatus();

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
    Task ResetCircuitBreakerAsync();
}