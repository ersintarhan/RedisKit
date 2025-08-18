namespace RedisKit.Models;

/// <summary>
///     Subscription information for sharded pub/sub
/// </summary>
public class ShardedSubscription
{
    /// <summary>
    ///     Unique subscription ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    ///     Channel or pattern
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    ///     Is this a pattern subscription
    /// </summary>
    public bool IsPattern { get; set; }

    /// <summary>
    ///     Shard ID for this subscription
    /// </summary>
    public string? ShardId { get; set; }

    /// <summary>
    ///     Subscription creation time
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Number of messages received
    /// </summary>
    public long MessageCount { get; set; }

    /// <summary>
    ///     Last message received time
    /// </summary>
    public DateTime? LastMessageAt { get; set; }
}