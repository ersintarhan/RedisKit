using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;

namespace RedisKit.Tests
{
    public class CacheServiceTests
    {
        [Fact]
        public void Constructor_WithValidParameters_DoesNotThrow()
        {
            // Arrange
            var mockDatabase = new Mock<IDatabaseAsync>();
            var mockLogger = new Mock<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromHours(1),
                CacheKeyPrefix = "test:"
            };

            // Act & Assert
            Assert.NotNull(new RedisCacheService(mockDatabase.Object, mockLogger.Object, options));
        }

        [Fact]
        public async Task GetAsync_WithNullKey_ThrowsArgumentException()
        {
            // Arrange
            var mockDatabase = new Mock<IDatabaseAsync>();
            var mockLogger = new Mock<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromHours(1),
                CacheKeyPrefix = "test:"
            };
            
            var cacheService = new RedisCacheService(mockDatabase.Object, mockLogger.Object, options);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => 
                await cacheService.GetAsync<TestModel>(null));
        }

        [Fact]
        public async Task SetAsync_WithNullKey_ThrowsArgumentException()
        {
            // Arrange
            var mockDatabase = new Mock<IDatabaseAsync>();
            var mockLogger = new Mock<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromHours(1),
                CacheKeyPrefix = "test:"
            };
            
            var cacheService = new RedisCacheService(mockDatabase.Object, mockLogger.Object, options);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => 
                await cacheService.SetAsync<TestModel>(null, new TestModel()));
        }

        [Fact]
        public async Task DeleteAsync_WithNullKey_ThrowsArgumentException()
        {
            // Arrange
            var mockDatabase = new Mock<IDatabaseAsync>();
            var mockLogger = new Mock<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromHours(1),
                CacheKeyPrefix = "test:"
            };
            
            var cacheService = new RedisCacheService(mockDatabase.Object, mockLogger.Object, options);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => 
                await cacheService.DeleteAsync(null));
        }

        [Fact]
        public void SetKeyPrefix_WithNullPrefix_ThrowsArgumentNullException()
        {
            // Arrange
            var mockDatabase = new Mock<IDatabaseAsync>();
            var mockLogger = new Mock<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromHours(1),
                CacheKeyPrefix = "test:"
            };
            
            var cacheService = new RedisCacheService(mockDatabase.Object, mockLogger.Object, options);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                cacheService.SetKeyPrefix(null));
        }

        public class TestModel
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }
    }
}