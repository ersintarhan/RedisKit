namespace RedisKit.Models;

/// <summary>
///     Statistics for a single shard
/// </summary>
public class ShardStats
{
    /// <summary>
    ///     Shard identifier
    /// </summary>
    public string ShardId { get; set; } = string.Empty;

    /// <summary>
    ///     Number of channels in this shard
    /// </summary>
    public long ChannelCount { get; set; }

    /// <summary>
    ///     Number of pattern subscriptions in this shard
    /// </summary>
    public long PatternCount { get; set; }

    /// <summary>
    ///     Number of subscribers in this shard
    /// </summary>
    public long SubscriberCount { get; set; }

    /// <summary>
    ///     Is this shard healthy
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    ///     Shard endpoint
    /// </summary>
    public string? Endpoint { get; set; }
}