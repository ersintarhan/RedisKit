using FluentAssertions;
using NSubstitute;
using RedisKit.Interfaces;
using Xunit;

namespace RedisKit.Tests;

/// <summary>
///     Tests for IDistributedLock interface methods
/// </summary>
public class IDistributedLockTests
{
    private readonly IDistributedLock _distributedLock;
    private readonly ILockHandle _lockHandle;

    public IDistributedLockTests()
    {
        _distributedLock = Substitute.For<IDistributedLock>();
        _lockHandle = Substitute.For<ILockHandle>();
    }

    #region AcquireLockAsync Tests

    [Fact]
    public async Task AcquireLockAsync_WithValidParameters_ReturnsLockHandle()
    {
        // Arrange
        const string resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(30);
        _distributedLock.AcquireLockAsync(resource, expiry)
            .Returns(Task.FromResult<ILockHandle?>(_lockHandle));

        // Act
        var result = await _distributedLock.AcquireLockAsync(resource, expiry);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(_lockHandle);
        await _distributedLock.Received(1).AcquireLockAsync(resource, expiry);
    }

    [Fact]
    public async Task AcquireLockAsync_WhenLockNotAvailable_ReturnsNull()
    {
        // Arrange
        const string resource = "locked-resource";
        var expiry = TimeSpan.FromSeconds(30);
        _distributedLock.AcquireLockAsync(resource, expiry)
            .Returns(Task.FromResult<ILockHandle?>(null));

        // Act
        var result = await _distributedLock.AcquireLockAsync(resource, expiry);

        // Assert
        result.Should().BeNull();
        await _distributedLock.Received(1).AcquireLockAsync(resource, expiry);
    }

    [Fact]
    public async Task AcquireLockAsync_WithCancellation_PropagatesCancellation()
    {
        // Arrange
        const string resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(30);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _distributedLock.AcquireLockAsync(resource, expiry, cts.Token)
            .Returns(Task.FromCanceled<ILockHandle?>(cts.Token));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _distributedLock.AcquireLockAsync(resource, expiry, cts.Token));

        await _distributedLock.Received(1).AcquireLockAsync(resource, expiry, cts.Token);
    }

    [Fact]
    public async Task AcquireLockAsync_WithRetry_ReturnsLockHandle()
    {
        // Arrange
        const string resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(30);
        var wait = TimeSpan.FromSeconds(5);
        var retry = TimeSpan.FromMilliseconds(100);

        _distributedLock.AcquireLockAsync(resource, expiry, wait, retry)
            .Returns(Task.FromResult<ILockHandle?>(_lockHandle));

        // Act
        var result = await _distributedLock.AcquireLockAsync(resource, expiry, wait, retry);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(_lockHandle);
        await _distributedLock.Received(1).AcquireLockAsync(resource, expiry, wait, retry);
    }

    [Fact]
    public async Task AcquireLockAsync_WithRetryTimeout_ReturnsNull()
    {
        // Arrange
        const string resource = "locked-resource";
        var expiry = TimeSpan.FromSeconds(30);
        var wait = TimeSpan.FromSeconds(1);
        var retry = TimeSpan.FromMilliseconds(100);

        _distributedLock.AcquireLockAsync(resource, expiry, wait, retry)
            .Returns(Task.FromResult<ILockHandle?>(null));

        // Act
        var result = await _distributedLock.AcquireLockAsync(resource, expiry, wait, retry);

        // Assert
        result.Should().BeNull();
        await _distributedLock.Received(1).AcquireLockAsync(resource, expiry, wait, retry);
    }

    [Fact]
    public async Task AcquireLockAsync_WithException_PropagatesException()
    {
        // Arrange
        const string resource = "test-resource";
        var expiry = TimeSpan.FromSeconds(30);
        var expectedException = new InvalidOperationException("Redis connection failed");

        _distributedLock.AcquireLockAsync(resource, expiry)
            .Returns(Task.FromException<ILockHandle?>(expectedException));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _distributedLock.AcquireLockAsync(resource, expiry));

        exception.Message.Should().Be("Redis connection failed");
        await _distributedLock.Received(1).AcquireLockAsync(resource, expiry);
    }

    #endregion

    #region IsLockedAsync Tests

    [Fact]
    public async Task IsLockedAsync_WhenLocked_ReturnsTrue()
    {
        // Arrange
        const string resource = "locked-resource";
        _distributedLock.IsLockedAsync(resource)
            .Returns(Task.FromResult(true));

        // Act
        var result = await _distributedLock.IsLockedAsync(resource);

        // Assert
        result.Should().BeTrue();
        await _distributedLock.Received(1).IsLockedAsync(resource);
    }

    [Fact]
    public async Task IsLockedAsync_WhenNotLocked_ReturnsFalse()
    {
        // Arrange
        const string resource = "unlocked-resource";
        _distributedLock.IsLockedAsync(resource)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _distributedLock.IsLockedAsync(resource);

        // Assert
        result.Should().BeFalse();
        await _distributedLock.Received(1).IsLockedAsync(resource);
    }

    [Fact]
    public async Task IsLockedAsync_WithCancellation_PropagatesCancellation()
    {
        // Arrange
        const string resource = "test-resource";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _distributedLock.IsLockedAsync(resource, cts.Token)
            .Returns(Task.FromCanceled<bool>(cts.Token));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _distributedLock.IsLockedAsync(resource, cts.Token));

        await _distributedLock.Received(1).IsLockedAsync(resource, cts.Token);
    }

    [Fact]
    public async Task IsLockedAsync_WithException_PropagatesException()
    {
        // Arrange
        const string resource = "test-resource";
        var expectedException = new InvalidOperationException("Connection lost");

        _distributedLock.IsLockedAsync(resource)
            .Returns(Task.FromException<bool>(expectedException));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _distributedLock.IsLockedAsync(resource));

        exception.Message.Should().Be("Connection lost");
        await _distributedLock.Received(1).IsLockedAsync(resource);
    }

    #endregion

    #region ExtendLockAsync Tests

    [Fact]
    public async Task ExtendLockAsync_WithValidLock_ReturnsTrue()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(60);
        _distributedLock.ExtendLockAsync(_lockHandle, expiry)
            .Returns(Task.FromResult(true));

        // Act
        var result = await _distributedLock.ExtendLockAsync(_lockHandle, expiry);

        // Assert
        result.Should().BeTrue();
        await _distributedLock.Received(1).ExtendLockAsync(_lockHandle, expiry);
    }

    [Fact]
    public async Task ExtendLockAsync_WithExpiredLock_ReturnsFalse()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(60);
        _distributedLock.ExtendLockAsync(_lockHandle, expiry)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _distributedLock.ExtendLockAsync(_lockHandle, expiry);

        // Assert
        result.Should().BeFalse();
        await _distributedLock.Received(1).ExtendLockAsync(_lockHandle, expiry);
    }

    [Fact]
    public async Task ExtendLockAsync_WithCancellation_PropagatesCancellation()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(60);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _distributedLock.ExtendLockAsync(_lockHandle, expiry, cts.Token)
            .Returns(Task.FromCanceled<bool>(cts.Token));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _distributedLock.ExtendLockAsync(_lockHandle, expiry, cts.Token));

        await _distributedLock.Received(1).ExtendLockAsync(_lockHandle, expiry, cts.Token);
    }

    [Fact]
    public async Task ExtendLockAsync_WithException_PropagatesException()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(60);
        var expectedException = new InvalidOperationException("Lock not found");

        _distributedLock.ExtendLockAsync(_lockHandle, expiry)
            .Returns(Task.FromException<bool>(expectedException));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _distributedLock.ExtendLockAsync(_lockHandle, expiry));

        exception.Message.Should().Be("Lock not found");
        await _distributedLock.Received(1).ExtendLockAsync(_lockHandle, expiry);
    }

    #endregion

    #region Multiple Operations Tests

    [Fact]
    public async Task MultipleLockOperations_WorkIndependently()
    {
        // Arrange
        const string resource1 = "resource1";
        const string resource2 = "resource2";
        var expiry = TimeSpan.FromSeconds(30);
        var lockHandle1 = Substitute.For<ILockHandle>();
        var lockHandle2 = Substitute.For<ILockHandle>();

        _distributedLock.AcquireLockAsync(resource1, expiry)
            .Returns(Task.FromResult<ILockHandle?>(lockHandle1));
        _distributedLock.AcquireLockAsync(resource2, expiry)
            .Returns(Task.FromResult<ILockHandle?>(lockHandle2));
        _distributedLock.IsLockedAsync(resource1)
            .Returns(Task.FromResult(true));
        _distributedLock.IsLockedAsync(resource2)
            .Returns(Task.FromResult(true));

        // Act
        var handle1 = await _distributedLock.AcquireLockAsync(resource1, expiry);
        var handle2 = await _distributedLock.AcquireLockAsync(resource2, expiry);
        var isLocked1 = await _distributedLock.IsLockedAsync(resource1);
        var isLocked2 = await _distributedLock.IsLockedAsync(resource2);

        // Assert
        handle1.Should().BeSameAs(lockHandle1);
        handle2.Should().BeSameAs(lockHandle2);
        isLocked1.Should().BeTrue();
        isLocked2.Should().BeTrue();

        await _distributedLock.Received(1).AcquireLockAsync(resource1, expiry);
        await _distributedLock.Received(1).AcquireLockAsync(resource2, expiry);
        await _distributedLock.Received(1).IsLockedAsync(resource1);
        await _distributedLock.Received(1).IsLockedAsync(resource2);
    }

    [Fact]
    public async Task AcquireExtendRelease_FullLifecycle()
    {
        // Arrange
        const string resource = "test-resource";
        var initialExpiry = TimeSpan.FromSeconds(30);
        var extendedExpiry = TimeSpan.FromSeconds(60);

        _distributedLock.AcquireLockAsync(resource, initialExpiry)
            .Returns(Task.FromResult<ILockHandle?>(_lockHandle));
        _distributedLock.ExtendLockAsync(_lockHandle, extendedExpiry)
            .Returns(Task.FromResult(true));
        _lockHandle.ReleaseAsync()
            .Returns(Task.CompletedTask);

        // Act
        var handle = await _distributedLock.AcquireLockAsync(resource, initialExpiry);
        var extended = await _distributedLock.ExtendLockAsync(handle!, extendedExpiry);
        await handle!.ReleaseAsync();

        // Assert
        handle.Should().NotBeNull();
        extended.Should().BeTrue();

        await _distributedLock.Received(1).AcquireLockAsync(resource, initialExpiry);
        await _distributedLock.Received(1).ExtendLockAsync(_lockHandle, extendedExpiry);
        await _lockHandle.Received(1).ReleaseAsync();
    }

    #endregion
}