namespace RedisKit.Models;

/// <summary>
/// Subscription statistics
/// </summary>
public class SubscriptionStats
{
    private long _messagesReceived;
    private long _messagesProcessed;
    private long _messagesFailed;

    public string ChannelOrPattern { get; set; } = string.Empty;
    public SubscriptionType Type { get; set; }
    public int HandlerCount { get; set; }

    public long MessagesReceived
    {
        get => Interlocked.Read(ref _messagesReceived);
        set => Interlocked.Exchange(ref _messagesReceived, value);
    }

    public long MessagesProcessed
    {
        get => Interlocked.Read(ref _messagesProcessed);
        set => Interlocked.Exchange(ref _messagesProcessed, value);
    }

    public long MessagesFailed
    {
        get => Interlocked.Read(ref _messagesFailed);
        set => Interlocked.Exchange(ref _messagesFailed, value);
    }

    public DateTime? LastMessageAt { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }

    public void IncrementMessagesReceived() => Interlocked.Increment(ref _messagesReceived);
    public void IncrementMessagesProcessed() => Interlocked.Increment(ref _messagesProcessed);
    public void IncrementMessagesFailed() => Interlocked.Increment(ref _messagesFailed);
}