namespace RedisKit.Models;

/// <summary>
/// Contains metrics about stream operations
/// </summary>
public class StreamMetrics
{
    /// <summary>
    /// Total number of messages in the stream
    /// </summary>
    public long TotalMessages { get; set; }

    /// <summary>
    /// Number of pending messages
    /// </summary>
    public long PendingMessages { get; set; }

    /// <summary>
    /// Number of consumers in all groups
    /// </summary>
    public int TotalConsumers { get; set; }

    /// <summary>
    /// Messages processed per second (calculated over a time window)
    /// </summary>
    public double MessagesPerSecond { get; set; }

    /// <summary>
    /// Average time messages spend in pending state
    /// </summary>
    public TimeSpan AverageProcessingTime { get; set; }

    /// <summary>
    /// Number of messages in the dead letter queue
    /// </summary>
    public long DeadLetterCount { get; set; }

    /// <summary>
    /// Time window used for rate calculations
    /// </summary>
    public TimeSpan MeasurementWindow { get; set; }

    /// <summary>
    /// Timestamp of the metrics collection
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}