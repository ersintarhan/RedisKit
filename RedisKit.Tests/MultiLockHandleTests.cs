using System.Collections.ObjectModel;
using FluentAssertions;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Services;
using Xunit;

namespace RedisKit.Tests;

/// <summary>
///     Tests for MultiLockHandle class
/// </summary>
public class MultiLockHandleTests
{
    private readonly List<ILockHandle> _lockHandles;
    private readonly MultiLockHandle _multiLockHandle;

    public MultiLockHandleTests()
    {
        _lockHandles = new List<ILockHandle>
        {
            Substitute.For<ILockHandle>(),
            Substitute.For<ILockHandle>(),
            Substitute.For<ILockHandle>()
        };

        _multiLockHandle = new MultiLockHandle(_lockHandles);
    }

    [Fact]
    public void Constructor_WithNullLocks_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MultiLockHandle(null!));
        exception.ParamName.Should().Be("locks");
    }

    [Fact]
    public void Constructor_WithValidLocks_InitializesCorrectly()
    {
        // Arrange
        var locks = new List<ILockHandle>
        {
            Substitute.For<ILockHandle>(),
            Substitute.For<ILockHandle>()
        };

        // Act
        var multiLock = new MultiLockHandle(locks);

        // Assert
        multiLock.Should().NotBeNull();
        multiLock.Locks.Should().HaveCount(2);
        multiLock.Locks.Should().BeEquivalentTo(locks);
    }

    [Fact]
    public void Locks_ReturnsReadOnlyList()
    {
        // Act
        var locks = _multiLockHandle.Locks;

        // Assert
        locks.Should().NotBeNull();
        locks.Should().HaveCount(3);
        locks.Should().BeOfType<ReadOnlyCollection<ILockHandle>>();
        locks.Should().BeEquivalentTo(_lockHandles);
    }

    [Fact]
    public async Task ReleaseAllAsync_ReleasesAllLocks()
    {
        // Arrange
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        // Act
        await _multiLockHandle.ReleaseAllAsync();

        // Assert
        foreach (var lockHandle in _lockHandles) await lockHandle.Received(1).ReleaseAsync();
    }

    [Fact]
    public async Task ReleaseAllAsync_WithCancellationToken_PassesTokenToAllLocks()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(cts.Token)
                .Returns(Task.CompletedTask);

        // Act
        await _multiLockHandle.ReleaseAllAsync(cts.Token);

        // Assert
        foreach (var lockHandle in _lockHandles) await lockHandle.Received(1).ReleaseAsync(cts.Token);
    }

    [Fact]
    public async Task ReleaseAllAsync_WhenAlreadyDisposed_DoesNothing()
    {
        // Arrange
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        await _multiLockHandle.DisposeAsync();

        // Clear received calls
        foreach (var lockHandle in _lockHandles) lockHandle.ClearReceivedCalls();

        // Act
        await _multiLockHandle.ReleaseAllAsync();

        // Assert
        foreach (var lockHandle in _lockHandles) await lockHandle.DidNotReceive().ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseAllAsync_WithFailingLock_StillReleasesOthers()
    {
        // Arrange
        _lockHandles[0].ReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _lockHandles[1].ReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Release failed")));
        _lockHandles[2].ReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _multiLockHandle.ReleaseAllAsync());

        // All locks should have been attempted to release
        foreach (var lockHandle in _lockHandles) await lockHandle.Received(1).ReleaseAsync();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesAllLocks()
    {
        // Arrange
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        // Act
        await _multiLockHandle.DisposeAsync();

        // Assert
        foreach (var lockHandle in _lockHandles) await lockHandle.Received(1).ReleaseAsync();
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_OnlyReleasesOnce()
    {
        // Arrange
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        // Act
        await _multiLockHandle.DisposeAsync();
        await _multiLockHandle.DisposeAsync();
        await _multiLockHandle.DisposeAsync();

        // Assert
        foreach (var lockHandle in _lockHandles) await lockHandle.Received(1).ReleaseAsync();
    }

    [Fact]
    public async Task Dispose_ReleasesAllLocks()
    {
        // Arrange
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        // Act
        _multiLockHandle.Dispose();

        // Assert - Wait a bit for the async operation to complete
        await Task.Delay(100);

        foreach (var lockHandle in _lockHandles) _ = lockHandle.Received().ReleaseAsync();
    }

    [Fact]
    public async Task Dispose_MultipleCalls_OnlyReleasesOnce()
    {
        // Arrange
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        // Act
        _multiLockHandle.Dispose();
        _multiLockHandle.Dispose();
        _multiLockHandle.Dispose();

        // Assert - Wait a bit for the async operation to complete
        await Task.Delay(100);

        foreach (var lockHandle in _lockHandles) _ = lockHandle.Received(1).ReleaseAsync();
    }

    [Fact]
    public async Task DisposeAsync_ThenDispose_DoesNotReleaseAgain()
    {
        // Arrange
        foreach (var lockHandle in _lockHandles)
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        // Act
        await _multiLockHandle.DisposeAsync();

        // Clear received calls
        foreach (var lockHandle in _lockHandles) lockHandle.ClearReceivedCalls();

        _multiLockHandle.Dispose();

        // Assert - Wait a bit to ensure no async operation happens
        await Task.Delay(100);

        foreach (var lockHandle in _lockHandles) await lockHandle.DidNotReceive().ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EmptyLockList_WorksCorrectly()
    {
        // Arrange
        var emptyLocks = new List<ILockHandle>();
        var multiLock = new MultiLockHandle(emptyLocks);

        // Act & Assert
        multiLock.Locks.Should().BeEmpty();

        // Should not throw
        var act = async () => await multiLock.ReleaseAllAsync();
        act.Should().NotThrowAsync();

        multiLock.Dispose();
    }

    [Fact]
    public async Task SingleLock_WorksCorrectly()
    {
        // Arrange
        var singleLock = Substitute.For<ILockHandle>();
        singleLock.ReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var multiLock = new MultiLockHandle(new List<ILockHandle> { singleLock });

        // Act
        await multiLock.ReleaseAllAsync();

        // Assert
        multiLock.Locks.Should().HaveCount(1);
        multiLock.Locks[0].Should().BeSameAs(singleLock);
        await singleLock.Received(1).ReleaseAsync();
    }

    [Fact]
    public async Task LargeLockList_ReleasesAllEfficiently()
    {
        // Arrange
        var largeLockList = new List<ILockHandle>();
        for (var i = 0; i < 100; i++)
        {
            var lockHandle = Substitute.For<ILockHandle>();
            lockHandle.ReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            largeLockList.Add(lockHandle);
        }

        var multiLock = new MultiLockHandle(largeLockList);

        // Act
        await multiLock.ReleaseAllAsync();

        // Assert
        multiLock.Locks.Should().HaveCount(100);
        foreach (var lockHandle in largeLockList) await lockHandle.Received(1).ReleaseAsync();
    }
}