# Redis Sentinel Configuration

RedisKit provides production-ready support for Redis Sentinel, enabling high availability and automatic failover for your Redis infrastructure.

## Overview

Redis Sentinel provides:
- **Monitoring**: Checks if master and replica instances are working as expected
- **Notification**: Notifies when a monitored Redis instance goes down
- **Automatic Failover**: Promotes a replica to master when the master fails
- **Configuration Provider**: Provides master discovery for clients

## Basic Configuration

### Using appsettings.json

```json
{
  "Redis": {
    "Sentinel": {
      "Endpoints": [
        "localhost:26379",
        "localhost:26380",
        "localhost:26381"
      ],
      "ServiceName": "mymaster",
      "RedisPassword": "your_redis_password",
      "EnableFailoverHandling": true
    }
  }
}
```

### Using Code Configuration

```csharp
services.AddRedisKit(options =>
{
    options.Sentinel = new SentinelOptions
    {
        Endpoints = new List<string> 
        { 
            "sentinel1.example.com:26379",
            "sentinel2.example.com:26379",
            "sentinel3.example.com:26379"
        },
        ServiceName = "mymaster",
        RedisPassword = "your_redis_password",
        SentinelPassword = null, // If Sentinel requires auth
        EnableFailoverHandling = true,
        HealthCheckInterval = TimeSpan.FromSeconds(30),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        UseSsl = false
    };
});
```

## Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `Endpoints` | List of Sentinel endpoints (host:port) | Empty list |
| `ServiceName` | Name of the Redis service as configured in Sentinel | "mymaster" |
| `RedisPassword` | Password for Redis master/replica authentication | null |
| `SentinelPassword` | Password for Sentinel authentication (if required) | null |
| `EnableFailoverHandling` | Enable automatic failover handling | true |
| `HealthCheckInterval` | Interval for checking Sentinel status | 30 seconds |
| `ConnectTimeout` | Timeout for connecting to Sentinel | 5 seconds |
| `UseSsl` | Enable TLS/SSL connection to Sentinel | false |

## Docker Compose Setup

Use the provided `docker-compose.sentinel.yml` for local development and testing:

```bash
# Start Sentinel cluster
docker-compose -f docker-compose.sentinel.yml up -d

# View logs
docker-compose -f docker-compose.sentinel.yml logs -f

# Test failover by stopping master
docker-compose -f docker-compose.sentinel.yml stop redis-master

# Clean up
docker-compose -f docker-compose.sentinel.yml down
```

## Production Deployment

### Prerequisites

1. **Minimum 3 Sentinel instances** for proper quorum
2. **Odd number of Sentinels** (3, 5, 7) to avoid split-brain
3. **Deploy Sentinels on separate machines** for true HA
4. **Configure appropriate quorum** (usually `(n/2) + 1`)

### Example Production Configuration

```csharp
services.AddRedisKit(options =>
{
    // Sentinel configuration for production
    options.Sentinel = new SentinelOptions
    {
        Endpoints = new List<string> 
        { 
            "sentinel1.prod.example.com:26379",
            "sentinel2.prod.example.com:26379",
            "sentinel3.prod.example.com:26379",
            "sentinel4.prod.example.com:26379",
            "sentinel5.prod.example.com:26379"
        },
        ServiceName = "redis-production",
        RedisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD"),
        SentinelPassword = Environment.GetEnvironmentVariable("SENTINEL_PASSWORD"),
        EnableFailoverHandling = true,
        HealthCheckInterval = TimeSpan.FromSeconds(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        UseSsl = true
    };

    // Additional resilience settings
    options.RetryConfiguration = new RetryConfiguration
    {
        MaxAttempts = 5,
        Strategy = BackoffStrategy.ExponentialWithJitter,
        InitialDelay = TimeSpan.FromMilliseconds(100),
        MaxDelay = TimeSpan.FromSeconds(10)
    };

    options.CircuitBreaker = new CircuitBreakerSettings
    {
        Enabled = true,
        FailureThreshold = 5,
        BreakDuration = TimeSpan.FromSeconds(60),
        HalfOpenMaxAttempts = 3
    };

    options.HealthMonitoring = new HealthMonitoringSettings
    {
        Enabled = true,
        CheckInterval = TimeSpan.FromSeconds(30),
        AutoReconnect = true,
        MaxConsecutiveFailures = 3
    };
});
```

## Failover Handling

RedisKit automatically handles failover scenarios:

1. **Connection Failed**: Automatically attempts to reconnect through Sentinel
2. **Master Changed**: Updates connection to new master
3. **Sentinel Unavailable**: Falls back to other Sentinel instances
4. **Network Partition**: Circuit breaker prevents cascade failures

### Monitoring Failover Events

```csharp
public class RedisFailoverMonitor : IHostedService
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisFailoverMonitor> _logger;

    public RedisFailoverMonitor(IRedisConnection connection, ILogger<RedisFailoverMonitor> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await _connection.GetMultiplexerAsync();
        
        multiplexer.ConnectionFailed += (sender, args) =>
        {
            _logger.LogWarning("Redis connection failed: {FailureType} at {EndPoint}", 
                args.FailureType, args.EndPoint);
        };

        multiplexer.ConnectionRestored += (sender, args) =>
        {
            _logger.LogInformation("Redis connection restored at {EndPoint}", args.EndPoint);
        };

        multiplexer.ConfigurationChanged += (sender, args) =>
        {
            _logger.LogInformation("Redis configuration changed at {EndPoint}", args.EndPoint);
        };
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// Register the monitor
services.AddHostedService<RedisFailoverMonitor>();
```

## Health Checks

Add health checks for monitoring:

```csharp
services.AddHealthChecks()
    .AddRedis(
        name: "redis-sentinel",
        tags: new[] { "redis", "sentinel", "db" });

// In your health check endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

## Testing Failover

### Manual Failover Test

```bash
# 1. Connect to a Sentinel
redis-cli -p 26379

# 2. Check current master
SENTINEL get-master-addr-by-name mymaster

# 3. Force failover
SENTINEL failover mymaster

# 4. Verify new master
SENTINEL get-master-addr-by-name mymaster
```

### Automated Testing

```csharp
[Fact]
public async Task Should_Handle_Sentinel_Failover()
{
    // Arrange
    var key = "test:failover";
    await _cache.SetAsync(key, "before", TimeSpan.FromMinutes(5));
    
    // Act - Simulate failover (requires manual intervention or automation)
    // Stop master container or force failover via Sentinel CLI
    
    await Task.Delay(TimeSpan.FromSeconds(10)); // Wait for failover
    
    // Assert - Should still work with new master
    var value = await _cache.GetAsync<string>(key);
    Assert.Equal("before", value);
    
    await _cache.SetAsync(key, "after", TimeSpan.FromMinutes(5));
    value = await _cache.GetAsync<string>(key);
    Assert.Equal("after", value);
}
```

## Troubleshooting

### Common Issues

1. **"Could not find master"**
   - Verify Sentinel endpoints are correct
   - Check ServiceName matches Sentinel configuration
   - Ensure Sentinels can reach Redis instances

2. **Authentication Failures**
   - Verify RedisPassword is correct
   - Check if Sentinel requires separate authentication
   - Ensure all Redis instances use same password

3. **Failover Not Working**
   - Check Sentinel quorum configuration
   - Verify network connectivity between Sentinels
   - Review Sentinel logs for errors

### Debug Logging

Enable debug logging to troubleshoot issues:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddFilter("RedisKit", LogLevel.Debug);
    builder.AddFilter("StackExchange.Redis", LogLevel.Debug);
});
```

## Best Practices

1. **Use odd number of Sentinels** (3, 5, 7) to maintain quorum
2. **Deploy Sentinels on separate hosts** from Redis instances
3. **Configure appropriate timeouts** based on network latency
4. **Monitor Sentinel health** separately from Redis health
5. **Test failover scenarios** regularly in staging environment
6. **Use connection pooling** through singleton RedisConnection
7. **Implement retry logic** for transient failures
8. **Set up alerts** for failover events
9. **Document recovery procedures** for your team
10. **Keep Sentinel configuration synchronized** across all instances

## Migration from Standalone Redis

To migrate from standalone Redis to Sentinel:

1. **No code changes required** if using RedisKit abstractions
2. **Add Sentinel configuration** to your settings
3. **Deploy Sentinel infrastructure**
4. **Update configuration and restart** application
5. **Monitor for successful connection** through Sentinel

The migration is transparent to application code when using RedisKit services.

## Resources

- [Redis Sentinel Documentation](https://redis.io/docs/manual/sentinel/)
- [RedisKit GitHub Repository](https://github.com/yourusername/RedisKit)
- [StackExchange.Redis Sentinel Support](https://stackexchange.github.io/StackExchange.Redis/Configuration.html#sentinel)