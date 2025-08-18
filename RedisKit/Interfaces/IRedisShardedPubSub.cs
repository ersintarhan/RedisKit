using RedisKit.Models;

namespace RedisKit.Interfaces;

/// <summary>
///     Interface for Redis Sharded Pub/Sub support (Redis 7.0+)
///     Provides scalable pub/sub across cluster shards
/// </summary>
public interface IRedisShardedPubSub
{
    /// <summary>
    ///     Publish a message to a sharded channel
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="channel">Channel name</param>
    /// <param name="message">Message to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of clients that received the message</returns>
    Task<long> PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Subscribe to a sharded channel
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="channel">Channel name</param>
    /// <param name="handler">Message handler</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Subscription token for unsubscribing</returns>
    Task<SubscriptionToken> SubscribeAsync<T>(
        string channel,
        Func<ShardedChannelMessage<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Subscribe to sharded channels matching a pattern
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="pattern">Channel pattern (e.g., "orders:*")</param>
    /// <param name="handler">Message handler</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Subscription token for unsubscribing</returns>
    Task<SubscriptionToken> SubscribePatternAsync<T>(
        string pattern,
        Func<ShardedChannelMessage<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Unsubscribe from a sharded channel
    /// </summary>
    /// <param name="channel">Channel name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Unsubscribe from a pattern
    /// </summary>
    /// <param name="pattern">Channel pattern</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnsubscribePatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Unsubscribe using a subscription token
    /// </summary>
    /// <param name="token">Subscription token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnsubscribeAsync(SubscriptionToken token, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Get sharded pub/sub statistics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Statistics about sharded pub/sub</returns>
    Task<ShardedPubSubStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Get number of subscribers for a sharded channel
    /// </summary>
    /// <param name="channel">Channel name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of subscribers</returns>
    Task<long> GetSubscriberCountAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Check if sharded pub/sub is supported
    /// </summary>
    /// <returns>True if Redis 7.0+ with sharded pub/sub support</returns>
    Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default);
}