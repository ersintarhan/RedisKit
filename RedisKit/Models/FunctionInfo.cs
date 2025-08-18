namespace RedisKit.Models;

/// <summary>
///     Information about a single function
/// </summary>
public class FunctionInfo
{
    /// <summary>
    ///     Function name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Function description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Is the function read-only
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    ///     Function flags
    /// </summary>
    public List<string> Flags { get; set; } = new();
}