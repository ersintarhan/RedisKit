using RedisKit.Models;

namespace RedisKit.Interfaces;

/// <summary>
///     Interface for Redis Functions support (Redis 7.0+)
///     Provides server-side scripting capabilities with better management than Lua scripts
/// </summary>
public interface IRedisFunction
{
    /// <summary>
    ///     Load a function library into Redis
    /// </summary>
    /// <param name="libraryCode">The function library code (Lua or JavaScript)</param>
    /// <param name="replace">Replace existing library if true</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully loaded</returns>
    Task<bool> LoadAsync(string libraryCode, bool replace = false, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Delete a function library from Redis
    /// </summary>
    /// <param name="libraryName">Name of the library to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully deleted</returns>
    Task<bool> DeleteAsync(string libraryName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     List all function libraries
    /// </summary>
    /// <param name="libraryNamePattern">Optional pattern to filter libraries</param>
    /// <param name="withCode">Include library code in response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of function library information</returns>
    Task<IEnumerable<FunctionLibraryInfo>> ListAsync(string? libraryNamePattern = null, bool withCode = false, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Call a Redis function
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="functionName">Name of the function to call (format: library.function)</param>
    /// <param name="keys">Keys to pass to the function</param>
    /// <param name="args">Arguments to pass to the function</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Function result</returns>
    Task<T?> CallAsync<T>(string functionName, string[]? keys = null, object[]? args = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Call a Redis function in read-only mode
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="functionName">Name of the function to call (format: library.function)</param>
    /// <param name="keys">Keys to pass to the function</param>
    /// <param name="args">Arguments to pass to the function</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Function result</returns>
    Task<T?> CallReadOnlyAsync<T>(string functionName, string[]? keys = null, object[]? args = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Flush all function libraries
    /// </summary>
    /// <param name="mode">Flush mode (Sync or Async)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully flushed</returns>
    Task<bool> FlushAsync(FlushMode mode = FlushMode.Async, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Get function statistics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Function statistics</returns>
    Task<FunctionStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Check if Redis Functions are supported
    /// </summary>
    /// <returns>True if Redis 7.0+ with Functions support</returns>
    Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default);
}