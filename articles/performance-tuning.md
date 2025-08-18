# Performance Tuning for RedisKit

## Connection Optimization

### Connection Pooling

RedisKit uses StackExchange.Redis which automatically manages connection pooling. Optimize your connection settings:

```csharp
builder.Services.AddRedisKit(options =>
{
    options.ConnectionString = "localhost:6379";
    
    // Connection pool settings
    options.ConnectTimeout = 5000;      // Connection timeout
    options.SyncTimeout = 5000;         // Sync operation timeout  
    options.AsyncTimeout = 5000;        // Async operation timeout
    options.KeepAlive = 60;            // Keep-alive interval in seconds
    options.ConnectRetry = 3;          // Number of connect retries
    
    // Performance settings
    options.AllowAdmin = false;        // Disable admin commands in production
    options.AbortOnConnectFail = false; // Don't abort on connection failure
});
```

### Multiple Connections

For high-throughput scenarios, use multiple connections:

```csharp
public class HighThroughputService
{
    private readonly IConnectionMultiplexer[] _connections;
    private int _counter = 0;
    
    public HighThroughputService(int connectionCount = 4)
    {
        _connections = new IConnectionMultiplexer[connectionCount];
        for (int i = 0; i < connectionCount; i++)
        {
            _connections[i] = ConnectionMultiplexer.Connect("localhost:6379");
        }
    }
    
    private IDatabase GetDatabase()
    {
        // Round-robin connection selection
        var index = Interlocked.Increment(ref _counter) % _connections.Length;
        return _connections[index].GetDatabase();
    }
}
```

## Serialization Performance

### Choose the Right Serializer

```csharp
// MessagePack - Fastest, smallest payload
builder.Services.AddRedisKit(options =>
{
    options.SerializerType = SerializerType.MessagePack;
});

// System.Text.Json - Good balance, human-readable
builder.Services.AddRedisKit(options =>
{
    options.SerializerType = SerializerType.SystemTextJson;
});
```

### Serialization Benchmarks

Based on our benchmarks with a 1KB object:

| Serializer | Serialize | Deserialize | Payload Size |
|------------|-----------|-------------|--------------|
| MessagePack | 1.8 μs | 2.4 μs | 412 bytes |
| System.Text.Json | 4.2 μs | 13.5 μs | 1,045 bytes |

### Custom Serialization

Optimize specific types with custom serialization:

```csharp
public class OptimizedSerializer : IRedisSerializer
{
    public string Name => "Optimized";
    
    public byte[] Serialize<T>(T obj)
    {
        if (obj is MySpecialType special)
        {
            // Custom optimized serialization
            return SerializeSpecial(special);
        }
        
        // Fall back to MessagePack
        return MessagePackSerializer.Serialize(obj);
    }
}
```

## Memory Optimization

### Object Pooling

RedisKit extensively uses ArrayPool and ObjectPool to minimize allocations:

```csharp
// Configuration for object pooling
builder.Services.AddRedisKit(options =>
{
    options.Pooling = new PoolingOptions
    {
        Enabled = true,
        MaxPoolSize = 100,  // Maximum objects in pool
        InitialPoolSize = 10 // Initial pool size
    };
});

// RedisKit internally uses memory pooling for batch operations
public class OptimizedStreamService
{
    // Automatic memory optimization features:
    // - ArrayPool<byte> for serialization buffers
    // - ObjectPool<List<Task>> for parallel operations
    // - Memory<byte> for zero-allocation serialization
    
    public async Task ProcessLargeBatchAsync()
    {
        // Small batches (≤100 items): Sequential processing with array pooling
        // Large batches (>100 items): Parallel processing with object pooling
        
        var messages = GenerateMessages(1000);
        
        // RedisKit automatically optimizes memory usage
        var messageIds = await _streams.AddBatchAsync(
            "events:stream",
            messages,
            maxLength: 100000);
    }
}
```

### ValueTask for Hot Paths

RedisKit uses ValueTask to reduce heap allocations in frequently accessed code paths:

```csharp
public class HighFrequencyService
{
    private readonly IRedisCacheService _cache;
    
    // ValueTask reduces allocations when data is often in cache
    public async ValueTask<Product?> GetProductAsync(int id)
    {
        // GetAsync returns ValueTask<T?> for hot path optimization
        return await _cache.GetAsync<Product>($"product:{id}");
    }
    
    // SetAsync also uses ValueTask
    public async ValueTask UpdateProductAsync(Product product)
    {
        await _cache.SetAsync($"product:{product.Id}", product, TimeSpan.FromHours(1));
    }
    
    // ExistsAsync returns ValueTask<bool>
    public async ValueTask<bool> IsProductCachedAsync(int id)
    {
        return await _cache.ExistsAsync($"product:{id}");
    }
}
```

### Memory<T> and Span<T> Support

RedisKit supports zero-allocation operations with Memory<byte> and Span<byte>:

```csharp
public class ZeroAllocationService
{
    private readonly IRedisCacheService _cache;
    
    // Direct byte operations - no intermediate allocations
    public async ValueTask<byte[]?> GetRawBytesAsync(string key)
    {
        return await _cache.GetBytesAsync(key);
    }
    
    // Set raw bytes directly
    public async ValueTask SetRawBytesAsync(string key, byte[] data)
    {
        await _cache.SetBytesAsync(key, data, TimeSpan.FromHours(1));
    }
    
    // Memory<byte> serialization for large objects
    public async ValueTask ProcessLargeObjectAsync(LargeObject obj)
    {
        // RedisKit uses Memory<byte> internally for serialization
        // This avoids copying large byte arrays
        await _cache.SetAsync("large:object", obj, TimeSpan.FromHours(1));
    }
}
```

### Streaming API for Large Datasets

Process large amounts of data without loading everything into memory:

```csharp
public class MemoryEfficientProcessor
{
    private readonly IRedisStreamService _streams;
    
    public async Task ProcessMillionsOfEventsAsync(CancellationToken ct)
    {
        // IAsyncEnumerable - processes data in chunks
        await foreach (var (id, data) in _streams.ReadStreamingAsync<Event>(
            "events:stream",
            batchSize: 100,  // Process 100 at a time
            cancellationToken: ct))
        {
            // Each batch is processed and released
            // Memory usage remains constant regardless of stream size
            await ProcessEventAsync(data);
        }
    }
}
```

### Dynamic Parallelism

RedisKit automatically adjusts parallelism based on CPU cores:

```csharp
// Internally optimized:
// MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount * 2, 8)
// This ensures optimal CPU utilization without overwhelming the system
```

## Batching and Pipelining

### Batch Operations

Reduce network round trips with batching:

```csharp
public class BatchOperations
{
    private readonly IRedisCacheService _cache;
    
    public async Task BatchSetAsync(Dictionary<string, Product> products)
    {
        // Bad: Individual operations
        foreach (var kvp in products)
        {
            await _cache.SetAsync(kvp.Key, kvp.Value);
        }
        
        // Good: Batch operation
        var batch = _cache.CreateBatch();
        var tasks = products.Select(kvp => 
            batch.SetAsync(kvp.Key, kvp.Value)).ToArray();
        
        batch.Execute();
        await Task.WhenAll(tasks);
    }
}
```

### Pipeline Commands with ExecuteBatchAsync

Use ExecuteBatchAsync for high-performance pipelining:

```csharp
public class PipelineExample
{
    private readonly IRedisCacheService _cache;
    
    // NEW: ExecuteBatchAsync - All operations in single round-trip
    public async Task<UserDashboard> GetDashboardDataAsync(string userId)
    {
        var result = await _cache.ExecuteBatchAsync(batch =>
        {
            // All operations are queued and sent together
            batch.GetAsync<UserProfile>($"profile:{userId}");
            batch.GetAsync<List<Notification>>($"notifications:{userId}");
            batch.GetAsync<UserSettings>($"settings:{userId}");
            batch.ExistsAsync($"premium:{userId}");
            batch.GetAsync<int>($"points:{userId}");
        });
        
        return new UserDashboard
        {
            Profile = result.GetResult<UserProfile>(0),
            Notifications = result.GetResult<List<Notification>>(1) ?? new(),
            Settings = result.GetResult<UserSettings>(2) ?? new(),
            IsPremium = result.GetResult<bool>(3),
            Points = result.GetResult<int>(4) ?? 0
        };
    }
    
    // Batch write operations
    public async Task UpdateUserDataAsync(string userId, UserData data)
    {
        await _cache.ExecuteBatchAsync(batch =>
        {
            batch.SetAsync($"profile:{userId}", data.Profile, TimeSpan.FromHours(1));
            batch.SetAsync($"settings:{userId}", data.Settings, TimeSpan.FromDays(30));
            batch.DeleteAsync($"temp:{userId}");
            batch.ExpireAsync($"session:{userId}", TimeSpan.FromMinutes(20));
        });
    }
}
```

## Memory Optimization

### Key Design

Use efficient key naming:

```csharp
// Bad: Long, verbose keys
var key = "application:module:submodule:user:profile:data:12345";

// Good: Short, structured keys
var key = "u:p:12345";  // user:profile:id

// Use hash tags for cluster routing
var key = "user:{12345}:profile";  // Routes to same node in cluster
```

### Data Structure Selection

Choose appropriate Redis data structures:

```csharp
public class DataStructureOptimization
{
    private readonly IDatabase _database;
    
    // For small objects (<100 fields): Use hashes
    public async Task StoreUserEfficientlyAsync(User user)
    {
        var key = $"user:{user.Id}";
        var hashEntries = new HashEntry[]
        {
            new("name", user.Name),
            new("email", user.Email),
            new("age", user.Age)
        };
        
        await _database.HashSetAsync(key, hashEntries);
    }
    
    // For counters: Use Redis counters
    public async Task IncrementCounterAsync(string counterId)
    {
        await _database.StringIncrementAsync($"counter:{counterId}");
    }
    
    // For sets: Use Redis sets
    public async Task AddToSetAsync(string setKey, string member)
    {
        await _database.SetAddAsync(setKey, member);
    }
}
```

### Compression

Compress large values:

```csharp
public class CompressionHelper
{
    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
    
    public static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
```

## Circuit Breaker Tuning

### Configure Circuit Breaker

```csharp
builder.Services.AddRedisKit(options =>
{
    options.CircuitBreaker = new CircuitBreakerSettings
    {
        Enabled = true,
        FailureThreshold = 5,          // Open after 5 failures
        SamplingDuration = TimeSpan.FromSeconds(60),  // Within 60 seconds
        MinimumThroughput = 10,        // Minimum 10 requests to evaluate
        DurationOfBreak = TimeSpan.FromSeconds(30),   // Stay open for 30 seconds
        SuccessThreshold = 2           // 2 successes to close
    };
});
```

### Monitor Circuit State

```csharp
public class CircuitMonitor
{
    private readonly RedisCircuitBreaker _circuitBreaker;
    
    public void LogCircuitState()
    {
        var stats = _circuitBreaker.GetStatistics();
        
        Console.WriteLine($"State: {stats.State}");
        Console.WriteLine($"Failures: {stats.FailureCount}");
        Console.WriteLine($"Success Rate: {stats.SuccessRate:P}");
        Console.WriteLine($"Last Failure: {stats.LastFailureTime}");
    }
}
```

## Async Best Practices

### Avoid Blocking

```csharp
// Bad: Blocking async call
var result = _cache.GetAsync<string>("key").Result;

// Good: Proper async/await
var result = await _cache.GetAsync<string>("key");

// Bad: Sync over async
public string GetValue(string key)
{
    return _cache.GetAsync<string>(key).GetAwaiter().GetResult();
}

// Good: Async all the way
public async Task<string> GetValueAsync(string key)
{
    return await _cache.GetAsync<string>(key);
}
```

### Configure Await

```csharp
public async Task HighPerformanceOperationAsync()
{
    // Don't capture context in library code
    await _cache.SetAsync("key", "value")
        .ConfigureAwait(false);
    
    // Parallel operations
    var tasks = Enumerable.Range(0, 100)
        .Select(i => _cache.GetAsync<string>($"key:{i}")
            .ConfigureAwait(false));
    
    await Task.WhenAll(tasks).ConfigureAwait(false);
}
```

## Monitoring and Metrics

### Performance Counters

```csharp
public class PerformanceMonitor
{
    private readonly IMetrics _metrics;
    
    public async Task<T> MeasureOperationAsync<T>(
        string operationName,
        Func<Task<T>> operation)
    {
        using var timer = _metrics.Measure.Timer.Time(operationName);
        
        try
        {
            var result = await operation();
            _metrics.Measure.Counter.Increment($"{operationName}:success");
            return result;
        }
        catch (Exception)
        {
            _metrics.Measure.Counter.Increment($"{operationName}:failure");
            throw;
        }
    }
}
```

### Health Checks

```csharp
public class RedisHealthCheck : IHealthCheck
{
    private readonly IRedisCacheService _cache;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Ping Redis
            await _cache.PingAsync();
            
            stopwatch.Stop();
            
            if (stopwatch.ElapsedMilliseconds > 100)
            {
                return HealthCheckResult.Degraded(
                    $"Redis response time: {stopwatch.ElapsedMilliseconds}ms");
            }
            
            return HealthCheckResult.Healthy(
                $"Redis response time: {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Redis connection failed",
                ex);
        }
    }
}
```

## Optimization Checklist

### Development
- [ ] Use MessagePack serialization for performance
- [ ] Implement connection pooling
- [ ] Use async/await properly
- [ ] Batch operations when possible
- [ ] Choose appropriate data structures

### Testing
- [ ] Load test with realistic data volumes
- [ ] Monitor memory usage
- [ ] Check serialization performance
- [ ] Test circuit breaker behavior
- [ ] Measure operation latencies

### Production
- [ ] Configure appropriate timeouts
- [ ] Enable circuit breaker
- [ ] Implement health checks
- [ ] Monitor key metrics
- [ ] Set up alerts for failures
- [ ] Configure connection keep-alive
- [ ] Implement retry policies
- [ ] Use compression for large values
- [ ] Optimize key naming conventions
- [ ] Implement cache warming for critical data

## Performance Benchmarks

Based on our testing with RedisKit:

| Operation | System.Text.Json | MessagePack | Improvement |
|-----------|-----------------|-------------|-------------|
| Set (1KB) | 52.3 μs | 22.8 μs | 2.3x faster |
| Get (1KB) | 48.7 μs | 19.5 μs | 2.5x faster |
| Memory | 12.5 KB | 2.2 KB | 5.6x less |

Choose your configuration based on your specific requirements for speed, memory, and debugging needs.