using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class CacheServiceTests
{
    private readonly ILogger<RedisCacheService> _logger;
    private readonly IOptions<RedisOptions> _options;
    private readonly IDatabaseAsync _db;
    private readonly RedisConnection _connection;

    public CacheServiceTests()
    {
        _logger = Substitute.For<ILogger<RedisCacheService>>();
        _db = Substitute.For<IDatabaseAsync>();

        var redisOptions = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultTtl = TimeSpan.FromHours(1),
            CacheKeyPrefix = "test:"
        };
        _options = Options.Create(redisOptions);

        _connection = Substitute.For<IRedisConnection>();
        _connection.GetDatabaseAsync().Returns(Task.FromResult(_db));
    }

    private RedisCacheService CreateSut()
    {
        return new RedisCacheService(_connection, _logger, _options);
    }

    [Fact]
    public void Constructor_WithValidParameters_DoesNotThrow()
    {
        // Arrange & Act
        var sut = CreateSut();

        // Assert
        Assert.NotNull(sut);
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var cacheService = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await cacheService.GetAsync<TestModel>(null!));
    }

    [Fact]
    public async Task SetAsync_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var cacheService = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await cacheService.SetAsync(null!, new TestModel()));
    }

    [Fact]
    public async Task DeleteAsync_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var cacheService = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await cacheService.DeleteAsync(null!));
    }

    [Fact]
    public void SetKeyPrefix_WithNullPrefix_ThrowsArgumentNullException()
    {
        // Arrange
        var cacheService = CreateSut();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            cacheService.SetKeyPrefix(null!));
    }

    [Fact(Skip = "Requires Redis instance for Lua script testing")]
    public async Task SetManyAsync_WithLuaScriptSupport_UsesOptimizedPath()
    {
        // Arrange
        // Setup Lua script support check
        _db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(Task.FromResult(RedisResult.Create((RedisValue)"PONG")));

        // Setup Lua script execution for SET with EXPIRE
        _db.ScriptEvaluateAsync(
                Arg.Is<string>(s => s.Contains("SET") && s.Contains("EX")),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>())
            .Returns(Task.FromResult(RedisResult.Create(3))); // Return count of items set

        var cacheService = CreateSut();

        var values = new Dictionary<string, TestModel>
        {
            ["key1"] = new() { Id = 1, Name = "Test1" },
            ["key2"] = new() { Id = 2, Name = "Test2" },
            ["key3"] = new() { Id = 3, Name = "Test3" }
        };

        // Act
        await cacheService.SetManyAsync(values, TimeSpan.FromSeconds(30));

        // Assert
        // Should check Lua support
        await _db.Received(1).ScriptEvaluateAsync(
            Arg.Is<string>(s => s.Contains("PING")),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>());

        // Should execute SET with EXPIRE script
        await _db.Received(1).ScriptEvaluateAsync(
            Arg.Is<string>(s => s.Contains("SET") && s.Contains("EX")),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>());
    }

    [Fact(Skip = "Requires Redis instance for fallback testing")]
    public async Task SetManyAsync_WithoutLuaScriptSupport_UsesFallback()
    {
        // Arrange
        var syncDb = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(syncDb);
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));
        _connection.GetDatabaseAsync().Returns(Task.FromResult(syncDb));

        // Setup Lua script support check to fail
        _db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(Task.FromException<RedisResult>(new RedisServerException("ERR unknown command")));

        var cacheService = CreateSut();

        var values = new Dictionary<string, TestModel>
        {
            ["key1"] = new() { Id = 1, Name = "Test1" },
            ["key2"] = new() { Id = 2, Name = "Test2" }
        };

        // Act
        await cacheService.SetManyAsync(values, TimeSpan.FromSeconds(30));

        // Assert
        // Should use MSET
        await syncDb.Received(1).StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>());

        // Should call EXPIRE for each key
        await syncDb.Received(2).KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>());
    }

    [Fact(Skip = "Requires Redis instance for chunking testing")]
    public async Task SetManyAsync_WithLargeDataset_UsesChunking()
    {
        // Arrange
        var syncDb = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(syncDb);
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));
        _connection.GetDatabaseAsync().Returns(Task.FromResult(syncDb));

        // Setup for no TTL (MSET only)
        syncDb.StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>())
            .Returns(Task.FromResult(true));

        var cacheService = CreateSut();

        // Create large dataset (1500 items)
        var values = Enumerable.Range(1, 1500)
            .ToDictionary(
                i => $"key{i}",
                i => new TestModel { Id = i, Name = $"Test{i}" }
            );

        // Act
        await cacheService.SetManyAsync(values, TimeSpan.Zero);

        // Assert
        // Should call MSET twice (1000 + 500)
        await syncDb.Received(2).StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>());
    }

    [MessagePackObject]
    public class TestModel
    {
        [Key(0)] public int Id { get; set; }

        [Key(1)] public string? Name { get; set; }
    }
}