namespace RedisKit.Models;

/// <summary>
///     Flush mode for Redis Functions
/// </summary>
public enum FlushMode
{
    /// <summary>
    ///     Flush synchronously
    /// </summary>
    Sync,

    /// <summary>
    ///     Flush asynchronously
    /// </summary>
    Async
}