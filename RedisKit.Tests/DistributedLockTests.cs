using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;
using Moq;

namespace RedisKit.Tests
{
    public class DistributedLockTests : IDisposable
    {
        private readonly Mock<IConnectionMultiplexer> _mockMultiplexer;
        private readonly Mock<IDatabase> _mockDatabase;
        private readonly RedisDistributedLock _distributedLock;
        private readonly Mock<ILogger<RedisDistributedLock>> _mockLogger;

        public DistributedLockTests()
        {
            _mockMultiplexer = new Mock<IConnectionMultiplexer>();
            _mockDatabase = new Mock<IDatabase>();
            _mockLogger = new Mock<ILogger<RedisDistributedLock>>();
            
            _mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockDatabase.Object);

            var options = Options.Create(new DistributedLockOptions
            {
                EnableAutoRenewal = false,
                DefaultExpiry = TimeSpan.FromSeconds(30)
            });

            _distributedLock = new RedisDistributedLock(
                _mockMultiplexer.Object,
                options,
                _mockLogger.Object);
        }

        [Fact]
        public async Task AcquireLockAsync_WhenResourceIsAvailable_ReturnsLockHandle()
        {
            // Arrange
            var resource = "test-resource";
            var expiry = TimeSpan.FromSeconds(10);
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(true);

            // Act
            var lockHandle = await _distributedLock.AcquireLockAsync(resource, expiry);

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
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(false);

            // Act
            var lockHandle = await _distributedLock.AcquireLockAsync(resource, expiry);

            // Assert
            Assert.Null(lockHandle);
        }

        [Fact]
        public async Task AcquireLockAsync_WithRetry_RetriesUntilSuccess()
        {
            // Arrange
            var resource = "test-resource";
            var expiry = TimeSpan.FromSeconds(10);
            var wait = TimeSpan.FromSeconds(2);
            var retry = TimeSpan.FromMilliseconds(100);
            var attempts = 0;
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(() =>
                {
                    attempts++;
                    return attempts == 3; // Succeed on third attempt
                });

            // Act
            var lockHandle = await _distributedLock.AcquireLockAsync(
                resource, expiry, wait, retry);

            // Assert
            Assert.NotNull(lockHandle);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task AcquireLockAsync_WithRetry_TimesOutAfterWaitPeriod()
        {
            // Arrange
            var resource = "test-resource";
            var expiry = TimeSpan.FromSeconds(10);
            var wait = TimeSpan.FromMilliseconds(500);
            var retry = TimeSpan.FromMilliseconds(100);
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(false);

            // Act
            var startTime = DateTime.UtcNow;
            var lockHandle = await _distributedLock.AcquireLockAsync(
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
            
            _mockDatabase.Setup(db => db.KeyExistsAsync(
                    It.Is<RedisKey>(k => k.ToString().Contains(resource)),
                    CommandFlags.DemandMaster))
                .ReturnsAsync(true);

            // Act
            var isLocked = await _distributedLock.IsLockedAsync(resource);

            // Assert
            Assert.True(isLocked);
        }

        [Fact]
        public async Task IsLockedAsync_WhenNotLocked_ReturnsFalse()
        {
            // Arrange
            var resource = "test-resource";
            
            _mockDatabase.Setup(db => db.KeyExistsAsync(
                    It.Is<RedisKey>(k => k.ToString().Contains(resource)),
                    CommandFlags.DemandMaster))
                .ReturnsAsync(false);

            // Act
            var isLocked = await _distributedLock.IsLockedAsync(resource);

            // Assert
            Assert.False(isLocked);
        }

        [Fact]
        public async Task ExtendLockAsync_WhenOwningLock_ExtendsSuccessfully()
        {
            // Arrange
            var resource = "test-resource";
            var expiry = TimeSpan.FromSeconds(10);
            var newExpiry = TimeSpan.FromSeconds(30);
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(true);

            _mockDatabase.Setup(db => db.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]>(),
                    It.IsAny<RedisValue[]>(),
                    CommandFlags.DemandMaster))
                .ReturnsAsync(RedisResult.Create(1));

            var lockHandle = await _distributedLock.AcquireLockAsync(resource, expiry);
            Assert.NotNull(lockHandle);

            // Act
            var extended = await _distributedLock.ExtendLockAsync(lockHandle, newExpiry);

            // Assert
            Assert.True(extended);
        }

        [Fact]
        public async Task AcquireMultiLockAsync_WhenAllAvailable_AcquiresAll()
        {
            // Arrange
            var resources = new[] { "resource1", "resource2", "resource3" };
            var expiry = TimeSpan.FromSeconds(10);
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(true);

            // Act
            var multiLock = await _distributedLock.AcquireMultiLockAsync(resources, expiry);

            // Assert
            Assert.NotNull(multiLock);
            Assert.Equal(3, multiLock.Locks.Count);
            foreach (var lockHandle in multiLock.Locks)
            {
                Assert.True(lockHandle.IsAcquired);
            }
        }

        [Fact]
        public async Task AcquireMultiLockAsync_WhenOneUnavailable_ReleasesAllAndReturnsNull()
        {
            // Arrange
            var resources = new[] { "resource1", "resource2", "resource3" };
            var expiry = TimeSpan.FromSeconds(10);
            var callCount = 0;
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return callCount != 2; // Fail on second resource
                });

            _mockDatabase.Setup(db => db.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]>(),
                    It.IsAny<RedisValue[]>(),
                    CommandFlags.DemandMaster))
                .ReturnsAsync(RedisResult.Create(1));

            // Act
            var multiLock = await _distributedLock.AcquireMultiLockAsync(resources, expiry);

            // Assert
            Assert.Null(multiLock);
        }

        [Fact]
        public async Task WaitForUnlockAsync_WhenLockReleased_Returns()
        {
            // Arrange
            var resource = "test-resource";
            var timeout = TimeSpan.FromSeconds(2);
            var checkCount = 0;
            
            _mockDatabase.Setup(db => db.KeyExistsAsync(
                    It.Is<RedisKey>(k => k.ToString().Contains(resource)),
                    CommandFlags.DemandMaster))
                .ReturnsAsync(() =>
                {
                    checkCount++;
                    return checkCount < 3; // Unlocked on third check
                });

            // Act
            await _distributedLock.WaitForUnlockAsync(resource, timeout);

            // Assert
            Assert.True(checkCount >= 3);
        }

        [Fact]
        public async Task WaitForUnlockAsync_WhenTimeout_ThrowsTimeoutException()
        {
            // Arrange
            var resource = "test-resource";
            var timeout = TimeSpan.FromMilliseconds(500);
            
            _mockDatabase.Setup(db => db.KeyExistsAsync(
                    It.Is<RedisKey>(k => k.ToString().Contains(resource)),
                    CommandFlags.DemandMaster))
                .ReturnsAsync(true);

            // Act & Assert
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await _distributedLock.WaitForUnlockAsync(resource, timeout));
        }

        [Fact]
        public async Task LockHandle_Dispose_ReleasesLock()
        {
            // Arrange
            var resource = "test-resource";
            var expiry = TimeSpan.FromSeconds(10);
            
            _mockDatabase.Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    When.NotExists,
                    CommandFlags.DemandMaster))
                .ReturnsAsync(true);

            _mockDatabase.Setup(db => db.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]>(),
                    It.IsAny<RedisValue[]>(),
                    CommandFlags.DemandMaster))
                .ReturnsAsync(RedisResult.Create(1));

            // Act
            ILockHandle? lockHandle;
            await using (lockHandle = await _distributedLock.AcquireLockAsync(resource, expiry))
            {
                Assert.NotNull(lockHandle);
                Assert.True(lockHandle.IsAcquired);
            }

            // Assert
            _mockDatabase.Verify(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                CommandFlags.DemandMaster), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public async Task AcquireLockAsync_WithInvalidResource_ThrowsArgumentException(string resource)
        {
            // Arrange
            var expiry = TimeSpan.FromSeconds(10);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await _distributedLock.AcquireLockAsync(resource, expiry));
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

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await _distributedLock.AcquireLockAsync(resource, expiry));
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}