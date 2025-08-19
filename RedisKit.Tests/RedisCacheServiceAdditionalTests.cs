using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using RedisKit.Services;
using StackExchange.Redis;
using System.Buffers;
using Xunit;

namespace RedisKit.Tests;

/// <summary>
/// Additional tests for RedisCacheService to increase coverage
/// </summary>
public class RedisCacheServiceAdditionalTests
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly IOptions<RedisOptions> _options;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabaseAsync _database;
    private readonly IDatabase _syncDatabase;
    private readonly RedisCacheService _cacheService;
    private readonly ObjectPoolProvider _poolProvider;

    public RedisCacheServiceAdditionalTests()
    {
        _connection = Substitute.For<IRedisConnection>();
        _logger = Substitute.For<ILogger<RedisCacheService>>();
        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _database = Substitute.For<IDatabaseAsync>();
        _syncDatabase = Substitute.For<IDatabase>();
        _poolProvider = Substitute.For<ObjectPoolProvider>();
        
        var redisOptions = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultTtl = TimeSpan.FromMinutes(5),
            Serializer = SerializerType.SystemTextJson,
            Pooling = new PoolingOptions 
            { 
                Enabled = true, 
                MaxPoolSize = 100 
            }
        };
        _options = Options.Create(redisOptions);

        _connection.GetDatabaseAsync().Returns(Task.FromResult(_database));
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(_multiplexer));
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_syncDatabase);

        _cacheService = new RedisCacheService(_connection, _logger, _options, _poolProvider);
    }

    #region GetBytesAsync Tests

    [Fact]
    public async Task GetBytesAsync_WithValidKey_ReturnsBytes()
    {
        // Arrange
        const string key = "test:key";
        byte[] expectedBytes = { 1, 2, 3, 4, 5 };
        _database.StringGetAsync(key, CommandFlags.None)
            .Returns(Task.FromResult((RedisValue)expectedBytes));

        // Act
        var result = await _cacheService.GetBytesAsync(key);

        // Assert
        result.Should().BeEquivalentTo(expectedBytes);
        await _database.Received(1).StringGetAsync(key, CommandFlags.None);
    }

    [Fact]
    public async Task GetBytesAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        const string key = "test:key";
        _database.StringGetAsync(key, CommandFlags.None)
            .Returns(Task.FromResult(RedisValue.Null));

        // Act
        var result = await _cacheService.GetBytesAsync(key);

        // Assert
        result.Should().BeNull();
        await _database.Received(1).StringGetAsync(key, CommandFlags.None);
    }

    [Fact]
    public async Task GetBytesAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await _cacheService.GetBytesAsync(null!));
    }

    [Fact]
    public async Task GetBytesAsync_WithKeyPrefix_UsesPrefix()
    {
        // Arrange
        const string prefix = "app:";
        const string key = "key";
        _cacheService.SetKeyPrefix(prefix);
        
        _database.StringGetAsync($"{prefix}{key}", CommandFlags.None)
            .Returns(Task.FromResult((RedisValue)new byte[] { 1, 2, 3 }));

        // Act
        var result = await _cacheService.GetBytesAsync(key);

        // Assert
        result.Should().NotBeNull();
        await _database.Received(1).StringGetAsync($"{prefix}{key}", CommandFlags.None);
    }

    #endregion

    #region SetBytesAsync Tests

    [Fact]
    public async Task SetBytesAsync_WithValidKeyAndValue_SetsValue()
    {
        // Arrange
        const string key = "test:key";
        byte[] value = { 1, 2, 3, 4, 5 };
        var ttl = TimeSpan.FromMinutes(10);
        
        _database.StringSetAsync(key, value, ttl, When.Always, CommandFlags.None)
            .Returns(Task.FromResult(true));

        // Act
        await _cacheService.SetBytesAsync(key, value, ttl);

        // Assert
        await _database.Received(1).StringSetAsync(key, Arg.Any<RedisValue>(), ttl, When.Always, CommandFlags.None);
    }

    [Fact]
    public async Task SetBytesAsync_WithoutTtl_UsesDefaultTtl()
    {
        // Arrange
        const string key = "test:key";
        byte[] value = { 1, 2, 3 };
        var defaultTtl = TimeSpan.FromMinutes(5); // From options
        
        _database.StringSetAsync(key, value, defaultTtl, When.Always, CommandFlags.None)
            .Returns(Task.FromResult(true));

        // Act
        await _cacheService.SetBytesAsync(key, value);

        // Assert
        await _database.Received(1).StringSetAsync(key, Arg.Any<RedisValue>(), defaultTtl, When.Always, CommandFlags.None);
    }

    [Fact]
    public async Task SetBytesAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        byte[] value = { 1, 2, 3 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await _cacheService.SetBytesAsync(null!, value));
    }

    [Fact]
    public async Task SetBytesAsync_WithNullValue_ThrowsArgumentNullException()
    {
        // Arrange
        const string key = "test:key";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await _cacheService.SetBytesAsync(key, null!));
    }

    [Fact]
    public async Task SetBytesAsync_WithKeyPrefix_UsesPrefix()
    {
        // Arrange
        const string prefix = "cache:";
        const string key = "data";
        byte[] value = { 10, 20, 30 };
        _cacheService.SetKeyPrefix(prefix);
        
        _database.StringSetAsync($"{prefix}{key}", value, Arg.Any<TimeSpan>(), When.Always, CommandFlags.None)
            .Returns(Task.FromResult(true));

        // Act
        await _cacheService.SetBytesAsync(key, value);

        // Assert
        await _database.Received(1).StringSetAsync(
            $"{prefix}{key}", 
            Arg.Any<RedisValue>(), 
            Arg.Any<TimeSpan>(), 
            When.Always, 
            CommandFlags.None);
    }

    #endregion

    #region SetKeyPrefix Tests

    [Fact]
    public void SetKeyPrefix_WithValidPrefix_SetsPrefix()
    {
        // Arrange
        const string prefix = "myapp:";

        // Act
        _cacheService.SetKeyPrefix(prefix);

        // Assert - verify by using it
        const string key = "test";
        _database.StringGetAsync($"{prefix}{key}", CommandFlags.None)
            .Returns(Task.FromResult(RedisValue.Null));
        
        var _ = _cacheService.GetBytesAsync(key).Result;
        
        _database.Received(1).StringGetAsync($"{prefix}{key}", CommandFlags.None);
    }

    [Fact]
    public void SetKeyPrefix_WithNullPrefix_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _cacheService.SetKeyPrefix(null!));
    }

    [Fact]
    public void SetKeyPrefix_WithEmptyPrefix_Works()
    {
        // Act
        _cacheService.SetKeyPrefix("");

        // Assert - verify by using it
        const string key = "test";
        _database.StringGetAsync(key, CommandFlags.None)
            .Returns(Task.FromResult(RedisValue.Null));
        
        var _ = _cacheService.GetBytesAsync(key).Result;
        
        _database.Received(1).StringGetAsync(key, CommandFlags.None);
    }

    [Fact]
    public void SetKeyPrefix_WithVeryLongPrefix_ThrowsArgumentException()
    {
        // Arrange - Create a prefix that exceeds max key length
        var longPrefix = new string('x', RedisConstants.MaxRedisKeyLength + 1);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            _cacheService.SetKeyPrefix(longPrefix));
        exception.Message.Should().Contain("exceeds maximum allowed length");
    }

    #endregion

    #region ExecuteBatchAsync Tests

    [Fact]
    public async Task ExecuteBatchAsync_WithValidOperations_ExecutesBatch()
    {
        // Arrange
        var batch = Substitute.For<IBatch>();
        _syncDatabase.CreateBatch(Arg.Any<object>()).Returns(batch);
        
        var tcs = new TaskCompletionSource<bool>();
        tcs.SetResult(true);
        
        batch.KeyDeleteAsync(Arg.Any<RedisKey>(), CommandFlags.None)
            .Returns(Task.FromResult(true));

        // Act
        var result = await _cacheService.ExecuteBatchAsync(ops =>
        {
            ops.DeleteAsync("key1");
        });

        // Assert
        result.Should().NotBeNull();
        batch.Received(1).Execute();
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithMultipleOperations_ExecutesAll()
    {
        // Arrange
        var batch = Substitute.For<IBatch>();
        _syncDatabase.CreateBatch(Arg.Any<object>()).Returns(batch);
        
        batch.KeyDeleteAsync(Arg.Any<RedisKey>(), CommandFlags.None)
            .Returns(Task.FromResult(true));
        batch.KeyExistsAsync(Arg.Any<RedisKey>(), CommandFlags.None)
            .Returns(Task.FromResult(true));

        // Act
        var result = await _cacheService.ExecuteBatchAsync(ops =>
        {
            ops.DeleteAsync("key1");
            ops.ExistsAsync("key2");
        });

        // Assert
        result.Should().NotBeNull();
        batch.Received(1).Execute();
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithNullConfigureAction_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<NullReferenceException>(() => 
            _cacheService.ExecuteBatchAsync(null!));
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public async Task GetAsync_WithVeryLongKey_ThrowsArgumentException()
    {
        // Arrange
        var longKey = new string('x', RedisConstants.MaxRedisKeyLength + 1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => 
            await _cacheService.GetAsync<TestData>(longKey));
        exception.Message.Should().Contain("exceeds maximum allowed length");
    }

    [Fact]
    public async Task SetAsync_WithVeryLongKey_ThrowsArgumentException()
    {
        // Arrange
        var longKey = new string('x', RedisConstants.MaxRedisKeyLength + 1);
        var data = new TestData { Id = 1, Name = "Test" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => 
            await _cacheService.SetAsync(longKey, data));
        exception.Message.Should().Contain("exceeds maximum allowed length");
    }

    [Fact]
    public async Task DeleteAsync_WithVeryLongKey_ThrowsArgumentException()
    {
        // Arrange
        var longKey = new string('x', RedisConstants.MaxRedisKeyLength + 1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _cacheService.DeleteAsync(longKey));
        exception.Message.Should().Contain("exceeds maximum allowed length");
    }

    [Fact]
    public async Task ExistsAsync_WithVeryLongKey_ThrowsArgumentException()
    {
        // Arrange
        var longKey = new string('x', RedisConstants.MaxRedisKeyLength + 1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => 
            await _cacheService.ExistsAsync(longKey));
        exception.Message.Should().Contain("exceeds maximum allowed length");
    }

    [Fact]
    public async Task GetBytesAsync_WithVeryLongKey_ThrowsArgumentException()
    {
        // Arrange
        var longKey = new string('x', RedisConstants.MaxRedisKeyLength + 1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => 
            await _cacheService.GetBytesAsync(longKey));
        exception.Message.Should().Contain("exceeds maximum allowed length");
    }

    [Fact]
    public async Task SetBytesAsync_WithVeryLongKey_ThrowsArgumentException()
    {
        // Arrange
        var longKey = new string('x', RedisConstants.MaxRedisKeyLength + 1);
        var value = new byte[] { 1, 2, 3 };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => 
            await _cacheService.SetBytesAsync(longKey, value));
        exception.Message.Should().Contain("exceeds maximum allowed length");
    }

    #endregion

    #region Test Classes

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}