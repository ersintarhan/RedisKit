namespace RedisKit.Models;

/// <summary>
///     Result of a retry operation
/// </summary>
public class RetryResult<T> where T : class
{
    /// <summary>
    ///     Number of messages successfully processed
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    ///     Number of messages that failed
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    ///     Number of messages moved to the dead letter queue
    /// </summary>
    public int DeadLetterCount { get; set; }

    /// <summary>
    ///     Successfully processed messages
    /// </summary>
    public Dictionary<string, T> ProcessedMessages { get; set; } = new();

    /// <summary>
    ///     Failed messages with error details
    /// </summary>
    public Dictionary<string, string> FailedMessages { get; set; } = new();

    /// <summary>
    ///     Total time taken for retry operation
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }
}