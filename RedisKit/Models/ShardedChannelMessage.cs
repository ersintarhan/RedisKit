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
    public string? Pattern { get; set; }
}