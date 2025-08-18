namespace RedisKit.Interfaces;

/// <summary>
/// Defines the operations that can be performed in a batch.
/// </summary>
public interface IBatchOperations
{
    /// <summary>
    /// Adds a get operation to the batch.
    /// </summary>
    /// <typeparam name="T">The type of the expected value.</typeparam>
    /// <param name="key">The key of the value to get.</param>
    /// <returns>A task that represents the asynchronous get operation. The task result contains the value of the key.</returns>
    Task<T?> GetAsync<T>(string key) where T : class;

    /// <summary>
    /// Adds a set operation to the batch.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key of the value to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="ttl">The time-to-live of the key-value pair.</param>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class;

    /// <summary>
    /// Adds a delete operation to the batch.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>A task that represents the asynchronous delete operation. The task result contains true if the key was deleted, otherwise false.</returns>
    Task<bool> DeleteAsync(string key);

    /// <summary>
    /// Adds an exists operation to the batch.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>A task that represents the asynchronous exists operation. The task result contains true if the key exists, otherwise false.</returns>
    Task<bool> ExistsAsync(string key);
}
