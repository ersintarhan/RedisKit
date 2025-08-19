namespace RedisKit.Tests.Integration;

/// <summary>
///     Test data models
/// </summary>
public class TestUser
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}