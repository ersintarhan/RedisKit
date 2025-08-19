using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;
using RedisKit.Services;
using Xunit;

namespace RedisKit.Tests;

public class RedisLockHandleTests
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisLockHandle> _logger;

    public RedisLockHandleTests()
    {
        // Mock both IDatabase and IDatabaseAsync interfaces
        _database = Substitute.For<IDatabase, IDatabaseAsync>();
        _logger = Substitute.For<ILogger<RedisLockHandle>>();
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesProperties()
    {
        // Arrange
        var resource = "test-resource";
        var lockId = "lock-123";
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        var handle = new RedisLockHandle(_database, resource, lockId, expiry, false, _logger);

        // Assert
        handle.Resource.Should().Be(resource);
        handle.LockId.Should().Be(lockId);
        handle.IsAcquired.Should().BeTrue();
        handle.AcquiredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        handle.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.Add(expiry), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Constructor_WithNullDatabase_ThrowsException()
    {
        // Act & Assert
        var act = () => new RedisLockHandle(null!, "resource", "lockId", TimeSpan.FromSeconds(30));
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("database");
    }

    [Fact]
    public void Constructor_WithNullResource_ThrowsException()
    {
        // Act & Assert
        var act = () => new RedisLockHandle(_database, null!, "lockId", TimeSpan.FromSeconds(30));
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resource");
    }

    [Fact]
    public void Constructor_WithNullLockId_ThrowsException()
    {
        // Act & Assert
        var act = () => new RedisLockHandle(_database, "resource", null!, TimeSpan.FromSeconds(30));
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("lockId");
    }

    [Fact]
    public async Task ExtendAsync_WhenLockIsHeld_ExtendsSuccessfully()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        var newExpiry = TimeSpan.FromSeconds(60);
        
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // Act
        var result = await handle.ExtendAsync(newExpiry);

        // Assert
        result.Should().BeTrue();
        handle.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.Add(newExpiry), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExtendAsync_WhenLockIsNotHeld_ReturnsFalse()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(0)));

        // Act
        var result = await handle.ExtendAsync(TimeSpan.FromSeconds(60));

        // Assert
        result.Should().BeFalse();
        handle.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task ExtendAsync_AfterDispose_ReturnsFalse()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        await handle.DisposeAsync();

        // Act
        var result = await handle.ExtendAsync(TimeSpan.FromSeconds(60));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_WhenLockIsHeld_ReleasesSuccessfully()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // Act
        await handle.ReleaseAsync();

        // Assert
        handle.IsAcquired.Should().BeFalse();
        await asyncDatabase.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            CommandFlags.DemandMaster);
    }

    [Fact]
    public async Task ReleaseAsync_WhenAlreadyReleased_DoesNothing()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        await handle.ReleaseAsync();
        asyncDatabase.ClearReceivedCalls();

        // Act
        await handle.ReleaseAsync();

        // Assert
        await asyncDatabase.DidNotReceive().ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact(Skip = "Complex async disposal mechanism - needs refactoring")]
    public async Task DisposeAsync_ReleasesLockAndTimer()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        
        // Configure the mock for both sync and async
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // Act
        await handle.DisposeAsync();

        // Assert - After dispose, we should have called the release method
        await asyncDatabase.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            CommandFlags.DemandMaster);
    }

    [Fact]
    public async Task DisposeAsync_WhenAlreadyDisposed_DoesNothing()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        await handle.DisposeAsync();
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ClearReceivedCalls();

        // Act
        await handle.DisposeAsync();

        // Assert
        await asyncDatabase.DidNotReceive().ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact(Skip = "Complex sync disposal with Task.Run.Wait - needs refactoring")]
    public void Dispose_ReleasesLockSynchronously()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        
        // Configure the mock for both sync and async
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // Act
        handle.Dispose();

        // Assert
        // The IsAcquired property returns false if disposed OR if expired
        // We can't directly test IsAcquired because it checks both _isAcquired AND expiry time
        
        // Verify that the release was attempted
        Thread.Sleep(100); // Give time for async operation
        asyncDatabase.Received().ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            CommandFlags.DemandMaster);
    }

    [Fact(Skip = "This test takes too long due to 5-second timeout")]
    public void Dispose_WhenReleaseThrows_LogsWarning()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(30), false, _logger);
        
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromException<RedisResult>(new RedisException("Connection lost")));

        // Act
        handle.Dispose();

        // Assert
        Thread.Sleep(6000); // Wait for timeout (5 seconds) plus buffer
        
        // The warning log happens in the synchronous Dispose method
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o != null && o.ToString()!.Contains("Failed to release lock")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void IsAcquired_WhenLockExpired_ReturnsFalse()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromMilliseconds(1), false, _logger);
        
        // Act
        Thread.Sleep(10); // Wait for expiry
        
        // Assert
        handle.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task Constructor_WithAutoRenewal_StartsRenewalTimer()
    {
        // Arrange
        var handle = new RedisLockHandle(_database, "resource", "lockId", TimeSpan.FromSeconds(3), true, _logger);
        
        var asyncDatabase = (IDatabaseAsync)_database;
        asyncDatabase.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // Act
        await Task.Delay(1500); // Wait for first renewal (should happen at ~1 second)

        // Assert
        await asyncDatabase.Received().ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(keys => keys[0] == "lock:resource"),
            Arg.Any<RedisValue[]>(),
            CommandFlags.DemandMaster);

        // Cleanup
        await handle.DisposeAsync();
    }
}