using RedisLib.Models;

namespace RedisLib.Interfaces
{
    /// <summary>
    /// Interface for Redis pub/sub operations with generic support
    /// </summary>
    public interface IRedisPubSubService
    {
        /// <summary>
        /// Publishes a message to a channel
        /// </summary>
        Task<long> PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Subscribes to a channel and registers a handler for messages
        /// </summary>
        /// <returns>Subscription token that can be used to unsubscribe</returns>
        Task<SubscriptionToken> SubscribeAsync<T>(string channel, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Subscribes to a channel with message metadata (channel name, publish time, etc.)
        /// </summary>
        /// <returns>Subscription token that can be used to unsubscribe</returns>
        Task<SubscriptionToken> SubscribeWithMetadataAsync<T>(string channel, Func<T, string, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Unsubscribes from a channel
        /// </summary>
        Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default);

        /// <summary>
        /// Unsubscribes a specific handler from a channel
        /// </summary>
        Task UnsubscribeAsync(SubscriptionToken token, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to channels matching a pattern and registers a handler for messages
        /// </summary>
        /// <returns>Subscription token that can be used to unsubscribe</returns>
        Task<SubscriptionToken> SubscribePatternAsync<T>(string pattern, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Subscribes to channels matching a pattern with channel name included
        /// </summary>
        /// <returns>Subscription token that can be used to unsubscribe</returns>
        Task<SubscriptionToken> SubscribePatternWithChannelAsync<T>(string pattern, Func<T, string, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Unsubscribes from a channel pattern
        /// </summary>
        Task UnsubscribePatternAsync(string pattern, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets statistics about current subscriptions
        /// </summary>
        Task<SubscriptionStats[]> GetSubscriptionStatsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a channel has any active subscriptions
        /// </summary>
        Task<bool> HasSubscribersAsync(string channel, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the number of subscribers for a channel
        /// </summary>
        Task<int> GetSubscriberCountAsync(string channel, CancellationToken cancellationToken = default);
    }
}