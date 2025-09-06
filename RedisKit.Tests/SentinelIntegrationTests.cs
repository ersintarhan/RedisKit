using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Extensions;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using Xunit;
using Xunit.Abstractions;

namespace RedisKit.Tests;

[Trait("Category", "Integration")]
[Trait("Category", "Sentinel")]
public class SentinelIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IRedisConnection _connection;
    private readonly IRedisCacheService _cache;

    public SentinelIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();

        // Configure services with Sentinel
        services.AddRedisKit(options =>
        {
            // Sentinel configuration
            options.Sentinel = new SentinelOptions
            {
                Endpoints = new List<string> 
                { 
                    "localhost:26379", 
                    "localhost:26380", 
                    "localhost:26381" 
                },
                ServiceName = "mymaster",
                RedisPassword = "redis_password",
                EnableFailoverHandling = true,
                HealthCheckInterval = TimeSpan.FromSeconds(10)
            };

            // Connection settings
            options.RetryConfiguration = new RetryConfiguration
            {
                MaxAttempts = 3,
                Strategy = BackoffStrategy.ExponentialWithJitter,
                InitialDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(5)
            };

            options.CircuitBreaker = new CircuitBreakerSettings
            {
                Enabled = true,
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromSeconds(30)
            };
        });

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        _serviceProvider = services.BuildServiceProvider();
        _connection = _serviceProvider.GetRequiredService<IRedisConnection>();
        _cache = _serviceProvider.GetRequiredService<IRedisCacheService>();
    }

    [Fact(Skip = "Requires Redis Sentinel setup")]
    [Trait("Category", "Sentinel")]
    public async Task Should_Connect_To_Redis_Via_Sentinel()
    {
        // Act
        var database = await _connection.GetDatabaseAsync();
        
        // Assert
        Assert.NotNull(database);
        
        // Test basic operation
        var key = $"test:sentinel:{Guid.NewGuid()}";
        await _cache.SetAsync(key, "test-value", TimeSpan.FromSeconds(30));
        var value = await _cache.GetAsync<string>(key);
        
        Assert.Equal("test-value", value);
        
        // Cleanup
        await _cache.DeleteAsync(key);
        
        _output.WriteLine("Successfully connected to Redis via Sentinel and performed operations");
    }

    [Fact(Skip = "Requires Redis Sentinel setup")]
    [Trait("Category", "Sentinel")]
    public async Task Should_Get_Master_Info_From_Sentinel()
    {
        // Act
        var multiplexer = await _connection.GetMultiplexerAsync();
        var endpoints = multiplexer.GetEndPoints();
        
        // Assert
        Assert.NotEmpty(endpoints);
        
        foreach (var endpoint in endpoints)
        {
            var server = multiplexer.GetServer(endpoint);
            _output.WriteLine($"Connected to: {endpoint}, IsMaster: {!server.IsReplica}");
        }
    }

    [Fact(Skip = "Requires Redis Sentinel setup")]
    [Trait("Category", "Sentinel")]
    public async Task Should_Handle_Multiple_Operations_Via_Sentinel()
    {
        // Arrange
        var tasks = new List<Task>();
        var keyPrefix = $"test:sentinel:batch:{Guid.NewGuid()}";
        
        // Act - Perform multiple operations in parallel
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var key = $"{keyPrefix}:{index}";
                await _cache.SetAsync(key, $"value-{index}", TimeSpan.FromSeconds(30));
                var retrieved = await _cache.GetAsync<string>(key);
                Assert.Equal($"value-{index}", retrieved);
            }));
        }
        
        await Task.WhenAll(tasks);
        
        // Cleanup
        var keys = Enumerable.Range(0, 10)
            .Select(i => $"{keyPrefix}:{i}");
        foreach (var key in keys)
        {
            await _cache.DeleteAsync(key);
        }
        
        _output.WriteLine("Successfully performed multiple operations via Sentinel");
    }

    [Fact(Skip = "Requires Redis Sentinel setup")]
    [Trait("Category", "Sentinel")]
    public async Task Should_Maintain_Connection_Health_With_Sentinel()
    {
        // Act
        var healthStatus = _connection.GetHealthStatus();
        
        // Assert
        Assert.True(healthStatus.IsHealthy, "Connection should be healthy");
        Assert.True(healthStatus.LastCheckTime > DateTime.MinValue);
        
        // Perform operations to verify health
        var key = $"test:health:{Guid.NewGuid()}";
        await _cache.SetAsync(key, "health-check", TimeSpan.FromSeconds(10));
        await _cache.DeleteAsync(key);
        
        _output.WriteLine($"Connection health status: IsHealthy={healthStatus.IsHealthy}, " +
                         $"LastCheck={healthStatus.LastCheckTime}");
    }

    [Fact(Skip = "Requires manual failover simulation")]
    [Trait("Category", "Sentinel")]
    public async Task Should_Handle_Failover_Gracefully()
    {
        // This test requires manual intervention:
        // 1. Start the test
        // 2. Stop the master: docker-compose -f docker-compose.sentinel.yml stop redis-master
        // 3. Wait for Sentinel to promote a replica
        // 4. Verify operations continue working
        
        var key = $"test:failover:{Guid.NewGuid()}";
        
        // Write before failover
        await _cache.SetAsync(key, "before-failover", TimeSpan.FromMinutes(5));
        
        _output.WriteLine("Value set. Now stop redis-master container to trigger failover...");
        _output.WriteLine("Waiting 20 seconds for failover to complete...");
        
        await Task.Delay(TimeSpan.FromSeconds(20));
        
        // Try to read after failover
        var value = await _cache.GetAsync<string>(key);
        Assert.Equal("before-failover", value);
        
        // Try to write after failover
        await _cache.SetAsync(key, "after-failover", TimeSpan.FromMinutes(5));
        value = await _cache.GetAsync<string>(key);
        Assert.Equal("after-failover", value);
        
        _output.WriteLine("Successfully handled failover!");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

// Note: To run these tests, ensure Redis Sentinel is running:
// docker-compose -f docker-compose.sentinel.yml up -d
//
// To test failover:
// 1. Run tests
// 2. Stop master: docker-compose -f docker-compose.sentinel.yml stop redis-master
// 3. Verify tests continue working
// 4. Restart: docker-compose -f docker-compose.sentinel.yml start redis-master