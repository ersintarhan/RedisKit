using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RedisKit.Extensions;
using RedisKit.Interfaces;
using RedisKit.Models;
using Xunit;

namespace RedisKit.Tests.Integration;

public class ServiceRegistrationTests : IntegrationTestBase
{
    [Fact]
    public void AllServices_ShouldBeRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisKit(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.CacheKeyPrefix = "test:";
            options.Serializer = SerializerType.SystemTextJson;
        });

        var provider = services.BuildServiceProvider();

        // Act & Assert - Core services
        var cacheService = provider.GetService<IRedisCacheService>();
        cacheService.Should().NotBeNull("IRedisCacheService should be registered");

        var pubSubService = provider.GetService<IRedisPubSubService>();
        pubSubService.Should().NotBeNull("IRedisPubSubService should be registered");

        var streamService = provider.GetService<IRedisStreamService>();
        streamService.Should().NotBeNull("IRedisStreamService should be registered");

        var distributedLock = provider.GetService<IDistributedLock>();
        distributedLock.Should().NotBeNull("IDistributedLock should be registered");

        // Redis 7.x services
        var functionService = provider.GetService<IRedisFunction>();
        functionService.Should().NotBeNull("IRedisFunction should be registered");

        var shardedPubSub = provider.GetService<IRedisShardedPubSub>();
        shardedPubSub.Should().NotBeNull("IRedisShardedPubSub should be registered");
    }

    [Fact]
    public async Task RedisFunctionService_ShouldBeAccessible()
    {
        // Act
        var functionService = ServiceProvider.GetService<IRedisFunction>();

        // Assert
        functionService.Should().NotBeNull("IRedisFunction should be resolvable from ServiceProvider");

        if (functionService != null)
        {
            var isSupported = await functionService.IsSupportedAsync();
            Console.WriteLine($"Redis Functions supported: {isSupported}");
        }
    }

    [Fact]
    public async Task RedisShardedPubSubService_ShouldBeAccessible()
    {
        // Act
        var shardedPubSubService = ServiceProvider.GetService<IRedisShardedPubSub>();

        // Assert
        shardedPubSubService.Should().NotBeNull("IRedisShardedPubSub should be resolvable from ServiceProvider");

        if (shardedPubSubService != null)
        {
            var isSupported = await shardedPubSubService.IsSupportedAsync();
            Console.WriteLine($"Sharded Pub/Sub supported: {isSupported}");
        }
    }
}