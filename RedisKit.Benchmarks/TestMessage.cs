using MessagePack;

namespace RedisKit.Benchmarks;

[MessagePackObject]
public class TestMessage
{
    [Key(0)] public Guid Id { get; set; }

    [Key(1)] public string Content { get; set; } = string.Empty;

    [Key(2)] public DateTime Timestamp { get; set; }

    [Key(3)] public string[] Tags { get; set; } = Array.Empty<string>();

    [Key(4)] public Dictionary<string, object> Data { get; set; } = new();
}