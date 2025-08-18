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
}