using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RedisKit.Extensions;
using RedisKit.Helpers;
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

    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly AsyncLazy<bool> _luaScriptSupport;
    private readonly RedisOptions _options;
    private readonly IRedisSerializer _serializer;
    private readonly ObjectPool<List<Task>>? _taskListPool;
    private string _keyPrefix = string.Empty;

    public RedisCacheService(
        IRedisConnection connection,
        ILogger<RedisCacheService> logger,
        IOptions<RedisOptions> options,
        ObjectPoolProvider? poolProvider = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Create the serializer based on configuration
        // In a future refactor, IRedisSerializer should be injected directly.
        _serializer = RedisSerializerFactory.Create(_options.Serializer);

        if (_options.Pooling.Enabled)
        {
            var provider = poolProvider ?? new DefaultObjectPoolProvider();
            if (provider is DefaultObjectPoolProvider defaultProvider) defaultProvider.MaximumRetained = _options.Pooling.MaxPoolSize;
            _taskListPool = provider.Create<List<Task>>();
        }

        // Initialize Lua script support detection lazily
        _luaScriptSupport = new AsyncLazy<bool>(async () =>
        {
            try
            {
                var database = await GetDatabaseAsync().ConfigureAwait(false);
                var result = await database.ScriptEvaluateAsync("return 'PONG'").ConfigureAwait(false);
                var supported = result?.ToString() == "PONG";
                _logger.LogDebug("Lua script support detected: {Supported}", supported);
                return supported;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lua script support check failed, using fallback mode");
                return false;
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }


    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        return await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                _logger.LogGetAsync(prefixedKey);
                var database = await GetDatabaseAsync();

                var value = await database.StringGetAsync(prefixedKey).ConfigureAwait(false);
                if (value.IsNullOrEmpty)
                    return null;

                var result = await _serializer.DeserializeAsync<T>((ReadOnlyMemory<byte>)value, cancellationToken);
                _logger.LogGetAsyncSuccess(prefixedKey);
                return result;
            },
            _logger,
            prefixedKey,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public async ValueTask SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                _logger.LogSetAsync(prefixedKey);
                var database = await GetDatabaseAsync();

                using var buffer = new SafeSerializationBuffer();
                var length = await _serializer.SerializeAsync(value, buffer.Buffer, cancellationToken).ConfigureAwait(false);
                var serializedValue = new ReadOnlyMemory<byte>(buffer.Buffer, 0, length);

                var expiry = ttl ?? _options.DefaultTtl;

                await database.StringSetAsync(prefixedKey, serializedValue, expiry).ConfigureAwait(false);

                _logger.LogSetAsyncSuccess(prefixedKey);
            },
            _logger,
            prefixedKey,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                _logger.LogDeleteAsync(prefixedKey);
                var database = await GetDatabaseAsync();

                await database.KeyDeleteAsync(prefixedKey).ConfigureAwait(false);

                _logger.LogDeleteAsyncSuccess(prefixedKey);
            },
            _logger,
            prefixedKey,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(keys);

        var originalKeys = keys.ToList();
        if (originalKeys.Count == 0) return new Dictionary<string, T?>();

        var prefixedKeys = originalKeys.Select(k => (RedisKey)$"{_keyPrefix}{k}").ToArray();

        using var pooledTasks = _taskListPool!.GetPooled();
        var deserializationTasks = pooledTasks.Value;
        var resultsByIndex = new ConcurrentDictionary<int, T?>();

        try
        {
            _logger.LogGetManyAsync(string.Join(", ", originalKeys));
            var database = await GetDatabaseAsync();

            var values = await database.StringGetAsync(prefixedKeys).ConfigureAwait(false);

            var result = new Dictionary<string, T?>(originalKeys.Count, StringComparer.Ordinal);

            for (var i = 0; i < originalKeys.Count; i++)
            {
                var index = i;
                if (values[index].IsNullOrEmpty)
                    resultsByIndex[index] = null;
                else
                    deserializationTasks.Add(Task.Run(async () =>
                    {
                        var deserialized = await _serializer.DeserializeAsync<T>(values[index]!, cancellationToken);
                        resultsByIndex[index] = deserialized;
                    }, cancellationToken));
            }

            await Task.WhenAll(deserializationTasks);

            for (var i = 0; i < originalKeys.Count; i++) result[originalKeys[i]] = resultsByIndex[i];

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
                _logger.LogSetManyBatchSizeWarning(values.Count, RedisConstants.DefaultBatchSizeThreshold);

            _logger.LogSetManyAsync(values.Count);
            var stopwatch = Stopwatch.StartNew();

            // Lua support will be checked lazily when needed

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

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";

        try
        {
            _logger.LogExistsAsync(prefixedKey);
            var database = await GetDatabaseAsync();

            var result = await database.KeyExistsAsync(prefixedKey).ConfigureAwait(false);

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

    public async ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";
        var db = await GetDatabaseAsync();
        return await db.StringGetAsync(prefixedKey);
    }

    public async ValueTask SetBytesAsync(string key, byte[] value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ValidateRedisKey(key);
        var prefixedKey = $"{_keyPrefix}{key}";
        var db = await GetDatabaseAsync();
        await db.StringSetAsync(prefixedKey, value, ttl ?? _options.DefaultTtl);
    }

    // Override SetKeyPrefix to validate prefix
    public void SetKeyPrefix(string prefix)
    {
        ValidateRedisKey(prefix);
        _keyPrefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
    }

    public async Task<BatchResult> ExecuteBatchAsync(Action<IBatchOperations> configureBatch)
    {
        var multiplexer = await _connection.GetMultiplexerAsync();
        var database = multiplexer.GetDatabase();
        var batch = database.CreateBatch();
        var operations = new BatchOperations(batch, _serializer);

        configureBatch(operations);

        batch.Execute();

        return await operations.GetResultsAsync();
    }

    // Add validation to protect against too-large keys
    private static void ValidateRedisKey(string key)
    {
        // Redis keys can't be longer than 512 MB (but practically less)
        if (!string.IsNullOrEmpty(key) && key.Length > RedisConstants.MaxRedisKeyLength) throw new ArgumentException($"Redis key exceeds maximum allowed length of {RedisConstants.MaxRedisKeyLength}", nameof(key));
    }


    private async Task<bool> IsLuaSupportedAsync()
    {
        return await _luaScriptSupport.Value.ConfigureAwait(false);
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
        using var pooledTasks = _taskListPool!.GetPooled();
        var tasks = pooledTasks.Value;

        tasks.AddRange(chunk.Select(async kvp =>
        {
            ValidateKeyValuePair(kvp);

            var prefixedKey = $"{_keyPrefix}{kvp.Key}";
            var serialized = await _serializer.SerializeAsync(kvp.Value, cancellationToken);
            return ((RedisKey)prefixedKey, (RedisValue)serialized);
        }));

        var results = await Task.WhenAll(tasks.Cast<Task<(RedisKey Key, RedisValue Value)>>());
        return results;
    }

    private static void ValidateKeyValuePair<T>(KeyValuePair<string, T> kvp) where T : class
    {
        ArgumentNullException.ThrowIfNull(kvp.Key);
        ArgumentNullException.ThrowIfNull(kvp.Value);

        if (string.IsNullOrEmpty(kvp.Key))
            throw new ArgumentException("Key cannot be null or empty");
    }

    private async Task SetChunkAsync((RedisKey Key, RedisValue Value)[] serializedPairs, TimeSpan expiry)
    {
        var luaSupported = await IsLuaSupportedAsync().ConfigureAwait(false);
        if (luaSupported && expiry != TimeSpan.Zero)
            await SetManyWithLuaScript(serializedPairs, expiry);
        else
            await SetManyWithFallback(serializedPairs, expiry);
    }

    private void LogProgressIfNeeded(int totalCount, int processedCount)
    {
        if (totalCount > RedisConstants.DefaultBatchSizeThreshold
            && processedCount % (RedisConstants.DefaultBatchSizeThreshold / 2) == 0)
            _logger.LogSetManyAsyncProgress(processedCount, totalCount);
    }

    private void LogPerformanceIfSlow(Stopwatch stopwatch, int count)
    {
        if (stopwatch.ElapsedMilliseconds > RedisConstants.SlowOperationThreshold)
            _logger.LogSlowSetManyAsync(count, stopwatch.ElapsedMilliseconds,
                _luaScriptSupport.GetValueOrDefault() ? "Lua" : "Fallback");
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
            var database = await GetDatabaseAsync();
            var keys = pairs.Select(p => p.Key).ToArray();
            var values = pairs.Select(p => p.Value).Concat(new RedisValue[] { expiry.TotalSeconds }).ToArray();
            var result = await database.ScriptEvaluateAsync(SetWithExpireScript, keys, values).ConfigureAwait(false);
            var successCount = (int?)result ?? 0;
            if (successCount != pairs.Length)
                _logger.LogSetManyPartialSuccess(pairs.Length, successCount);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOSCRIPT"))
        {
            _logger.LogLuaScriptNotInCache();
            // Fallback to non-Lua implementation
            await SetManyWithFallback(pairs, expiry);
        }
        catch (Exception ex) when (ex is RedisConnectionException ||
                                   ex is RedisTimeoutException ||
                                   ex is RedisServerException)
        {
            _logger.LogLuaScriptExecutionFailed(ex);
            // Fallback to non-Lua implementation
            await SetManyWithFallback(pairs, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogLuaScriptExecutionFailed(ex);
            throw;
        }
    }

    private async Task SetManyWithFallback((RedisKey Key, RedisValue Value)[] pairs, TimeSpan? expiry)
    {
        // Get the synchronous IDatabase interface to create a batch
        var database = (await _connection.GetMultiplexerAsync()).GetDatabase();

        if (expiry == TimeSpan.Zero || expiry == null)
        {
            // No TTL: use MSET for single round-trip
            var kvpArray = pairs
                .Select(p => new KeyValuePair<RedisKey, RedisValue>(p.Key, p.Value))
                .ToArray();
            await database.StringSetAsync(kvpArray).ConfigureAwait(false);
        }
        else
        {
            // With TTL: use a batch to pipeline MSET and EXPIRE commands
            var batch = database.CreateBatch();

            var kvpArray = pairs
                .Select(p => new KeyValuePair<RedisKey, RedisValue>(p.Key, p.Value))
                .ToArray();

            // First: MSET
            var msetTask = batch.StringSetAsync(kvpArray);

            // Then: parallel EXPIRE to minimize round-trips
            using var pooledTasks = _taskListPool!.GetPooled();
            var expireTasks = pooledTasks.Value;
            expireTasks.AddRange(pairs.Select(pair => batch.KeyExpireAsync(pair.Key, expiry.Value)));
            batch.Execute();
            await Task.WhenAll(expireTasks.Append(msetTask));
        }
    }

    private Task<IDatabaseAsync> GetDatabaseAsync()
    {
        return _connection.GetDatabaseAsync();
    }
}