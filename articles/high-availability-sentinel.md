# High Availability with Redis Sentinel

RedisKit provides production-ready support for Redis Sentinel, enabling automatic failover and high availability for your Redis infrastructure without requiring code changes.

## What is Redis Sentinel?

Redis Sentinel is a distributed system designed to provide high availability for Redis. It offers:

- **Monitoring**: Continuous health checks of master and replica instances
- **Automatic Failover**: Promotes a replica to master when the primary fails
- **Service Discovery**: Clients automatically discover the current master
- **Configuration Management**: Updates client configurations during topology changes

## Why Use Redis Sentinel?

### Traditional Redis Challenges

Without Sentinel, Redis deployments face several challenges:

- Single point of failure with one master
- Manual intervention required for failover
- Application downtime during maintenance
- Complex client-side failover logic

### Sentinel Solutions

Sentinel addresses these challenges by providing:

- **Zero-downtime failover**: Automatic promotion of replicas
- **Transparent to applications**: No code changes required
- **Self-healing**: Automatic recovery from failures
- **Scalable monitoring**: Multiple Sentinel instances for consensus

## Setting Up Redis Sentinel

### Basic Configuration

Configure RedisKit to use Sentinel with minimal setup:

```csharp
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Redis with Sentinel
        builder.Services.AddRedisKit(options =>
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
                RedisPassword = Configuration["Redis:Password"],
                EnableFailoverHandling = true
            };
        });

        var app = builder.Build();
        app.Run();
    }
}
```

### Configuration from appsettings.json

```json
{
  "Redis": {
    "Sentinel": {
      "Endpoints": [
        "10.0.1.10:26379",
        "10.0.1.11:26379",
        "10.0.1.12:26379"
      ],
      "ServiceName": "redis-primary",
      "RedisPassword": "${REDIS_PASSWORD}",
      "SentinelPassword": "${SENTINEL_PASSWORD}",
      "EnableFailoverHandling": true,
      "HealthCheckInterval": "00:00:30",
      "ConnectTimeout": "00:00:05",
      "UseSsl": true
    },
    "RetryConfiguration": {
      "MaxAttempts": 5,
      "Strategy": "ExponentialWithJitter",
      "InitialDelay": "00:00:00.100",
      "MaxDelay": "00:00:10"
    }
  }
}
```

## Deployment Architecture

### Recommended Production Setup

For production environments, deploy Redis Sentinel with:

1. **Odd number of Sentinels** (3, 5, or 7) to maintain quorum
2. **Sentinels on separate hosts** from Redis instances
3. **Cross-availability zone deployment** for cloud environments
4. **Appropriate quorum configuration** (majority: `(n/2) + 1`)

```yaml
# Example Docker Compose for development/testing
version: '3.8'

services:
  redis-master:
    image: redis:7-alpine
    command: redis-server --requirepass redis_password
    
  redis-replica-1:
    image: redis:7-alpine
    command: redis-server --replicaof redis-master 6379 --masterauth redis_password --requirepass redis_password
    
  redis-replica-2:
    image: redis:7-alpine
    command: redis-server --replicaof redis-master 6379 --masterauth redis_password --requirepass redis_password
    
  sentinel-1:
    image: redis:7-alpine
    command: redis-sentinel /etc/redis-sentinel.conf
    volumes:
      - ./sentinel.conf:/etc/redis-sentinel.conf
      
  sentinel-2:
    image: redis:7-alpine
    command: redis-sentinel /etc/redis-sentinel.conf
    volumes:
      - ./sentinel.conf:/etc/redis-sentinel.conf
      
  sentinel-3:
    image: redis:7-alpine
    command: redis-sentinel /etc/redis-sentinel.conf
    volumes:
      - ./sentinel.conf:/etc/redis-sentinel.conf
```

### Sentinel Configuration File

```conf
# sentinel.conf
port 26379
dir /tmp

# Monitor Redis master
sentinel monitor mymaster redis-master 6379 2
sentinel auth-pass mymaster redis_password

# Failover settings
sentinel down-after-milliseconds mymaster 5000
sentinel parallel-syncs mymaster 1
sentinel failover-timeout mymaster 10000

# Notification script (optional)
# sentinel notification-script mymaster /path/to/notify.sh
```

## Handling Failover Scenarios

### Automatic Failover Process

When a master fails, Sentinel automatically:

1. **Detects failure** through health checks
2. **Reaches consensus** with other Sentinels
3. **Selects best replica** based on replication offset
4. **Promotes replica** to new master
5. **Reconfigures remaining replicas** to replicate from new master
6. **Updates clients** with new master information

### Application-Level Handling

RedisKit handles failover transparently, but you can monitor events:

```csharp
public class RedisFailoverMonitor : IHostedService
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisFailoverMonitor> _logger;
    private readonly IEmailService _emailService;

    public RedisFailoverMonitor(
        IRedisConnection connection,
        ILogger<RedisFailoverMonitor> logger,
        IEmailService emailService)
    {
        _connection = connection;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await _connection.GetMultiplexerAsync();
        
        // Monitor connection failures
        multiplexer.ConnectionFailed += async (sender, args) =>
        {
            _logger.LogWarning(
                "Redis connection failed: {FailureType} at {EndPoint}", 
                args.FailureType, 
                args.EndPoint);
            
            if (args.FailureType == ConnectionFailureType.UnableToConnect)
            {
                await _emailService.SendAlertAsync(
                    "Redis Connection Failed",
                    $"Connection to {args.EndPoint} failed. Failover may be in progress.");
            }
        };

        // Monitor connection restoration
        multiplexer.ConnectionRestored += (sender, args) =>
        {
            _logger.LogInformation(
                "Redis connection restored at {EndPoint}", 
                args.EndPoint);
        };

        // Monitor configuration changes (new master)
        multiplexer.ConfigurationChanged += async (sender, args) =>
        {
            _logger.LogInformation(
                "Redis configuration changed. New endpoint: {EndPoint}", 
                args.EndPoint);
            
            await _emailService.SendAlertAsync(
                "Redis Master Changed",
                $"New master detected at {args.EndPoint}");
        };
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

## Testing Failover

### Manual Failover Testing

Test failover in your environment:

```bash
# Connect to Sentinel
redis-cli -p 26379

# Check current master
127.0.0.1:26379> SENTINEL get-master-addr-by-name mymaster
1) "10.0.1.10"
2) "6379"

# Force failover
127.0.0.1:26379> SENTINEL failover mymaster
OK

# Verify new master
127.0.0.1:26379> SENTINEL get-master-addr-by-name mymaster
1) "10.0.1.11"
2) "6379"
```

### Automated Testing

```csharp
[Fact]
public async Task Should_Handle_Failover_Gracefully()
{
    // Arrange
    var cache = _serviceProvider.GetRequiredService<IRedisCacheService>();
    var testKey = "failover:test";
    var testValue = "before-failover";
    
    // Act - Set value before failover
    await cache.SetAsync(testKey, testValue, TimeSpan.FromMinutes(5));
    
    // Simulate failover (in test environment)
    await SimulateFailover();
    
    // Wait for Sentinel to complete failover
    await Task.Delay(TimeSpan.FromSeconds(10));
    
    // Assert - Value should still be accessible
    var retrievedValue = await cache.GetAsync<string>(testKey);
    Assert.Equal(testValue, retrievedValue);
    
    // Verify we can write after failover
    await cache.SetAsync(testKey, "after-failover", TimeSpan.FromMinutes(5));
    retrievedValue = await cache.GetAsync<string>(testKey);
    Assert.Equal("after-failover", retrievedValue);
}

private async Task SimulateFailover()
{
    // Use Docker or process management to stop master
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "stop redis-master",
            RedirectStandardOutput = true,
            UseShellExecute = false
        }
    };
    
    process.Start();
    await process.WaitForExitAsync();
}
```

## Monitoring and Observability

### Health Checks

Implement health checks for Sentinel connectivity:

```csharp
public class SentinelHealthCheck : IHealthCheck
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<SentinelHealthCheck> _logger;

    public SentinelHealthCheck(
        IRedisConnection connection,
        ILogger<SentinelHealthCheck> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var multiplexer = await _connection.GetMultiplexerAsync();
            var endpoints = multiplexer.GetEndPoints();
            
            if (!endpoints.Any())
            {
                return HealthCheckResult.Unhealthy("No Redis endpoints available");
            }

            // Check if we can reach the master
            var database = await _connection.GetDatabaseAsync();
            await database.PingAsync();
            
            // Get current master from connection
            var server = multiplexer.GetServer(endpoints.First());
            var isMaster = !server.IsReplica;
            
            var data = new Dictionary<string, object>
            {
                ["endpoint"] = endpoints.First().ToString(),
                ["is_master"] = isMaster,
                ["connected"] = multiplexer.IsConnected
            };

            return HealthCheckResult.Healthy("Redis Sentinel connection healthy", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for Redis Sentinel");
            return HealthCheckResult.Unhealthy("Redis Sentinel health check failed", ex);
        }
    }
}

// Register health check
services.AddHealthChecks()
    .AddCheck<SentinelHealthCheck>("redis-sentinel", tags: new[] { "redis", "sentinel" });
```

### Metrics Collection

Track Sentinel-related metrics:

```csharp
public class SentinelMetricsCollector : IHostedService
{
    private readonly IRedisConnection _connection;
    private readonly IMetrics _metrics;
    private Timer _timer;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(async _ => await CollectMetrics(), null, 
            TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private async Task CollectMetrics()
    {
        var multiplexer = await _connection.GetMultiplexerAsync();
        
        // Track connection metrics
        _metrics.Gauge("redis.sentinel.connected_clients", 
            multiplexer.GetCounters().TotalOutstanding);
        
        _metrics.Gauge("redis.sentinel.connection_failures", 
            multiplexer.GetCounters().ConnectionsFailed);
        
        // Track failover events
        var endpoints = multiplexer.GetEndPoints();
        foreach (var endpoint in endpoints)
        {
            var server = multiplexer.GetServer(endpoint);
            if (server.IsConnected)
            {
                _metrics.Gauge("redis.sentinel.is_master", 
                    server.IsReplica ? 0 : 1,
                    new[] { ("endpoint", endpoint.ToString()) });
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }
}
```

## Best Practices

### 1. Deployment Strategy

- **Use odd numbers**: Deploy 3, 5, or 7 Sentinels for proper quorum
- **Separate hosts**: Don't run Sentinels on the same machines as Redis
- **Network segmentation**: Ensure Sentinels can reach all Redis instances
- **Firewall rules**: Open ports 26379 (Sentinel) and 6379 (Redis)

### 2. Configuration Management

- **Environment variables**: Store passwords in secure vaults
- **Configuration validation**: Verify Sentinel endpoints on startup
- **Timeout tuning**: Adjust based on network latency
- **Retry strategies**: Use exponential backoff with jitter

### 3. Monitoring and Alerting

- **Health checks**: Regular connectivity verification
- **Metrics collection**: Track failover frequency
- **Log aggregation**: Centralize Sentinel and Redis logs
- **Alert fatigue**: Avoid alerting on transient issues

### 4. Testing Strategy

- **Chaos engineering**: Regularly test failover scenarios
- **Load testing**: Verify performance during failover
- **Recovery testing**: Ensure proper recovery after failures
- **Documentation**: Maintain runbooks for common scenarios

## Troubleshooting

### Common Issues and Solutions

#### Issue: "Could not find master"
```csharp
// Solution: Verify Sentinel configuration
var options = new SentinelOptions
{
    Endpoints = new List<string> { /* verify these are correct */ },
    ServiceName = "mymaster", // Must match Sentinel configuration
    // ...
};
```

#### Issue: Authentication failures
```csharp
// Solution: Ensure both passwords are set if required
options.Sentinel = new SentinelOptions
{
    RedisPassword = "redis_password",      // For Redis auth
    SentinelPassword = "sentinel_password", // For Sentinel auth (if configured)
    // ...
};
```

#### Issue: Failover not triggering
```bash
# Check Sentinel configuration
redis-cli -p 26379
> SENTINEL masters
> SENTINEL slaves mymaster
> INFO sentinel

# Verify quorum settings
> SENTINEL ckquorum mymaster
```

## Migration Guide

### Migrating from Standalone Redis

Migrating from standalone Redis to Sentinel is seamless with RedisKit:

```csharp
// Before: Standalone Redis
services.AddRedisKit(options =>
{
    options.ConnectionString = "redis.example.com:6379";
});

// After: Redis with Sentinel
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
        RedisPassword = "your_password"
    };
});
```

No code changes required in your services - RedisKit handles the complexity!

## Performance Considerations

### Connection Pooling

RedisKit automatically manages connection pooling with Sentinel:

- Single shared connection to current master
- Automatic reconnection after failover
- No connection storms during topology changes

### Latency During Failover

Typical failover timeline:

1. **Detection** (5-10 seconds): Time to detect master failure
2. **Consensus** (1-2 seconds): Sentinels agree on failover
3. **Promotion** (1-2 seconds): Replica promoted to master
4. **Client update** (< 1 second): Clients notified of new master

Total downtime: **~10-15 seconds** with default settings

### Optimization Tips

```csharp
// Optimize for faster failover detection
options.Sentinel = new SentinelOptions
{
    HealthCheckInterval = TimeSpan.FromSeconds(10), // More frequent checks
    ConnectTimeout = TimeSpan.FromSeconds(3),       // Faster timeout
    // ...
};

// Configure Sentinel for faster detection
// In sentinel.conf:
// sentinel down-after-milliseconds mymaster 3000  # Faster detection
// sentinel failover-timeout mymaster 10000        # Quicker failover
```

## Summary

Redis Sentinel with RedisKit provides:

- **Zero-downtime deployments**: Automatic failover handling
- **Transparent integration**: No application code changes
- **Production-ready**: Battle-tested high availability
- **Comprehensive monitoring**: Built-in health checks and metrics
- **Easy migration**: Seamless upgrade from standalone Redis

Start using Redis Sentinel today for true high availability in your Redis infrastructure!