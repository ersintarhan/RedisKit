using MessagePack;

namespace RedisKit.Example;

[MessagePackObject]
public class TestData
{
    [Key(0)] public int Id { get; set; }

    [Key(1)] public string Name { get; set; } = string.Empty;

    [Key(2)] public DateTime Created { get; set; }
}