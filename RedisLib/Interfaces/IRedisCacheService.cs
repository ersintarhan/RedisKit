namespace RedisLib.Interfaces
{
    /// <summary>
    /// Interface for Redis caching operations with generic support
    /// </summary>
    public interface IRedisCacheService
    {
        /// <summary>
        /// Gets an item from the cache by key
        /// </summary>
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Sets an item in the cache with optional TTL
        /// </summary>
        Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Deletes an item from the cache by key
        /// </summary>
        Task DeleteAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets multiple items from the cache
        /// </summary>
        Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Sets multiple items in the cache with optional TTL
        /// </summary>
        Task SetManyAsync<T>(IDictionary<string, T> values, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Checks if a key exists in the cache
        /// </summary>
        Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a prefix for all cache keys
        /// </summary>
        void SetKeyPrefix(string prefix);
    }
}