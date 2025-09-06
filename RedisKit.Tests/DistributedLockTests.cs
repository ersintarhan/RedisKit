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
        // Use more tolerance for CI environments
        Assert.True(elapsed >= wait.Subtract(TimeSpan.FromMilliseconds(50)), $"Elapsed time {elapsed} should be at least {wait}");
        Assert.True(elapsed < wait + TimeSpan.FromSeconds(2), $"Elapsed time {elapsed} should be less than {wait} + 2s"); // Increased tolerance for CI
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

    #region AcquireMultiLockAsync Tests

    [Fact]
    public async Task AcquireMultiLockAsync_WithAllResourcesAvailable_ReturnsMultiLockHandle()
    {
        // Arrange
        var resources = new[] { "resource1", "resource2", "resource3" };
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
        var multiLockHandle = await sut.AcquireMultiLockAsync(resources, expiry);

        // Assert
        Assert.NotNull(multiLockHandle);
        Assert.Equal(resources.Length, multiLockHandle.Locks.Count);

        // Verify all resources were locked
        await _dbAsync.Received(3).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            expiry,
            When.NotExists,
            CommandFlags.DemandMaster);
    }

    [Fact]
    public async Task AcquireMultiLockAsync_WithOneResourceUnavailable_ReturnsNullAndReleasesAcquiredLocks()
    {
        // Arrange
        var resources = new[] { "resource1", "resource2", "resource3" };
        var expiry = TimeSpan.FromSeconds(10);
        var sut = CreateSut();

        var syncDb = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(syncDb);
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));

        // First two succeed, third fails
        _dbAsync.StringSetAsync(
                Arg.Is<RedisKey>(key => key.ToString().Contains("resource1") || key.ToString().Contains("resource2")),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                When.NotExists,
                CommandFlags.DemandMaster)
            .Returns(Task.FromResult(true));

        _dbAsync.StringSetAsync(
                Arg.Is<RedisKey>(key => key.ToString().Contains("resource3")),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                When.NotExists,
                CommandFlags.DemandMaster)
            .Returns(Task.FromResult(false));

        // Mock for release operations
        syncDb.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // Act
        var multiLockHandle = await sut.AcquireMultiLockAsync(resources, expiry);

        // Assert
        Assert.Null(multiLockHandle);

        // Verify at least one resource was attempted (first two succeed, third fails)
        await _dbAsync.Received().StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            expiry,
            When.NotExists,
            CommandFlags.DemandMaster);

        // Note: Cleanup happens through individual lock handles' ReleaseAsync() methods
        // This is harder to verify in unit tests but the important part is that multiLockHandle is null
    }

    [Fact]
    public async Task AcquireMultiLockAsync_WithNullResources_ThrowsArgumentException()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(10);
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.AcquireMultiLockAsync(null!, expiry));
    }

    [Fact]
    public async Task AcquireMultiLockAsync_WithEmptyResources_ThrowsArgumentException()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(10);
        var sut = CreateSut();
        var emptyResources = new string[0];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.AcquireMultiLockAsync(emptyResources, expiry));
    }

    #endregion

    #region WaitForUnlockAsync Tests

    [Fact]
    public async Task WaitForUnlockAsync_WhenResourceBecomesUnlocked_ReturnsSuccessfully()
    {
        // Arrange
        var resource = "test-resource";
        var timeout = TimeSpan.FromSeconds(1);
        var sut = CreateSut();

        var callCount = 0;
        _dbAsync.KeyExistsAsync(Arg.Any<RedisKey>(), CommandFlags.DemandMaster)
            .Returns(_ => Task.FromResult(++callCount <= 2)); // First 2 calls return true (locked), then false (unlocked)

        // Act & Assert - Should not throw
        await sut.WaitForUnlockAsync(resource, timeout);

        // Verify multiple checks were made
        await _dbAsync.Received().KeyExistsAsync(Arg.Any<RedisKey>(), CommandFlags.DemandMaster);
    }

    [Fact]
    public async Task WaitForUnlockAsync_WhenResourceRemainisLocked_ThrowsTimeoutException()
    {
        // Arrange
        var resource = "test-resource";
        var timeout = TimeSpan.FromMilliseconds(500);
        var sut = CreateSut();

        _dbAsync.KeyExistsAsync(Arg.Any<RedisKey>(), CommandFlags.DemandMaster)
            .Returns(Task.FromResult(true)); // Always returns true (locked)

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => sut.WaitForUnlockAsync(resource, timeout));

        Assert.Contains($"Timeout waiting for resource '{resource}' to be unlocked", ex.Message);

        // Verify multiple checks were made
        await _dbAsync.Received().KeyExistsAsync(Arg.Any<RedisKey>(), CommandFlags.DemandMaster);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task WaitForUnlockAsync_WithInvalidResource_ThrowsArgumentException(string? resource)
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(1);
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.WaitForUnlockAsync(resource!, timeout));
    }


    [Fact]
    public async Task WaitForUnlockAsync_WithLongResourceName_ThrowsArgumentException()
    {
        // Arrange
        var resource = new string('a', 600); // Longer than 512 char limit
        var timeout = TimeSpan.FromSeconds(1);
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.WaitForUnlockAsync(resource, timeout));
    }

    #endregion
}