using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using RedisLib.Extensions;
using RedisLib.Interfaces;
using RedisLib.Models;
using RedisLib.Services;

namespace RedisLib.Tests
{
    public class ServiceCollectionExtensionsTests
    {
        private readonly ServiceCollection _services;

        public ServiceCollectionExtensionsTests()
        {
            _services = new ServiceCollection();
            // Add required logging services
            _services.AddLogging();
        }

        #region AddRedisServices Tests

        [Fact]
        public void AddRedisServices_WithNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            IServiceCollection? services = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                services!.AddRedisServices(options => { }));
        }

        [Fact]
        public void AddRedisServices_WithNullConfigureOptions_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                _services.AddRedisServices(null!));
        }

        [Fact]
        public void AddRedisServices_RegistersRedisOptions_Correctly()
        {
            // Arrange
            var expectedConnectionString = "test-redis:6379";
            var expectedTtl = TimeSpan.FromMinutes(10);

            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = expectedConnectionString;
                options.DefaultTtl = expectedTtl;
            });

            var serviceProvider = _services.BuildServiceProvider();
            var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>();

            // Assert
            Assert.NotNull(redisOptions.Value);
            Assert.Equal(expectedConnectionString, redisOptions.Value.ConnectionString);
            Assert.Equal(expectedTtl, redisOptions.Value.DefaultTtl);
        }

        [Fact]
        public void AddRedisServices_RegistersRedisConnection_AsSingleton()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            // Assert
            var descriptor = Assert.Single(_services, d => d.ServiceType == typeof(RedisConnection));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void AddRedisServices_RegistersIDatabaseAsync_AsSingleton()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            // Assert
            var descriptor = Assert.Single(_services, d => d.ServiceType == typeof(IDatabaseAsync));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void AddRedisServices_RegistersISubscriber_AsSingleton()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            // Assert
            var descriptor = Assert.Single(_services, d => d.ServiceType == typeof(ISubscriber));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void AddRedisServices_RegistersCacheService_AsSingleton()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            // Assert
            var descriptor = Assert.Single(_services, d => d.ServiceType == typeof(IRedisCacheService));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void AddRedisServices_RegistersPubSubService_AsSingleton()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            // Assert
            var descriptor = Assert.Single(_services, d => d.ServiceType == typeof(IRedisPubSubService));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void AddRedisServices_RegistersStreamService_AsSingleton()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            // Assert
            var descriptor = Assert.Single(_services, d => d.ServiceType == typeof(IRedisStreamService));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        [Fact]
        public void AddRedisServices_ReturnsServiceCollection_ForChaining()
        {
            // Act
            var result = _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            // Assert
            Assert.Same(_services, result);
        }

        [Fact]
        public void AddRedisServices_WithCustomSerializer_ConfiguresCorrectly()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.Serializer = SerializerType.MessagePack;
            });

            var serviceProvider = _services.BuildServiceProvider();
            var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>();

            // Assert
            Assert.Equal(SerializerType.MessagePack, redisOptions.Value.Serializer);
        }

        [Fact]
        public void AddRedisServices_WithAllOptions_ConfiguresCorrectly()
        {
            // Arrange
            var expectedConnectionString = "redis-cluster:6379";
            var expectedTtl = TimeSpan.FromHours(2);
            var expectedOperationTimeout = TimeSpan.FromSeconds(10);
            var expectedCacheKeyPrefix = "myapp:";
            var expectedSerializer = SerializerType.SystemTextJson;
            var expectedRetryAttempts = 5;
            var expectedRetryDelay = TimeSpan.FromSeconds(2);

            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = expectedConnectionString;
                options.DefaultTtl = expectedTtl;
                options.OperationTimeout = expectedOperationTimeout;
                options.CacheKeyPrefix = expectedCacheKeyPrefix;
                options.Serializer = expectedSerializer;
                options.RetryAttempts = expectedRetryAttempts;
                options.RetryDelay = expectedRetryDelay;
            });

            var serviceProvider = _services.BuildServiceProvider();
            var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>();

            // Assert
            Assert.NotNull(redisOptions.Value);
            Assert.Equal(expectedConnectionString, redisOptions.Value.ConnectionString);
            Assert.Equal(expectedTtl, redisOptions.Value.DefaultTtl);
            Assert.Equal(expectedOperationTimeout, redisOptions.Value.OperationTimeout);
            Assert.Equal(expectedCacheKeyPrefix, redisOptions.Value.CacheKeyPrefix);
            Assert.Equal(expectedSerializer, redisOptions.Value.Serializer);
            Assert.Equal(expectedRetryAttempts, redisOptions.Value.RetryAttempts);
            Assert.Equal(expectedRetryDelay, redisOptions.Value.RetryDelay);
        }

        #endregion

        #region Service Resolution Tests

        [Fact]
        public void AddRedisServices_ServicesCanBeResolved_WithoutErrors()
        {
            // Arrange
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
            });

            var serviceProvider = _services.BuildServiceProvider();

            // Act & Assert - These should not throw
            Assert.NotNull(serviceProvider.GetRequiredService<IOptions<RedisOptions>>());
            Assert.NotNull(serviceProvider.GetRequiredService<RedisConnection>());
            
            // Note: IDatabaseAsync and ISubscriber require actual Redis connection
            // so they would throw in unit tests. These would be tested in integration tests.
        }

        [Fact]
        public void AddRedisServices_MultipleRegistrations_ThrowsOrOverwrites()
        {
            // Act - Register twice with different configurations
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "first:6379";
                options.CacheKeyPrefix = "first:";
            });

            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "second:6379";
                options.CacheKeyPrefix = "second:";
            });

            var serviceProvider = _services.BuildServiceProvider();
            var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>();

            // Assert - The second registration should be active (last wins in Configure)
            Assert.Equal("second:6379", redisOptions.Value.ConnectionString);
            Assert.Equal("second:", redisOptions.Value.CacheKeyPrefix);
        }

        #endregion

        #region Validation Tests

        [Fact]
        public void AddRedisServices_WithEmptyConnectionString_DoesNotThrowDuringRegistration()
        {
            // Registration should succeed, but connection creation would fail later
            // Act & Assert - Should not throw during registration
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "";
            });

            var serviceProvider = _services.BuildServiceProvider();
            var options = serviceProvider.GetRequiredService<IOptions<RedisOptions>>();
            
            // The empty connection string is stored
            Assert.Empty(options.Value.ConnectionString);
        }

        [Fact]
        public void AddRedisServices_WithInvalidTtl_StoresValue()
        {
            // Act
            _services.AddRedisServices(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.DefaultTtl = TimeSpan.FromSeconds(-1); // Invalid TTL
            });

            var serviceProvider = _services.BuildServiceProvider();
            var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>();

            // Assert - The invalid value is stored (validation would happen at usage time)
            Assert.Equal(TimeSpan.FromSeconds(-1), redisOptions.Value.DefaultTtl);
        }

        #endregion
    }
}