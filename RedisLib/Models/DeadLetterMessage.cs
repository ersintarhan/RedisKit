namespace RedisLib.Models;

/// <summary>
/// Represents a message that failed processing and was moved to dead letter queue
/// </summary>
public class DeadLetterMessage<T> where T : class
{
    /// <summary>
    /// The original message that failed
    /// </summary>
    public T? OriginalMessage { get; set; }

    /// <summary>
    /// Reason for failure
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>
    /// Exception details if available
    /// </summary>
    public string? ExceptionDetails { get; set; }

    /// <summary>
    /// When the failure occurred
    /// </summary>
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Original stream name
    /// </summary>
    public string OriginalStream { get; set; } = string.Empty;

    /// <summary>
    /// Original message ID in the stream
    /// </summary>
    public string OriginalMessageId { get; set; } = string.Empty;

    /// <summary>
    /// Number of retry attempts before moving to DLQ
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Consumer that was processing the message
    /// </summary>
    public string? ConsumerName { get; set; }

    /// <summary>
    /// Consumer group that owned the message
    /// </summary>
    public string? GroupName { get; set; }
}