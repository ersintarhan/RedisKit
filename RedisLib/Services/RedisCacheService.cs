using Microsoft.Extensions.Logging;
using RedisLib.Interfaces;
using RedisLib.Models;
using RedisLib.Serialization;
using StackExchange.Redis;

namespace RedisLib.Services
{
    /// <summary>
    /// Implementation of IRedisCacheService using StackExchange.Redis and configurable serialization
    /// </summary>
    public class RedisCacheService : IRedisCacheService
    {
        private readonly IDatabaseAsync _database;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly RedisOptions _options;
        private readonly IRedisSerializer _serializer;
        private string _keyPrefix = string.Empty;

        public RedisCacheService(
            IDatabaseAsync database,
            ILogger<RedisCacheService> logger,
            RedisOptions options)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // Create the serializer based on configuration
            _serializer = RedisSerializerFactory.Create(_options.Serializer);
        }

        public void SetKeyPrefix(string prefix)
        {
            _keyPrefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var prefixedKey = $"{_keyPrefix}{key}";

            try
            {
                Logging.LoggingExtensions.LogGetAsync(_logger, prefixedKey);

                var value = await _database.StringGetAsync(prefixedKey);
                if (value.IsNullOrEmpty)
                    return null;

                var result = await _serializer.DeserializeAsync<T>(value!, cancellationToken: cancellationToken);
                Logging.LoggingExtensions.LogGetAsyncSuccess(_logger, prefixedKey);
                return result;
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogGetAsyncError(_logger, prefixedKey, ex);
                throw;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var prefixedKey = $"{_keyPrefix}{key}";

            try
            {
                Logging.LoggingExtensions.LogSetAsync(_logger, prefixedKey);

                var serializedValue = await _serializer.SerializeAsync(value, cancellationToken: cancellationToken);
                var expiry = ttl ?? _options.DefaultTtl;

                await _database.StringSetAsync(prefixedKey, serializedValue, expiry);

                Logging.LoggingExtensions.LogSetAsyncSuccess(_logger, prefixedKey);
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogSetAsyncError(_logger, prefixedKey, ex);
                throw;
            }
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var prefixedKey = $"{_keyPrefix}{key}";

            try
            {
                Logging.LoggingExtensions.LogDeleteAsync(_logger, prefixedKey);

                await _database.KeyDeleteAsync(prefixedKey);

                Logging.LoggingExtensions.LogDeleteAsyncSuccess(_logger, prefixedKey);
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogDeleteAsyncError(_logger, prefixedKey, ex);
                throw;
            }
        }

        public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
        {
            if (keys == null)
                throw new ArgumentNullException(nameof(keys));

            // Materialize the keys once to avoid multiple enumeration
            var keyArray = keys.ToArray();
            var prefixedKeys = keyArray.Select(k => $"{_keyPrefix}{k}").ToArray();

            try
            {
                Logging.LoggingExtensions.LogGetManyAsync(_logger, string.Join(", ", prefixedKeys));

                var redisKeys = prefixedKeys.Select(k => (RedisKey)k).ToArray();
                var values = await _database.StringGetAsync(redisKeys);

                var result = new Dictionary<string, T?>();
                for (int i = 0; i < prefixedKeys.Length; i++)
                {
                    // Use the original key (without prefix) as dictionary key
                    var originalKey = keyArray[i];
                    if (values[i].IsNullOrEmpty)
                        result[originalKey] = null;
                    else
                        result[originalKey] = await _serializer.DeserializeAsync<T>(values[i]!, cancellationToken: cancellationToken);
                }

                Logging.LoggingExtensions.LogGetManyAsyncSuccess(_logger, result.Count);
                return result;
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogGetManyAsyncError(_logger, ex);
                throw;
            }
        }

        public async Task SetManyAsync<T>(IDictionary<string, T> values, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            var expiry = ttl ?? _options.DefaultTtl;

            try
            {
                Logging.LoggingExtensions.LogSetManyAsync(_logger, values.Count);

                // For better performance, we'll use parallel execution instead of batch
                var tasks = new List<Task>();

                foreach (var kvp in values)
                {
                    if (string.IsNullOrEmpty(kvp.Key))
                        throw new ArgumentException("Key cannot be null or empty", nameof(values));

                    if (kvp.Value == null)
                        throw new ArgumentNullException(nameof(values));

                    var prefixedKey = $"{_keyPrefix}{kvp.Key}";
                    var serializedValue = await _serializer.SerializeAsync(kvp.Value, cancellationToken: cancellationToken);

                    // Execute operations in parallel
                    var task = _database.StringSetAsync(prefixedKey, serializedValue, expiry);
                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);

                Logging.LoggingExtensions.LogSetManyAsyncSuccess(_logger, values.Count);
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogSetManyAsyncError(_logger, ex);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var prefixedKey = $"{_keyPrefix}{key}";

            try
            {
                Logging.LoggingExtensions.LogExistsAsync(_logger, prefixedKey);

                var result = await _database.KeyExistsAsync(prefixedKey);

                Logging.LoggingExtensions.LogExistsAsyncSuccess(_logger, prefixedKey, result);
                return result;
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogExistsAsyncError(_logger, prefixedKey, ex);
                throw;
            }
        }
    }
}