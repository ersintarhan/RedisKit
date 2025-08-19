namespace RedisKit.Models;

/// <summary>
///     Message received from a sharded channel
/// </summary>
/// <typeparam name="T">Message data type</typeparam>
public class ShardedChannelMessage<T> where T : class
{
    /// <summary>
    ///     Channel name
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    ///     Message data
    /// </summary>
    public T Data { get; set; } = default!;

    /// <summary>
    ///     Shard ID where the message was received
    /// </summary>
    /// <remarks>
    ///     Currently returns a placeholder value (machine name) as StackExchange.Redis
    ///     doesn't expose actual shard metadata in pub/sub callbacks.
    ///     Proper implementation would require access to Redis cluster topology.
    /// </remarks>
    public string ShardId { get; set; } = string.Empty;

    /// <summary>
    ///     Timestamp when the message was received
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Additional metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    ///     Pattern that matched (for pattern subscriptions)
    /// </summary>
    /// <remarks>
    ///     Note: Sharded Pub/Sub does not support pattern subscriptions in Redis.
    ///     This field is included for API consistency but will always be null for sharded channels.
    /// </remarks>
    public string? Pattern { get; set; }
}