namespace RedisKit.Models;

/// <summary>
///     Statistics for sharded pub/sub
/// </summary>
public class ShardedPubSubStats
{
    /// <summary>
    ///     Total number of sharded channels with subscribers
    /// </summary>
    public long TotalChannels { get; set; }

    /// <summary>
    ///     Total number of sharded pattern subscriptions
    /// </summary>
    public long TotalPatterns { get; set; }

    /// <summary>
    ///     Total number of subscribers across all shards
    /// </summary>
    public long TotalSubscribers { get; set; }

    /// <summary>
    ///     Number of shards in the cluster
    /// </summary>
    public int ShardCount { get; set; }

    /// <summary>
    ///     Statistics per shard
    /// </summary>
    public List<ShardStats> ShardStatistics { get; set; } = new();

    /// <summary>
    ///     Timestamp of statistics collection
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}