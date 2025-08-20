using FluentAssertions;
using RedisKit.Tests.InMemory;
using Xunit;

namespace RedisKit.Tests.InMemoryBasedTests;

public class DistributedLockBusinessLogicTests
{
    private readonly InMemoryDistributedLock _lockService;

    public DistributedLockBusinessLogicTests()
    {
        _lockService = new InMemoryDistributedLock();
    }

    [Fact]
    public async Task AcquireLockAsync_Should_Acquire_Lock_When_Available()
    {
        // Arrange
        var resource = "test-resource";

        // Act
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Assert
        handle.Should().NotBeNull();
        handle!.Resource.Should().Be(resource);
        handle.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireLockAsync_Should_Return_Null_When_Already_Locked()
    {
        // Arrange
        var resource = "locked-resource";
        var handle1 = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Act
        var handle2 = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().BeNull();
    }

    [Fact]
    public async Task AcquireLockAsync_Should_Acquire_After_Expiry()
    {
        // Arrange
        var resource = "expiring-lock";
        var handle1 = await _lockService.AcquireLockAsync(resource, TimeSpan.FromMilliseconds(100));

        // Act
        await Task.Delay(150);
        var handle2 = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireLockAsync_With_Wait_Should_Retry()
    {
        // Arrange
        var resource = "wait-resource";
        var handle1 = await _lockService.AcquireLockAsync(resource, TimeSpan.FromMilliseconds(200)); // Increased from 100ms

        // Act
        var acquireTask = _lockService.AcquireLockAsync(
            resource,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(2), // Increased wait time for CI
            TimeSpan.FromMilliseconds(100) // Increased retry interval
        );

        await Task.Delay(300); // Increased delay to ensure lock expires (was 150ms)
        var handle2 = await acquireTask;

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull();
    }

    [Fact]
    public async Task IsLockedAsync_Should_Return_True_When_Locked()
    {
        // Arrange
        var resource = "check-locked";
        await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Act
        var isLocked = await _lockService.IsLockedAsync(resource);

        // Assert
        isLocked.Should().BeTrue();
    }

    [Fact]
    public async Task IsLockedAsync_Should_Return_False_When_Not_Locked()
    {
        // Act
        var isLocked = await _lockService.IsLockedAsync("not-locked");

        // Assert
        isLocked.Should().BeFalse();
    }

    [Fact]
    public async Task IsLockedAsync_Should_Return_False_After_Expiry()
    {
        // Arrange
        var resource = "expiry-check";
        await _lockService.AcquireLockAsync(resource, TimeSpan.FromMilliseconds(100));

        // Act
        await Task.Delay(150);
        var isLocked = await _lockService.IsLockedAsync(resource);

        // Assert
        isLocked.Should().BeFalse();
    }

    [Fact]
    public async Task ExtendLockAsync_Should_Extend_Valid_Lock()
    {
        // Arrange
        var resource = "extend-lock";
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromMilliseconds(100));

        // Act
        var extended = await _lockService.ExtendLockAsync(handle!, TimeSpan.FromSeconds(10));
        await Task.Delay(150);
        var isLocked = await _lockService.IsLockedAsync(resource);

        // Assert
        extended.Should().BeTrue();
        isLocked.Should().BeTrue();
    }

    [Fact]
    public async Task ExtendLockAsync_Should_Return_False_For_Invalid_Handle()
    {
        // Arrange
        var resource = "extend-invalid";
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));
        await handle!.ReleaseAsync();

        // Act
        var extended = await _lockService.ExtendLockAsync(handle, TimeSpan.FromSeconds(10));

        // Assert
        extended.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_Should_Release_Lock()
    {
        // Arrange
        var resource = "release-lock";
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Act
        await handle!.ReleaseAsync();
        var isLocked = await _lockService.IsLockedAsync(resource);

        // Assert
        isLocked.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_Should_Release_Lock()
    {
        // Arrange
        var resource = "dispose-lock";
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Act
        handle!.Dispose();
        var isLocked = await _lockService.IsLockedAsync(resource);

        // Assert
        isLocked.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_Should_Release_Lock()
    {
        // Arrange
        var resource = "dispose-async-lock";
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        // Act
        await handle!.DisposeAsync();
        var isLocked = await _lockService.IsLockedAsync(resource);

        // Assert
        isLocked.Should().BeFalse();
    }

    [Fact]
    public async Task LockHandle_Properties_Should_Be_Set()
    {
        // Arrange
        var resource = "props-lock";
        var beforeAcquire = DateTime.UtcNow;

        // Act
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));
        var afterAcquire = DateTime.UtcNow;

        // Assert
        handle!.Resource.Should().Be(resource);
        handle.LockId.Should().NotBeNullOrEmpty();
        handle.IsAcquired.Should().BeTrue();
        handle.AcquiredAt.Should().BeOnOrAfter(beforeAcquire).And.BeOnOrBefore(afterAcquire);
        handle.ExpiresAt.Should().BeAfter(handle.AcquiredAt);
    }

    [Fact]
    public async Task ExtendAsync_On_Handle_Should_Extend_Lock()
    {
        // Arrange
        var resource = "handle-extend";
        var handle = await _lockService.AcquireLockAsync(resource, TimeSpan.FromMilliseconds(100));
        var originalExpiry = handle!.ExpiresAt;

        // Act
        var extended = await handle.ExtendAsync(TimeSpan.FromSeconds(10));
        var newExpiry = handle.ExpiresAt;

        // Assert
        extended.Should().BeTrue();
        newExpiry.Should().BeAfter(originalExpiry);
    }

    [Fact]
    public async Task Clear_Should_Release_All_Locks()
    {
        // Arrange
        await _lockService.AcquireLockAsync("lock1", TimeSpan.FromSeconds(10));
        await _lockService.AcquireLockAsync("lock2", TimeSpan.FromSeconds(10));
        await _lockService.AcquireLockAsync("lock3", TimeSpan.FromSeconds(10));

        // Act
        _lockService.Clear();

        // Assert
        (await _lockService.IsLockedAsync("lock1")).Should().BeFalse();
        (await _lockService.IsLockedAsync("lock2")).Should().BeFalse();
        (await _lockService.IsLockedAsync("lock3")).Should().BeFalse();
    }

    [Fact]
    public async Task Multiple_Locks_On_Different_Resources_Should_Work()
    {
        // Act
        var handle1 = await _lockService.AcquireLockAsync("resource1", TimeSpan.FromSeconds(10));
        var handle2 = await _lockService.AcquireLockAsync("resource2", TimeSpan.FromSeconds(10));
        var handle3 = await _lockService.AcquireLockAsync("resource3", TimeSpan.FromSeconds(10));

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull();
        handle3.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancellation_Should_Stop_Wait()
    {
        // Arrange
        var resource = "cancel-wait";
        await _lockService.AcquireLockAsync(resource, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        // Act
        var handle = await _lockService.AcquireLockAsync(
            resource,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(50),
            cts.Token
        );

        // Assert
        handle.Should().BeNull();
    }
}