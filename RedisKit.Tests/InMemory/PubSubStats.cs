namespace RedisKit.Tests.InMemory;

public class PubSubStats
{
    public int TotalChannels { get; set; }
    public int TotalSubscribers { get; set; }
    public DateTime CollectedAt { get; set; }
}