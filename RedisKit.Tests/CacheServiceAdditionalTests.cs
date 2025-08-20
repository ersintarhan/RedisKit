using FluentAssertions;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class CacheServiceAdditionalTests
{
    private readonly IRedisConnection _connection;
    private readonly IDatabaseAsync _db;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly IOptions<RedisOptions> _options;

    public CacheServiceAdditionalTests()
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
        _connection.GetDatabaseAsync().Returns(_db);
    }

    private RedisCacheService CreateSut()
    {
        return new RedisCacheService(_connection, _logger, _options);
    }

    [Fact]
    public async Task GetManyAsync_WithEmptyKeys_ReturnsEmptyDictionary()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetManyAsync<TestModel>(Array.Empty<string>());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void SetKeyPrefix_WithValidPrefix_UpdatesPrefix()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => sut.SetKeyPrefix("newprefix:"));
        exception.Should().BeNull();
    }

    [Fact]
    public void SetKeyPrefix_WithEmptyString_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        var exception = Record.Exception(() => sut.SetKeyPrefix(string.Empty));
        exception.Should().BeNull();
    }

    [Fact]
    public async Task SetManyAsync_WithNullValues_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.SetManyAsync<TestModel>(null!));
    }


    [Fact]
    public async Task GetManyAsync_WithNullKeys_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.GetManyAsync<TestModel>(null!));
    }

    [Fact]
    public async Task SetBytesAsync_WithNullBytes_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.SetBytesAsync("test-key", null!));
    }

    [Fact]
    public async Task SetBytesAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();
        var testBytes = new byte[] { 1, 2, 3 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.SetBytesAsync(null!, testBytes));
    }

    [Fact]
    public async Task GetBytesAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.GetBytesAsync(null!));
    }

    [Fact]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.ExistsAsync(null!));
    }

    [Fact]
    public void Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisCacheService(null!, _logger, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisCacheService(_connection, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisCacheService(_connection, _logger, null!));
    }

    [MessagePackObject]
    public class TestModel
    {
        [Key(0)] public int Id { get; set; }
        [Key(1)] public string Name { get; set; } = string.Empty;
    }
}