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
        get => _messagesReceived;
        set => _messagesReceived = value;
    }
        
    public long MessagesProcessed 
    { 
        get => _messagesProcessed;
        set => _messagesProcessed = value;
    }
        
    public long MessagesFailed 
    { 
        get => _messagesFailed;
        set => _messagesFailed = value;
    }
        
    public DateTime? LastMessageAt { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }

    public void IncrementMessagesReceived() => Interlocked.Increment(ref _messagesReceived);
    public void IncrementMessagesProcessed() => Interlocked.Increment(ref _messagesProcessed);
    public void IncrementMessagesFailed() => Interlocked.Increment(ref _messagesFailed);
}