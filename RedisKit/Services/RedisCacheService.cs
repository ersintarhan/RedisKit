using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RedisKit.Interfaces;
using RedisKit.Logging;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     Implementation of IRedisCacheService using StackExchange.Redis and configurable serialization
/// </summary>
public class RedisCacheService : IRedisCacheService
{
    private const string ValueCannotBeNullOrEmpty = "Value cannot be null or empty";

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

    private readonly IDatabaseAsync _database;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly RedisOptions _options;
    private readonly IRedisSerializer _serializer;
    private string _keyPrefix = string.Empty;

    // Lua script support detection
    private bool? _supportsLuaScripts;
    private readonly SemaphoreSlim _luaScriptDetectionLock = new SemaphoreSlim(1, 1);

    private bool _useFallbackMode;

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


    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        try
        {
            _logger.LogGetAsync(prefixedKey);

            var value = await _database.StringGetAsync(prefixedKey).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
                return null;

            var result = await _serializer.DeserializeAsync<T>(value!, cancellationToken);
            _logger.LogGetAsyncSuccess(prefixedKey);
            return result;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogGetAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogGetAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisServerException ex)
        {
            _logger.LogGetAsyncError(prefixedKey, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogGetAsyncError(prefixedKey, ex);
            throw;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        try
        {
            _logger.LogSetAsync(prefixedKey);

            var serializedValue = await _serializer.SerializeAsync(value, cancellationToken).ConfigureAwait(false);
            var expiry = ttl ?? _options.DefaultTtl;

            await _database.StringSetAsync(prefixedKey, serializedValue, expiry).ConfigureAwait(false);

            _logger.LogSetAsyncSuccess(prefixedKey);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogSetAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogSetAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisServerException ex)
        {
            _logger.LogSetAsyncError(prefixedKey, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogSetAsyncError(prefixedKey, ex);
            throw;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        try
        {
            _logger.LogDeleteAsync(prefixedKey);

            await _database.KeyDeleteAsync(prefixedKey).ConfigureAwait(false);

            _logger.LogDeleteAsyncSuccess(prefixedKey);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogDeleteAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogDeleteAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisServerException ex)
        {
            _logger.LogDeleteAsyncError(prefixedKey, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDeleteAsyncError(prefixedKey, ex);
            throw;
        }
    }

    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(keys);

        // Materialize the keys once to avoid multiple enumeration
        var keyArray = keys.ToArray();
        var prefixedKeys = keyArray.Select(k => $"{_keyPrefix}{k}").ToArray();

        try
        {
            _logger.LogGetManyAsync(string.Join(", ", prefixedKeys));

            var redisKeys = prefixedKeys.Select(k => (RedisKey)k).ToArray();
            var values = await _database.StringGetAsync(redisKeys).ConfigureAwait(false);

            // Use StringComparer.Ordinal for better performance
            var result = new Dictionary<string, T?>(keyArray.Length, StringComparer.Ordinal);
            for (var i = 0; i < prefixedKeys.Length; i++)
            {
                // Use the original key (without prefix) as dictionary key
                var originalKey = keyArray[i];
                if (values[i].IsNullOrEmpty)
                    result[originalKey] = null;
                else
                    result[originalKey] = await _serializer.DeserializeAsync<T>(values[i]!, cancellationToken);
            }

            _logger.LogGetManyAsyncSuccess(result.Count);
            return result;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogGetManyAsyncError(ex);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogGetManyAsyncError(ex);
            throw;
        }
        catch (RedisServerException ex)
        {
            _logger.LogGetManyAsyncError(ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogGetManyAsyncError(ex);
            throw;
        }
    }

    public async Task SetManyAsync<T>(IDictionary<string, T> values, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(values);

        var expiry = ttl ?? _options.DefaultTtl;

        try
        {
            // Validate input to prevent potential DoS attacks
            if (values.Count > RedisConstants.DefaultBatchSizeThreshold) // Limit batch size to prevent memory issues
            {
                _logger.LogSetManyBatchSizeWarning(values.Count, RedisConstants.DefaultBatchSizeThreshold);
            }

            _logger.LogSetManyAsync(values.Count);
            var stopwatch = Stopwatch.StartNew();

            await EnsureLuaScriptSupportChecked();

            await ProcessBatchesAsync(values, expiry, cancellationToken);

            stopwatch.Stop();
            _logger.LogSetManyAsyncSuccess(values.Count);

            LogPerformanceIfSlow(stopwatch, values.Count);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogSetManyAsyncError(ex);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogSetManyAsyncError(ex);
            throw;
        }
        catch (RedisServerException ex)
        {
            _logger.LogSetManyAsyncError(ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogSetManyAsyncError(ex);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        try
        {
            _logger.LogExistsAsync(prefixedKey);

            var result = await _database.KeyExistsAsync(prefixedKey).ConfigureAwait(false);

            _logger.LogExistsAsyncSuccess(prefixedKey, result);
            return result;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogExistsAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogExistsAsyncError(prefixedKey, ex);
            throw;
        }
        catch (RedisServerException ex)
        {
            _logger.LogExistsAsyncError(prefixedKey, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogExistsAsyncError(prefixedKey, ex);
            throw;
        }
    }

    // Add validation to protect against too-large keys
    private static void ValidateRedisKey(string key)
    {
        // Redis keys can't be longer than 512 MB (but practically less)
        if (!string.IsNullOrEmpty(key) && key.Length > RedisConstants.MaxRedisKeyLength)
        {
            throw new ArgumentException($"Redis key exceeds maximum allowed length of {RedisConstants.MaxRedisKeyLength}", nameof(key));
        }
    }

    // Override SetKeyPrefix to validate prefix
    public void SetKeyPrefix(string prefix)
    {
        ValidateRedisKey(prefix);
        _keyPrefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
    }


    private async Task EnsureLuaScriptSupportChecked()
    {
        if (_supportsLuaScripts == null)
        {
            await _luaScriptDetectionLock.WaitAsync();
            try
            {
                _supportsLuaScripts ??= await CheckLuaScriptSupport();
            }
            finally
            {
                _luaScriptDetectionLock.Release();
            }
        }
    }

    private async Task ProcessBatchesAsync<T>(IDictionary<string, T> values, TimeSpan expiry, CancellationToken cancellationToken)
        where T : class
    {
        var chunkSize = CalculateOptimalChunkSize(values.Count);
        var processedCount = 0;

        foreach (var chunk in values.Chunk(chunkSize))
        {
            var serializedPairs = await SerializeChunkAsync(chunk, cancellationToken);
            await SetChunkAsync(serializedPairs, expiry);
            processedCount += chunk.Length;
            LogProgressIfNeeded(values.Count, processedCount);
        }
    }

    private async Task<(RedisKey Key, RedisValue Value)[]> SerializeChunkAsync<T>(
        IEnumerable<KeyValuePair<string, T>> chunk, CancellationToken cancellationToken)
        where T : class
    {
        var tasks = chunk.Select(async kvp =>
        {
            ValidateKeyValuePair(kvp);

            var prefixedKey = $"{_keyPrefix}{kvp.Key}";
            var serialized = await _serializer.SerializeAsync(kvp.Value, cancellationToken);
            return ((RedisKey)prefixedKey, (RedisValue)serialized);
        });

        return await Task.WhenAll(tasks);
    }

    private static void ValidateKeyValuePair<T>(KeyValuePair<string, T> kvp) where T : class
    {
        ArgumentNullException.ThrowIfNull(kvp.Key);
        ArgumentNullException.ThrowIfNull(kvp.Value);

        if (string.IsNullOrEmpty(kvp.Key))
            throw new ArgumentException("Key cannot be null or empty");
        if (kvp.Value is not null)
            throw new ArgumentNullException(nameof(kvp), ValueCannotBeNullOrEmpty);
    }

    private async Task SetChunkAsync((RedisKey Key, RedisValue Value)[] serializedPairs, TimeSpan expiry)
    {
        if (_supportsLuaScripts.GetValueOrDefault() && !_useFallbackMode && expiry != TimeSpan.Zero)
            await SetManyWithLuaScript(serializedPairs, expiry);
        else
            await SetManyWithFallback(serializedPairs, expiry);
    }

    private void LogProgressIfNeeded(int totalCount, int processedCount)
    {
        if (totalCount > RedisConstants.DefaultBatchSizeThreshold
            && processedCount % (RedisConstants.DefaultBatchSizeThreshold / 2) == 0)
        {
            _logger.LogSetManyAsyncProgress(processedCount, totalCount);
        }
    }

    private void LogPerformanceIfSlow(Stopwatch stopwatch, int count)
    {
        if (stopwatch.ElapsedMilliseconds > RedisConstants.SlowOperationThreshold)
            _logger.LogSlowSetManyAsync(count, stopwatch.ElapsedMilliseconds,
                _supportsLuaScripts.GetValueOrDefault() ? "Lua" : "Fallback");
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
                _logger.LogLuaScriptSupported();
            else
                _logger.LogLuaScriptTestFailed();

            return supported;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogLuaScriptNotSupported(ex);
            return false;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogLuaScriptNotSupported(ex);
            return false;
        }
        catch (RedisServerException ex)
        {
            _logger.LogLuaScriptNotSupported(ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogLuaScriptNotSupported(ex);
            return false;
        }
    }

    private static int CalculateOptimalChunkSize(int totalCount)
    {
        return totalCount switch
        {
            < RedisConstants.SmallBatchThreshold => totalCount, // No chunking for small sets
            < RedisConstants.MediumBatchThreshold => RedisConstants.SmallBatchThreshold, // Medium chunk for medium sets
            < RedisConstants.DefaultBatchSizeThreshold => RedisConstants.MediumBatchThreshold, // Standard chunk for large sets
            _ => RedisConstants.DefaultBatchSizeThreshold // Large chunk for very large sets
        };
    }

    private async Task SetManyWithLuaScript((RedisKey Key, RedisValue Value)[] pairs, TimeSpan expiry)
    {
        try
        {
            var keys = pairs.Select(p => p.Key).ToArray();
            var values = pairs.Select(p => p.Value).Concat([(RedisValue)expiry.TotalSeconds]).ToArray();
            var result = await _database.ScriptEvaluateAsync(SetWithExpireScript, keys, values).ConfigureAwait(false);
            var successCount = Convert.ToInt32(result);
            if (successCount != pairs.Length)
                _logger.LogSetManyPartialSuccess(pairs.Length, successCount);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOSCRIPT"))
        {
            _logger.LogLuaScriptNotInCache();
            _useFallbackMode = true;
            await SetManyWithFallback(pairs, expiry);
        }
        catch (Exception ex) when (ex is RedisConnectionException ||
                                   ex is RedisTimeoutException ||
                                   ex is RedisServerException)
        {
            _logger.LogLuaScriptExecutionFailed(ex);
            _useFallbackMode = true;
            await SetManyWithFallback(pairs, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogLuaScriptExecutionFailed(ex);
            _useFallbackMode = true;
            throw;
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
}