using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests
{
    public class RedisCacheServiceTests : IDisposable
    {
        private readonly Mock<IDatabase> _mockDatabase;
        private readonly Mock<IConnectionMultiplexer> _mockMultiplexer;
        private readonly Mock<ILogger<RedisCacheService>> _mockLogger;
        private readonly Mock<IRedisSerializer> _mockSerializer;
        private readonly RedisCacheService _cacheService;
        private readonly CancellationTokenSource _cts;

        public RedisCacheServiceTests()
        {
            _mockDatabase = new Mock<IDatabase>();
            _mockMultiplexer = new Mock<IConnectionMultiplexer>();
            _mockLogger = new Mock<ILogger<RedisCacheService>>();
            _mockSerializer = new Mock<IRedisSerializer>();
            _cts = new CancellationTokenSource();

            _mockMultiplexer.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockDatabase.Object);

            _mockSerializer.Setup(x => x.Name).Returns("TestSerializer");

            _cacheService = new RedisCacheService(
                _mockDatabase.Object,
                _mockLogger.Object,
                _mockSerializer.Object);
        }

        #region Get Tests

        [Fact]
        public async Task GetAsync_WithValidKey_ReturnsDeserializedValue()
        {
            // Arrange
            var key = "test:key";
            var value = new TestObject { Id = 1, Name = "Test" };
            var serializedData = new byte[] { 1, 2, 3, 4 };
            
            _mockDatabase.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(serializedData);
            _mockSerializer.Setup(x => x.DeserializeAsync<TestObject>(serializedData, It.IsAny<CancellationToken>()))
                .ReturnsAsync(value);

            // Act
            var result = await _cacheService.GetAsync<TestObject>(key, _cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(value.Id, result.Id);
            Assert.Equal(value.Name, result.Name);
        }

        [Fact]
        public async Task GetAsync_WithNonExistentKey_ReturnsNull()
        {
            // Arrange
            var key = "nonexistent:key";
            
            _mockDatabase.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            // Act
            var result = await _cacheService.GetAsync<TestObject>(key, _cts.Token);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_WithNullKey_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _cacheService.GetAsync<TestObject>(null!, _cts.Token));
        }

        [Fact]
        public async Task GetAsync_WithEmptyKey_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _cacheService.GetAsync<TestObject>("", _cts.Token));
        }

        #endregion

        #region Set Tests

        [Fact]
        public async Task SetAsync_WithValidData_StoresSerializedValue()
        {
            // Arrange
            var key = "test:key";
            var value = new TestObject { Id = 1, Name = "Test" };
            var expiry = TimeSpan.FromMinutes(5);
            var serializedData = new byte[] { 1, 2, 3, 4 };
            
            _mockSerializer.Setup(x => x.SerializeAsync(value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serializedData);
            _mockDatabase.Setup(x => x.StringSetAsync(key, It.IsAny<RedisValue>(), expiry, It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.SetAsync(key, value, expiry, _cts.Token);

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.StringSetAsync(key, serializedData, expiry, It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task SetAsync_WithNullValue_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _cacheService.SetAsync("key", (TestObject)null!, TimeSpan.FromMinutes(1), _cts.Token));
        }

        [Fact]
        public async Task SetAsync_WithConditionalSet_RespectsCondition()
        {
            // Arrange
            var key = "test:key";
            var value = new TestObject { Id = 1, Name = "Test" };
            var expiry = TimeSpan.FromMinutes(5);
            var serializedData = new byte[] { 1, 2, 3, 4 };
            var condition = When.NotExists;
            
            _mockSerializer.Setup(x => x.SerializeAsync(value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serializedData);
            _mockDatabase.Setup(x => x.StringSetAsync(key, It.IsAny<RedisValue>(), expiry, condition, It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            // Act
            var result = await _cacheService.SetAsync(key, value, expiry, condition, _cts.Token);

            // Assert
            Assert.False(result);
            _mockDatabase.Verify(x => x.StringSetAsync(key, serializedData, expiry, condition, It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion

        #region Delete Tests

        [Fact]
        public async Task DeleteAsync_WithExistingKey_ReturnsTrue()
        {
            // Arrange
            var key = "test:key";
            _mockDatabase.Setup(x => x.KeyDeleteAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.DeleteAsync(key, _cts.Token);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task DeleteAsync_WithNonExistentKey_ReturnsFalse()
        {
            // Arrange
            var key = "nonexistent:key";
            _mockDatabase.Setup(x => x.KeyDeleteAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            // Act
            var result = await _cacheService.DeleteAsync(key, _cts.Token);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task DeleteManyAsync_WithMultipleKeys_DeletesAll()
        {
            // Arrange
            var keys = new[] { "key1", "key2", "key3" };
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            
            _mockDatabase.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(2);

            // Act
            var result = await _cacheService.DeleteManyAsync(keys, _cts.Token);

            // Assert
            Assert.Equal(2, result);
        }

        #endregion

        #region GetOrSet Tests

        [Fact]
        public async Task GetOrSetAsync_WithCachedValue_ReturnsCachedValue()
        {
            // Arrange
            var key = "test:key";
            var cachedValue = new TestObject { Id = 1, Name = "Cached" };
            var serializedData = new byte[] { 1, 2, 3, 4 };
            
            _mockDatabase.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(serializedData);
            _mockSerializer.Setup(x => x.DeserializeAsync<TestObject>(serializedData, It.IsAny<CancellationToken>()))
                .ReturnsAsync(cachedValue);

            // Act
            var result = await _cacheService.GetOrSetAsync(
                key,
                async () => new TestObject { Id = 2, Name = "New" },
                TimeSpan.FromMinutes(5),
                _cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(cachedValue.Id, result.Id);
            Assert.Equal(cachedValue.Name, result.Name);
            _mockDatabase.Verify(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task GetOrSetAsync_WithNoCachedValue_CallsFactoryAndStores()
        {
            // Arrange
            var key = "test:key";
            var newValue = new TestObject { Id = 2, Name = "New" };
            var serializedData = new byte[] { 5, 6, 7, 8 };
            var expiry = TimeSpan.FromMinutes(5);
            
            _mockDatabase.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            _mockSerializer.Setup(x => x.SerializeAsync(newValue, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serializedData);
            _mockDatabase.Setup(x => x.StringSetAsync(key, It.IsAny<RedisValue>(), expiry, It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.GetOrSetAsync(
                key,
                async () => newValue,
                expiry,
                _cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(newValue.Id, result.Id);
            Assert.Equal(newValue.Name, result.Name);
            _mockDatabase.Verify(x => x.StringSetAsync(key, serializedData, expiry, It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion

        #region Exists Tests

        [Fact]
        public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
        {
            // Arrange
            var key = "test:key";
            _mockDatabase.Setup(x => x.KeyExistsAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.ExistsAsync(key, _cts.Token);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
        {
            // Arrange
            var key = "nonexistent:key";
            _mockDatabase.Setup(x => x.KeyExistsAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            // Act
            var result = await _cacheService.ExistsAsync(key, _cts.Token);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region Expire Tests

        [Fact]
        public async Task ExpireAsync_WithValidKey_SetsExpiration()
        {
            // Arrange
            var key = "test:key";
            var expiry = TimeSpan.FromMinutes(10);
            _mockDatabase.Setup(x => x.KeyExpireAsync(key, expiry, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.ExpireAsync(key, expiry, _cts.Token);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task ExpireAtAsync_WithValidKey_SetsExpirationTime()
        {
            // Arrange
            var key = "test:key";
            var expireAt = DateTime.UtcNow.AddHours(1);
            _mockDatabase.Setup(x => x.KeyExpireAsync(key, expireAt, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.ExpireAtAsync(key, expireAt, _cts.Token);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region GetMany Tests

        [Fact]
        public async Task GetManyAsync_WithMultipleKeys_ReturnsValues()
        {
            // Arrange
            var keys = new[] { "key1", "key2", "key3" };
            var values = new[]
            {
                new TestObject { Id = 1, Name = "Test1" },
                null,
                new TestObject { Id = 3, Name = "Test3" }
            };
            var serializedData = new[]
            {
                new byte[] { 1, 2, 3 },
                null,
                new byte[] { 7, 8, 9 }
            };

            var redisValues = new RedisValue[]
            {
                serializedData[0],
                RedisValue.Null,
                serializedData[2]
            };

            _mockDatabase.Setup(x => x.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValues);
            
            _mockSerializer.Setup(x => x.DeserializeAsync<TestObject>(serializedData[0], It.IsAny<CancellationToken>()))
                .ReturnsAsync(values[0]);
            _mockSerializer.Setup(x => x.DeserializeAsync<TestObject>(serializedData[2], It.IsAny<CancellationToken>()))
                .ReturnsAsync(values[2]);

            // Act
            var result = await _cacheService.GetManyAsync<TestObject>(keys, _cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.Equal(values[0].Id, result["key1"].Id);
            Assert.Null(result["key2"]);
            Assert.Equal(values[2].Id, result["key3"].Id);
        }

        #endregion

        #region SetMany Tests

        [Fact]
        public async Task SetManyAsync_WithMultipleValues_StoresAll()
        {
            // Arrange
            var values = new Dictionary<string, TestObject>
            {
                ["key1"] = new TestObject { Id = 1, Name = "Test1" },
                ["key2"] = new TestObject { Id = 2, Name = "Test2" }
            };
            var expiry = TimeSpan.FromMinutes(5);

            _mockSerializer.Setup(x => x.SerializeAsync(It.IsAny<TestObject>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new byte[] { 1, 2, 3 });

            // Act
            await _cacheService.SetManyAsync(values, expiry, _cts.Token);

            // Assert
            _mockDatabase.Verify(x => x.StringSetAsync(
                It.IsAny<KeyValuePair<RedisKey, RedisValue>[]>(), 
                It.IsAny<CommandFlags>()), 
                Times.AtLeastOnce);
        }

        #endregion

        #region Increment/Decrement Tests

        [Fact]
        public async Task IncrementAsync_WithKey_IncrementsValue()
        {
            // Arrange
            var key = "counter:key";
            var incrementBy = 5;
            _mockDatabase.Setup(x => x.StringIncrementAsync(key, incrementBy, It.IsAny<CommandFlags>()))
                .ReturnsAsync(10);

            // Act
            var result = await _cacheService.IncrementAsync(key, incrementBy, _cts.Token);

            // Assert
            Assert.Equal(10, result);
        }

        [Fact]
        public async Task DecrementAsync_WithKey_DecrementsValue()
        {
            // Arrange
            var key = "counter:key";
            var decrementBy = 3;
            _mockDatabase.Setup(x => x.StringDecrementAsync(key, decrementBy, It.IsAny<CommandFlags>()))
                .ReturnsAsync(7);

            // Act
            var result = await _cacheService.DecrementAsync(key, decrementBy, _cts.Token);

            // Assert
            Assert.Equal(7, result);
        }

        #endregion

        #region Touch Tests

        [Fact]
        public async Task TouchAsync_WithExistingKey_RefreshesExpiration()
        {
            // Arrange
            var key = "test:key";
            _mockDatabase.Setup(x => x.KeyTouchAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.TouchAsync(key, _cts.Token);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region TTL Tests

        [Fact]
        public async Task GetTimeToLiveAsync_WithExpiringKey_ReturnsTimeSpan()
        {
            // Arrange
            var key = "test:key";
            var ttl = TimeSpan.FromMinutes(30);
            _mockDatabase.Setup(x => x.KeyTimeToLiveAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(ttl);

            // Act
            var result = await _cacheService.GetTimeToLiveAsync(key, _cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(ttl, result.Value);
        }

        [Fact]
        public async Task GetTimeToLiveAsync_WithNonExpiringKey_ReturnsNull()
        {
            // Arrange
            var key = "test:key";
            _mockDatabase.Setup(x => x.KeyTimeToLiveAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync((TimeSpan?)null);

            // Act
            var result = await _cacheService.GetTimeToLiveAsync(key, _cts.Token);

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region Refresh Tests

        [Fact]
        public async Task RefreshAsync_WithValidKey_SetsNewExpiration()
        {
            // Arrange
            var key = "test:key";
            var expiry = TimeSpan.FromMinutes(15);
            
            _mockDatabase.Setup(x => x.StringGetAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new byte[] { 1, 2, 3 });
            _mockDatabase.Setup(x => x.StringSetAsync(key, It.IsAny<RedisValue>(), expiry, When.Exists, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.RefreshAsync(key, expiry, _cts.Token);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region Persist Tests

        [Fact]
        public async Task PersistAsync_WithExpiringKey_RemovesExpiration()
        {
            // Arrange
            var key = "test:key";
            _mockDatabase.Setup(x => x.KeyPersistAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _cacheService.PersistAsync(key, _cts.Token);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region GetWithExpiry Tests

        [Fact]
        public async Task GetWithExpiryAsync_WithValidKey_ReturnsValueAndExpiry()
        {
            // Arrange
            var key = "test:key";
            var value = new TestObject { Id = 1, Name = "Test" };
            var serializedData = new byte[] { 1, 2, 3, 4 };
            var expiry = TimeSpan.FromMinutes(30);
            
            _mockDatabase.Setup(x => x.StringGetWithExpiryAsync(key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValueWithExpiry(serializedData, expiry));
            _mockSerializer.Setup(x => x.DeserializeAsync<TestObject>(serializedData, It.IsAny<CancellationToken>()))
                .ReturnsAsync(value);

            // Act
            var result = await _cacheService.GetWithExpiryAsync<TestObject>(key, _cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(value.Id, result.Value.Id);
            Assert.Equal(expiry, result.Expiry);
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public async Task Operations_WithCancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            var key = "test:key";
            _cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => 
                _cacheService.GetAsync<TestObject>(key, _cts.Token));
        }

        [Fact]
        public async Task SetAsync_WithSerializationError_ThrowsAndLogs()
        {
            // Arrange
            var key = "test:key";
            var value = new TestObject { Id = 1, Name = "Test" };
            var exception = new InvalidOperationException("Serialization failed");
            
            _mockSerializer.Setup(x => x.SerializeAsync(value, It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _cacheService.SetAsync(key, value, TimeSpan.FromMinutes(5), _cts.Token));
        }

        #endregion

        public void Dispose()
        {
            _cts?.Dispose();
        }

        private class TestObject
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}