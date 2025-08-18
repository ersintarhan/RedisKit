namespace RedisKit.Models;

/// <summary>
///     Information about a Redis function library
/// </summary>
public class FunctionLibraryInfo
{
    /// <summary>
    ///     Library name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Scripting engine (LUA or JS)
    /// </summary>
    public string Engine { get; set; } = "LUA";

    /// <summary>
    ///     Library description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     List of functions in the library
    /// </summary>
    public List<FunctionInfo> Functions { get; set; } = new();

    /// <summary>
    ///     Library source code (if requested)
    /// </summary>
    public string? Code { get; set; }
}