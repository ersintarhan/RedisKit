namespace RedisKit.Models;

/// <summary>
///     Subscription statistics, tracking key metrics for each subscription (channel or pattern).
/// </summary>
public class SubscriptionStats
{
    private long _messagesFailed;
    private long _messagesProcessed;
    private long _messagesReceived;

    /// <summary>
    ///     The name of the channel or pattern that this statistics object represents.
    /// </summary>
    public string ChannelOrPattern { get; set; } = string.Empty;

    /// <summary>
    ///     The type of subscription (e.g., Channel, Pattern).
    /// </summary>
    public SubscriptionType Type { get; set; }

    /// <summary>
    ///     The number of handlers currently registered for this subscription.
    /// </summary>
    public int HandlerCount { get; set; }

    /// <summary>
    ///     The total number of messages received for this subscription. Uses Interlocked.Read/Exchange for thread safety.
    /// </summary>
    public long MessagesReceived
    {
        get => Interlocked.Read(ref _messagesReceived);
        set => Interlocked.Exchange(ref _messagesReceived, value);
    }

    /// <summary>
    ///     The total number of messages successfully processed for this subscription. Uses Interlocked.Read/Exchange for thread safety.
    /// </summary>
    public long MessagesProcessed
    {
        get => Interlocked.Read(ref _messagesProcessed);
        set => Interlocked.Exchange(ref _messagesProcessed, value);
    }

    /// <summary>
    ///     The total number of messages that failed processing for this subscription.  Uses Interlocked.Read/Exchange for thread safety.
    /// </summary>
    public long MessagesFailed
    {
        get => Interlocked.Read(ref _messagesFailed);
        set => Interlocked.Exchange(ref _messagesFailed, value);
    }

    /// <summary>
    ///     The last time a message was received for this subscription.
    /// </summary>
    public DateTime? LastMessageAt { get; set; }

    /// <summary>
    ///     The average time taken to process a message for this subscription.
    /// </summary>
    public TimeSpan AverageProcessingTime { get; set; }

    /// <summary>
    ///     Increments the number of messages received for this subscription in a thread-safe manner.
    /// </summary>
    public void IncrementMessagesReceived()
    {
        Interlocked.Increment(ref _messagesReceived);
    }

    /// <summary>
    ///     Increments the number of messages processed for this subscription in a thread-safe manner.
    /// </summary>
    public void IncrementMessagesProcessed()
    {
        Interlocked.Increment(ref _messagesProcessed);
    }

    /// <summary>
    ///     Increments the number of messages failed for this subscription in a thread-safe manner.
    /// </summary>
    public void IncrementMessagesFailed()
    {
        Interlocked.Increment(ref _messagesFailed);
    }
}