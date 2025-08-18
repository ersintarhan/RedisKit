using MessagePack;

namespace RedisKit.Benchmarks;

[MessagePackObject]
public class TestData
{
    [Key(0)] public int Id { get; set; }

    [Key(1)] public string Name { get; set; } = string.Empty;

    [Key(2)] public string Email { get; set; } = string.Empty;

    [Key(3)] public int Age { get; set; }

    [Key(4)] public bool IsActive { get; set; }

    [Key(5)] public string[] Tags { get; set; } = Array.Empty<string>();

    [Key(6)] public string? Description { get; set; }

    [Key(7)] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Key(8)] public Dictionary<string, object> Metadata { get; set; } = new();
}