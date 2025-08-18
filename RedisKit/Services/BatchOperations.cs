using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;

namespace RedisKit.Services;

internal class BatchOperations : IBatchOperations
{
    private readonly IBatch _batch;
    private readonly IRedisSerializer _serializer;
    private readonly List<Task> _tasks = new();

    public BatchOperations(IBatch batch, IRedisSerializer serializer)
    {
        _batch = batch;
        _serializer = serializer;
    }

    public Task<T?> GetAsync<T>(string key) where T : class
    {
        var task = _batch.StringGetAsync(key);
        var tcs = new TaskCompletionSource<T?>();
        _tasks.Add(task.ContinueWith(async t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                var value = t.Result;
                if (value.IsNullOrEmpty)
                {
                    tcs.SetResult(default);
                }
                else
                {
                    var deserialized = await _serializer.DeserializeAsync<T>((ReadOnlyMemory<byte>)value);
                    tcs.SetResult(deserialized);
                }
            }
            else
            {
                tcs.SetException(t.Exception!.InnerExceptions);
            }
        }));
        return tcs.Task;
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
    {
        var serializedValue = _serializer.Serialize(value);
        return _batch.StringSetAsync(key, serializedValue, ttl);
    }

    public Task<bool> DeleteAsync(string key)
    {
        return _batch.KeyDeleteAsync(key);
    }

    public Task<bool> ExistsAsync(string key)
    {
        return _batch.KeyExistsAsync(key);
    }

    public async Task<BatchResult> GetResultsAsync()
    {
        try
        {
            await Task.WhenAll(_tasks);
            return new BatchResult(true);
        }
        catch
        {
            return new BatchResult(false);
        }
    }
}
