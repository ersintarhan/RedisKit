using Microsoft.Extensions.Logging;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;
using System.Diagnostics;

namespace RedisKit.Services
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

        // Lua script support detection
        private bool? _supportsLuaScripts = null;
        private bool _useFallbackMode = false;

        // Lua script for SET with EXPIRE
        private const string SetWithExpireScript = @"
            local count = 0
            local ttl = tonumber(ARGV[#ARGV])
            for i=1,#KEYS do
                if redis.call('SET', KEYS[i], ARGV[i], 'EX', ttl) then
                    count = count + 1
                end
            end
            return count
        ";

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

                var value = await _database.StringGetAsync(prefixedKey).ConfigureAwait(false);
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

                var serializedValue = await _serializer.SerializeAsync(value, cancellationToken: cancellationToken).ConfigureAwait(false);
                var expiry = ttl ?? _options.DefaultTtl;

                await _database.StringSetAsync(prefixedKey, serializedValue, expiry).ConfigureAwait(false);

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

                await _database.KeyDeleteAsync(prefixedKey).ConfigureAwait(false);

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
                var values = await _database.StringGetAsync(redisKeys).ConfigureAwait(false);

                // Use StringComparer.Ordinal for better performance
                var result = new Dictionary<string, T?>(keyArray.Length, StringComparer.Ordinal);
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
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Check Lua script support (once)
                if (_supportsLuaScripts == null)
                {
                    _supportsLuaScripts = await CheckLuaScriptSupport();
                }

                // Adaptive chunk size based on data size
                var chunkSize = CalculateOptimalChunkSize(values.Count);
                var processedCount = 0;

                foreach (var chunk in values.Chunk(chunkSize))
                {
                    // Parallel serialization for better performance
                    var serializeTasks = chunk.Select(async kvp =>
                    {
                        if (string.IsNullOrEmpty(kvp.Key))
                            throw new ArgumentException("Key cannot be null or empty", nameof(values));
                        if (kvp.Value == null)
                            throw new ArgumentNullException(nameof(values));

                        var prefixedKey = $"{_keyPrefix}{kvp.Key}";
                        var serialized = await _serializer.SerializeAsync(kvp.Value, cancellationToken).ConfigureAwait(false);
                        return (Key: (RedisKey)prefixedKey, Value: (RedisValue)serialized);
                    }).ToArray();

                    var serializedPairs = await Task.WhenAll(serializeTasks);

                    // Choose strategy based on capabilities and configuration
                    if (_supportsLuaScripts.Value && !_useFallbackMode && expiry != TimeSpan.Zero)
                    {
                        await SetManyWithLuaScript(serializedPairs, expiry);
                    }
                    else
                    {
                        await SetManyWithFallback(serializedPairs, expiry);
                    }

                    processedCount += chunk.Count();

                    // Progress logging for large datasets
                    if (values.Count > 10000 && processedCount % 5000 == 0)
                    {
                        Logging.LoggingExtensions.LogSetManyAsyncProgress(_logger, processedCount, values.Count);
                    }
                }

                stopwatch.Stop();
                Logging.LoggingExtensions.LogSetManyAsyncSuccess(_logger, values.Count);

                // Performance monitoring
                if (stopwatch.ElapsedMilliseconds > 1000)
                {
                    Logging.LoggingExtensions.LogSlowSetManyAsync(_logger, values.Count, stopwatch.ElapsedMilliseconds,
                        _supportsLuaScripts.Value ? "Lua" : "Fallback");
                }
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogSetManyAsyncError(_logger, ex);
                throw;
            }
        }

        private async Task<bool> CheckLuaScriptSupport()
        {
            try
            {
                // Test with a simple Lua script
                var testScript = "return redis.call('PING')";
                var result = await _database.ScriptEvaluateAsync(testScript).ConfigureAwait(false);
                var supported = result.ToString() == "PONG";

                if (supported)
                {
                    Logging.LoggingExtensions.LogLuaScriptSupported(_logger);
                }
                else
                {
                    Logging.LoggingExtensions.LogLuaScriptTestFailed(_logger);
                }

                return supported;
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogLuaScriptNotSupported(_logger, ex);
                return false;
            }
        }

        private int CalculateOptimalChunkSize(int totalCount)
        {
            return totalCount switch
            {
                < 100 => totalCount, // No chunking for small sets
                < 1000 => 500, // Medium chunk for medium sets
                < 10000 => 1000, // Standard chunk for large sets
                _ => 2000 // Large chunk for very large sets
            };
        }

        private async Task SetManyWithLuaScript((RedisKey Key, RedisValue Value)[] pairs, TimeSpan expiry)
        {
            try
            {
                var keys = pairs.Select(p => p.Key).ToArray();
                var values = pairs.Select(p => p.Value)
                    .Concat(new[] { (RedisValue)expiry.TotalSeconds })
                    .ToArray();

                var result = await _database.ScriptEvaluateAsync(SetWithExpireScript, keys, values).ConfigureAwait(false);

                var successCount = (int)result;
                if (successCount != pairs.Length)
                {
                    Logging.LoggingExtensions.LogSetManyPartialSuccess(_logger, pairs.Length, successCount);
                }
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOSCRIPT"))
            {
                // Script not in cache, switch to fallback
                Logging.LoggingExtensions.LogLuaScriptNotInCache(_logger);
                _useFallbackMode = true;
                await SetManyWithFallback(pairs, expiry);
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogLuaScriptExecutionFailed(_logger, ex);
                _useFallbackMode = true;
                await SetManyWithFallback(pairs, expiry);
            }
        }

        private async Task SetManyWithFallback((RedisKey Key, RedisValue Value)[] pairs, TimeSpan? expiry)
        {
            if (expiry == TimeSpan.Zero || expiry == null)
            {
                // No TTL: use MSET for single round-trip
                var kvpArray = pairs
                    .Select(p => new KeyValuePair<RedisKey, RedisValue>(p.Key, p.Value))
                    .ToArray();
                await _database.StringSetAsync(kvpArray).ConfigureAwait(false);
            }
            else
            {
                // With TTL: MSET followed by parallel EXPIRE
                var kvpArray = pairs
                    .Select(p => new KeyValuePair<RedisKey, RedisValue>(p.Key, p.Value))
                    .ToArray();

                // First: MSET
                await _database.StringSetAsync(kvpArray).ConfigureAwait(false);

                // Then: parallel EXPIRE to minimize round-trips
                var expireTasks = pairs
                    .Select(p => _database.KeyExpireAsync(p.Key, expiry.Value))
                    .ToArray();
                await Task.WhenAll(expireTasks);
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

                var result = await _database.KeyExistsAsync(prefixedKey).ConfigureAwait(false);

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