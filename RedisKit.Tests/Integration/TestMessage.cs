namespace RedisKit.Tests.Integration;

public class TestMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Author { get; set; }
    public int Priority { get; set; }
}