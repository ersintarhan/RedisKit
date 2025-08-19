using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedisKit.Extensions;
using RedisKit.Interfaces;
using RedisKit.Models;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests.Integration;

/// <summary>
///     Base class for integration tests with real Redis instance
/// </summary>
[Trait("Category", "Integration")]
[Collection("Integration")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly List<string> _functionsToClean = new();

    private readonly List<string> _keysToClean = new();
    private readonly List<string> _streamsToClean = new();
    protected IServiceProvider ServiceProvider { get; private set; } = null!;
    protected IConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;
    protected IDatabase Database { get; private set; } = null!;
    protected IRedisCacheService CacheService { get; private set; } = null!;
    protected IRedisPubSubService PubSubService { get; private set; } = null!;
    protected IRedisStreamService StreamService { get; private set; } = null!;
    protected IDistributedLock DistributedLock { get; private set; } = null!;
    protected IRedisFunction? FunctionService { get; private set; }
    protected IRedisShardedPubSub? ShardedPubSubService { get; private set; }

    protected virtual string ConnectionString => "localhost:6379";
    protected virtual int TestDatabase => 15; // Use database 15 for tests to avoid conflicts
    protected virtual string TestKeyPrefix => $"test:{Guid.NewGuid():N}:";

    public virtual async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => { builder.SetMinimumLevel(LogLevel.Debug); });

        // Add Redis services
        services.AddRedisKit(options =>
        {
            options.ConnectionString = $"{ConnectionString},defaultDatabase={TestDatabase}";
            options.CacheKeyPrefix = TestKeyPrefix;
            options.DefaultTtl = TimeSpan.FromMinutes(5);
            options.OperationTimeout = TimeSpan.FromSeconds(10);
            options.RetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromMilliseconds(100);
            options.Serializer = SerializerType.SystemTextJson;
        });

        ServiceProvider = services.BuildServiceProvider();

        // Get services
        var redisConnection = ServiceProvider.GetRequiredService<IRedisConnection>();
        ConnectionMultiplexer = await redisConnection.GetMultiplexerAsync();
        Database = ConnectionMultiplexer.GetDatabase(TestDatabase);

        CacheService = ServiceProvider.GetRequiredService<IRedisCacheService>();
        PubSubService = ServiceProvider.GetRequiredService<IRedisPubSubService>();
        StreamService = ServiceProvider.GetRequiredService<IRedisStreamService>();
        DistributedLock = ServiceProvider.GetRequiredService<IDistributedLock>();

        // Get optional Redis 7.x services
        try
        {
            FunctionService = ServiceProvider.GetService<IRedisFunction>();
            ShardedPubSubService = ServiceProvider.GetService<IRedisShardedPubSub>();

            // Debug: Check if services are registered
            if (FunctionService == null) Console.WriteLine("WARNING: IRedisFunction service not registered");
            if (ShardedPubSubService == null) Console.WriteLine("WARNING: IRedisShardedPubSub service not registered");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting Redis 7.x services: {ex.Message}");
        }

        // Verify connection
        var ping = await Database.PingAsync();
        if (ping.TotalMilliseconds > 1000) throw new InvalidOperationException($"Redis connection is too slow: {ping.TotalMilliseconds}ms");

        // Get Redis version
        try
        {
            var server = ConnectionMultiplexer.GetServer(ConnectionMultiplexer.GetEndPoints()[0]);
            var info = await server.InfoAsync("server");
            if (info != null)
                foreach (var group in info)
                foreach (var item in group)
                    if (item.Key == "redis_version")
                    {
                        Console.WriteLine($"Redis version: {item.Value}");
                        break;
                    }
        }
        catch
        {
            // Ignore errors getting version info
        }
    }

    public virtual async Task DisposeAsync()
    {
        try
        {
            // Clean up test keys
            if (_keysToClean.Count > 0)
            {
                var keys = _keysToClean.Select(k => (RedisKey)k).ToArray();
                await Database.KeyDeleteAsync(keys);
            }

            // Clean up streams
            foreach (var stream in _streamsToClean) await Database.KeyDeleteAsync(stream);

            // Clean up functions
            if (FunctionService != null && await FunctionService.IsSupportedAsync())
                foreach (var function in _functionsToClean)
                    try
                    {
                        await FunctionService.DeleteAsync(function);
                    }
                    catch
                    {
                        // Ignore errors during cleanup
                    }

            // Flush test database (optional - be careful in production!)
            // await Database.ExecuteAsync("FLUSHDB");
        }
        finally
        {
            // Dispose services
            if (ServiceProvider is IDisposable disposable) disposable.Dispose();
        }
    }

    /// <summary>
    ///     Track a key for cleanup
    /// </summary>
    protected void TrackKey(string key)
    {
        _keysToClean.Add(key);
    }

    /// <summary>
    ///     Track a stream for cleanup
    /// </summary>
    protected void TrackStream(string streamKey)
    {
        _streamsToClean.Add(streamKey);
    }

    /// <summary>
    ///     Track a function library for cleanup
    /// </summary>
    protected void TrackFunction(string libraryName)
    {
        _functionsToClean.Add(libraryName);
    }

    /// <summary>
    ///     Generate a unique test key
    /// </summary>
    protected string GenerateTestKey(string suffix = "")
    {
        var key = $"{TestKeyPrefix}{suffix}";
        TrackKey(key);
        return key;
    }

    /// <summary>
    ///     Check if Redis 7.x features are available
    /// </summary>
    protected async Task<bool> IsRedis7Available()
    {
        if (FunctionService == null) return false;
        return await FunctionService.IsSupportedAsync();
    }

    /// <summary>
    ///     Skip test if Redis 7.x is not available
    /// </summary>
    protected async Task RequireRedis7()
    {
        if (!await IsRedis7Available()) throw new SkipException("Redis 7.0+ required for this test");
    }
}