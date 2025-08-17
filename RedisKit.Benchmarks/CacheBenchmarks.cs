using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedisKit.Extensions;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;

namespace RedisKit.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CacheBenchmarks : IDisposable
{
    private ServiceProvider _serviceProvider = null!;
    private IRedisCacheService _cacheService = null!;
    private readonly TestData _testObject;
    private readonly Dictionary<string, TestData> _batchData;
    private bool _disposed;

    public CacheBenchmarks()
    {
        _testObject = new TestData
        {
            Id = 1,
            Name = "Benchmark User",
            Email = "benchmark@example.com",
            Age = 30,
            IsActive = true,
            Tags = new[] { "benchmark", "test" }
        };

        _batchData = Enumerable.Range(1, 100)
            .ToDictionary(
                i => $"benchmark:user:{i}",
                i => new TestData
                {
                    Id = i,
                    Name = $"User {i}",
                    Email = $"user{i}@example.com",
                    Age = 20 + (i % 50),
                    IsActive = i % 2 == 0,
                    Tags = new[] { $"user_{i}" }
                });
    }

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Mock Redis for benchmarking (we'll use in-memory mock)
        services.AddRedisServices(options =>
        {
            options.ConnectionString = "localhost:6379"; // Will be mocked
            options.DefaultTtl = TimeSpan.FromMinutes(10);
            options.Serializer = SerializerType.MessagePack;
        });

        _serviceProvider = services.BuildServiceProvider();
        _cacheService = _serviceProvider.GetRequiredService<IRedisCacheService>();

        // Pre-populate cache for read benchmarks
        await _cacheService.SetAsync("benchmark:read:test", _testObject);
        await _cacheService.SetManyAsync(_batchData.Take(10).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    [Benchmark(Baseline = true)]
    public async Task Set_Single_Object()
    {
        await _cacheService.SetAsync("benchmark:single", _testObject, TimeSpan.FromMinutes(5));
    }

    [Benchmark]
    public async Task Get_Single_Object()
    {
        await _cacheService.GetAsync<TestData>("benchmark:read:test");
    }

    [Benchmark]
    public async Task Set_Many_Objects_10()
    {
        var batch = _batchData.Take(10).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        await _cacheService.SetManyAsync(batch, TimeSpan.FromMinutes(5));
    }

    [Benchmark]
    public async Task Get_Many_Objects_10()
    {
        var keys = _batchData.Keys.Take(10);
        await _cacheService.GetManyAsync<TestData>(keys);
    }

    [Benchmark]
    public async Task Set_With_Expiry()
    {
        await _cacheService.SetAsync("benchmark:expiry", _testObject, TimeSpan.FromSeconds(30));
    }

    [Benchmark]
    public async Task Delete_Single()
    {
        await _cacheService.DeleteAsync("benchmark:delete");
    }

    [Benchmark]
    public async Task Exists_Check()
    {
        await _cacheService.ExistsAsync("benchmark:read:test");
    }

    [Benchmark]
    public async Task Set_And_Get_Pipeline()
    {
        await _cacheService.SetAsync("benchmark:pipeline", _testObject);
        await _cacheService.GetAsync<TestData>("benchmark:pipeline");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
            }
            _disposed = true;
        }
    }
}