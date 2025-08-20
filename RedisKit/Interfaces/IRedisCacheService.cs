using RedisKit.Models;

namespace RedisKit.Interfaces;

/// <summary>
///     Interface for Redis caching operations with generic support
/// </summary>
public interface IRedisCacheService
{
    /// <summary>
    ///     Gets an item from the cache by key
    /// </summary>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Sets an item in the cache with optional TTL
    /// </summary>
    ValueTask SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Asynchronously gets the value of a key as a byte array.
    /// </summary>
    /// <param name="key">The key of the value to get.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The value of the key as a byte array, or null if the key does not exist.</returns>
    ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asynchronously sets the value of a key from a byte array.
    /// </summary>
    /// <param name="key">The key of the value to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="ttl">The time-to-live of the key-value pair.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask SetBytesAsync(string key, byte[] value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes an item from the cache by key
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets multiple items from the cache
    /// </summary>
    Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Sets multiple items in the cache with optional TTL
    /// </summary>
    Task SetManyAsync<T>(IDictionary<string, T> values, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    ///     Checks if a key exists in the cache
    /// </summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes a batch of operations.
    /// </summary>
    /// <param name="configureBatch">An action to configure the batch operations.</param>
    /// <returns>A task that represents the asynchronous batch execution. The task result contains the batch result.</returns>
    Task<BatchResult> ExecuteBatchAsync(Action<IBatchOperations> configureBatch);

    /// <summary>
    ///     Sets a prefix for all cache keys
    /// </summary>
    void SetKeyPrefix(string? prefix);
}