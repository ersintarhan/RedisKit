using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NSubstitute;
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
                await cacheService.GetAsync<TestModel>(null!));
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

        [Fact]
        public async Task SetManyAsync_WithLuaScriptSupport_UsesOptimizedPath()
        {
            // Arrange
            var mockDatabase = Substitute.For<IDatabaseAsync>();
            var mockLogger = Substitute.For<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromSeconds(60),
                CacheKeyPrefix = "test:"
            };

            // Setup Lua script support check
            mockDatabase.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
                .Returns(Task.FromResult(RedisResult.Create((RedisValue)"PONG")));

            // Setup Lua script execution for SET with EXPIRE
            mockDatabase.ScriptEvaluateAsync(
                Arg.Is<string>(s => s.Contains("SET") && s.Contains("EX")),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>())
                .Returns(Task.FromResult(RedisResult.Create((RedisValue)3))); // Return count of items set

            var cacheService = new RedisCacheService(mockDatabase, mockLogger, options);

            var values = new Dictionary<string, TestModel>
            {
                ["key1"] = new TestModel { Id = 1, Name = "Test1" },
                ["key2"] = new TestModel { Id = 2, Name = "Test2" },
                ["key3"] = new TestModel { Id = 3, Name = "Test3" }
            };

            // Act
            await cacheService.SetManyAsync(values, TimeSpan.FromSeconds(30));

            // Assert
            // Should check Lua support
            await mockDatabase.Received(1).ScriptEvaluateAsync(
                Arg.Is<string>(s => s.Contains("PING")),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>());

            // Should execute SET with EXPIRE script
            await mockDatabase.Received(1).ScriptEvaluateAsync(
                Arg.Is<string>(s => s.Contains("SET") && s.Contains("EX")),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>());
        }

        [Fact]
        public async Task SetManyAsync_WithoutLuaScriptSupport_UsesFallback()
        {
            // Arrange
            var mockDatabase = Substitute.For<IDatabaseAsync>();
            var mockLogger = Substitute.For<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromSeconds(60),
                CacheKeyPrefix = "test:"
            };

            // Setup Lua script support check to fail
            mockDatabase.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
                .Returns(Task.FromException<RedisResult>(new RedisServerException("ERR unknown command")));

            // Setup MSET
            mockDatabase.StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>())
                .Returns(Task.FromResult(true));

            // Setup EXPIRE
            mockDatabase.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>())
                .Returns(Task.FromResult(true));

            var cacheService = new RedisCacheService(mockDatabase, mockLogger, options);

            var values = new Dictionary<string, TestModel>
            {
                ["key1"] = new TestModel { Id = 1, Name = "Test1" },
                ["key2"] = new TestModel { Id = 2, Name = "Test2" }
            };

            // Act
            await cacheService.SetManyAsync(values, TimeSpan.FromSeconds(30));

            // Assert
            // Should use MSET
            await mockDatabase.Received(1).StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>());

            // Should call EXPIRE for each key
            await mockDatabase.Received(2).KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>());
        }

        [Fact]
        public async Task SetManyAsync_WithLargeDataset_UsesChunking()
        {
            // Arrange
            var mockDatabase = Substitute.For<IDatabaseAsync>();
            var mockLogger = Substitute.For<ILogger<RedisCacheService>>();
            var options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                DefaultTtl = TimeSpan.FromSeconds(60),
                CacheKeyPrefix = "test:"
            };

            // Setup for no TTL (MSET only)
            mockDatabase.StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>())
                .Returns(Task.FromResult(true));

            var cacheService = new RedisCacheService(mockDatabase, mockLogger, options);

            // Create large dataset (1500 items)
            var values = Enumerable.Range(1, 1500)
                .ToDictionary(
                    i => $"key{i}",
                    i => new TestModel { Id = i, Name = $"Test{i}" }
                );

            // Act
            await cacheService.SetManyAsync(values, TimeSpan.Zero);

            // Assert
            // Should call MSET twice (1000 + 500)
            await mockDatabase.Received(2).StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>());
        }

        [MessagePack.MessagePackObject]
        public class TestModel
        {
            [MessagePack.Key(0)]
            public int Id { get; set; }
            [MessagePack.Key(1)]
            public string? Name { get; set; }
        }
    }
}