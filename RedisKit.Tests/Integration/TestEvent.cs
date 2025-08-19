namespace RedisKit.Tests.Integration;

public class TestEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; } = "";
    public object? Data { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? Source { get; set; }
}