using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class DistributedLockTests
{
    private readonly IRedisConnection _connection;
    private readonly IDatabaseAsync _dbAsync;
    private readonly ILogger<RedisDistributedLock> _logger;

    public DistributedLockTests()
    {
        _logger = Substitute.For<ILogger<RedisDistributedLock>>();
        _dbAsync = Substitute.For<IDatabaseAsync>();

        _connection = Substitute.For<IRedisConnection>();
        _connection.GetDatabaseAsync().Returns(_dbAsync);
    }

    private RedisDistributedLock CreateSut()
    {
        var options = Options.Create(new DistributedLockOptions
        {
            EnableAutoRenewal = false,
            DefaultExpiry = TimeSpan.FromSeconds(30)
        });

        return new RedisDistributedLock(_connection, options, _logger);
    }

    [Fact]
    public async Task AcquireLockAsync_WhenResourceIsAvailable_ReturnsLockHandle()
    {
        // Arrange
        var resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(10);
        var sut = CreateSut();

        var syncDb = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(syncDb);
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));

        _dbAsync.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                When.NotExists,
                CommandFlags.DemandMaster)
            .Returns(Task.FromResult(true));

        // Act
        var lockHandle = await sut.AcquireLockAsync(resource, expiry);

        // Assert
        Assert.NotNull(lockHandle);
        Assert.Equal(resource, lockHandle.Resource);
        Assert.True(lockHandle.IsAcquired);
        Assert.NotEmpty(lockHandle.LockId);
    }

    [Fact]
    public async Task AcquireLockAsync_WhenResourceIsLocked_ReturnsNull()
    {
        // Arrange
        var resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(10);
        var sut = CreateSut();

        _dbAsync.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                When.NotExists,
                CommandFlags.DemandMaster)
            .Returns(Task.FromResult(false));

        // Act
        var lockHandle = await sut.AcquireLockAsync(resource, expiry);

        // Assert
        Assert.Null(lockHandle);
    }

    [Fact]
    public async Task AcquireLockAsync_WithRetry_TimesOutAfterWaitPeriod()
    {
        // Arrange
        var resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(10);
        var wait = TimeSpan.FromMilliseconds(500);
        var retry = TimeSpan.FromMilliseconds(100);
        var sut = CreateSut();

        _dbAsync.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                When.NotExists,
                CommandFlags.DemandMaster)
            .Returns(Task.FromResult(false));

        // Act
        var startTime = DateTime.UtcNow;
        var lockHandle = await sut.AcquireLockAsync(
            resource, expiry, wait, retry);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.Null(lockHandle);
        Assert.True(elapsed >= wait);
        Assert.True(elapsed < wait + TimeSpan.FromSeconds(1)); // Some tolerance
    }

    [Fact]
    public async Task IsLockedAsync_WhenLocked_ReturnsTrue()
    {
        // Arrange
        var resource = "test-resource";
        var sut = CreateSut();

        _dbAsync.KeyExistsAsync(
                Arg.Any<RedisKey>(),
                CommandFlags.DemandMaster)
            .Returns(Task.FromResult(true));

        // Act
        var isLocked = await sut.IsLockedAsync(resource);

        // Assert
        Assert.True(isLocked);
    }

    [Fact]
    public async Task IsLockedAsync_WhenNotLocked_ReturnsFalse()
    {
        // Arrange
        var resource = "test-resource";
        var sut = CreateSut();

        _dbAsync.KeyExistsAsync(
                Arg.Any<RedisKey>(),
                CommandFlags.DemandMaster)
            .Returns(Task.FromResult(false));

        // Act
        var isLocked = await sut.IsLockedAsync(resource);

        // Assert
        Assert.False(isLocked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task AcquireLockAsync_WithInvalidResource_ThrowsArgumentException(string? resource)
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(10);
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.AcquireLockAsync(resource!, expiry));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task AcquireLockAsync_WithInvalidExpiry_ThrowsArgumentException(int seconds)
    {
        // Arrange
        var resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(seconds);
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.AcquireLockAsync(resource, expiry));
    }
}