namespace RedisKit.Models;

/// <summary>
///     Redis Functions statistics
/// </summary>
public class FunctionStats
{
    /// <summary>
    ///     Number of loaded libraries
    /// </summary>
    public int LibraryCount { get; set; }

    /// <summary>
    ///     Total number of functions
    /// </summary>
    public int FunctionCount { get; set; }

    /// <summary>
    ///     Memory used by functions (bytes)
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    ///     Number of running functions
    /// </summary>
    public int RunningFunctions { get; set; }

    /// <summary>
    ///     Engines available
    /// </summary>
    public List<string> Engines { get; set; } = new();
}