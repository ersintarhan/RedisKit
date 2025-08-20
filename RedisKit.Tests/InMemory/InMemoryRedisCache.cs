using System.Collections.Concurrent;
using RedisKit.Interfaces;
using RedisKit.Models;

namespace RedisKit.Tests.InMemory;

/// <summary>
///     In-memory implementation of IRedisCacheService for testing
/// </summary>
public class InMemoryRedisCache : IRedisCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private string _keyPrefix = "";

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        var fullKey = $"{_keyPrefix}{key}";

        if (_cache.TryGetValue(fullKey, out var entry))
        {
            if (!IsExpired(entry)) return new ValueTask<T?>((T?)entry.Value);
            _cache.TryRemove(fullKey, out _);
        }

        return new ValueTask<T?>(default(T?));
    }

    public ValueTask SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var fullKey = $"{_keyPrefix}{key}";
        var entry = new CacheEntry
        {
            Value = value,
            ExpiryTime = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null
        };

        _cache[fullKey] = entry;
        return ValueTask.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var fullKey = $"{_keyPrefix}{key}";
        _cache.TryRemove(fullKey, out _);
        return Task.CompletedTask;
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var fullKey = $"{_keyPrefix}{key}";

        if (_cache.TryGetValue(fullKey, out var entry))
        {
            if (!IsExpired(entry)) return new ValueTask<bool>(true);
            _cache.TryRemove(fullKey, out _);
        }

        return new ValueTask<bool>(false);
    }

    public Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        var result = new Dictionary<string, T?>();

        foreach (var key in keys)
        {
            var fullKey = $"{_keyPrefix}{key}";
            if (_cache.TryGetValue(fullKey, out var entry) && !IsExpired(entry))
                result[key] = (T?)entry.Value;
            else
                result[key] = null;
        }

        return Task.FromResult(result);
    }

    public Task SetManyAsync<T>(IDictionary<string, T> values, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        foreach (var kvp in values)
        {
            var fullKey = $"{_keyPrefix}{kvp.Key}";
            _cache[fullKey] = new CacheEntry
            {
                Value = kvp.Value,
                ExpiryTime = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null
            };
        }

        return Task.CompletedTask;
    }

    public ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var fullKey = $"{_keyPrefix}{key}";

        if (_cache.TryGetValue(fullKey, out var entry) && !IsExpired(entry)) return new ValueTask<byte[]?>(entry.Bytes);

        return new ValueTask<byte[]?>(default(byte[]?));
    }

    public ValueTask SetBytesAsync(string key, byte[] value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var fullKey = $"{_keyPrefix}{key}";
        _cache[fullKey] = new CacheEntry
        {
            Bytes = value,
            ExpiryTime = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null
        };

        return ValueTask.CompletedTask;
    }

    public void SetKeyPrefix(string? prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        _keyPrefix = prefix;
    }

    public Task<BatchResult> ExecuteBatchAsync(Action<IBatchOperations> configureBatch)
    {
        var batch = new InMemoryBatchOperations(this);
        configureBatch(batch);
        return batch.ExecuteAsync();
    }

    private static bool IsExpired(CacheEntry entry)
    {
        return entry.ExpiryTime.HasValue && entry.ExpiryTime.Value <= DateTime.UtcNow;
    }

    public void Clear()
    {
        _cache.Clear();
    }

    private class CacheEntry
    {
        public object? Value { get; set; }
        public byte[]? Bytes { get; set; }
        public DateTime? ExpiryTime { get; set; }
    }

    // Helper class for batch operations
    private class InMemoryBatchOperations : IBatchOperations
    {
        private readonly InMemoryRedisCache _cache;
        private readonly List<Func<Task>> _operations = new();

        public InMemoryBatchOperations(InMemoryRedisCache cache)
        {
            _cache = cache;
        }

        public Task<T?> GetAsync<T>(string key) where T : class
        {
            var tcs = new TaskCompletionSource<T?>();
            _operations.Add(async () =>
            {
                var result = await _cache.GetAsync<T>(key);
                tcs.SetResult(result);
            });
            return tcs.Task;
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
        {
            _operations.Add(() => _cache.SetAsync(key, value, ttl).AsTask());
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string key)
        {
            var tcs = new TaskCompletionSource<bool>();
            _operations.Add(async () =>
            {
                await _cache.DeleteAsync(key);
                tcs.SetResult(true);
            });
            return tcs.Task;
        }

        public Task<bool> ExistsAsync(string key)
        {
            var tcs = new TaskCompletionSource<bool>();
            _operations.Add(async () =>
            {
                var result = await _cache.ExistsAsync(key);
                tcs.SetResult(result);
            });
            return tcs.Task;
        }

        public async Task<BatchResult> ExecuteAsync()
        {
            var tasks = _operations.Select(op => op()).ToArray();
            await Task.WhenAll(tasks);
            return new BatchResult(true);
        }
    }
}