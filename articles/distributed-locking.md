# Distributed Locking with RedisKit

RedisKit provides a robust distributed locking mechanism based on the Redlock algorithm, enabling safe coordination across multiple processes and machines.

## Basic Usage

### Simple Lock Acquisition

```csharp
public class OrderService
{
    private readonly IDistributedLock _distributedLock;
    
    public OrderService(IDistributedLock distributedLock)
    {
        _distributedLock = distributedLock;
    }
    
    public async Task ProcessOrderAsync(string orderId)
    {
        // Acquire lock for this order
        await using var lockHandle = await _distributedLock.AcquireLockAsync(
            $"order:{orderId}",
            TimeSpan.FromSeconds(30));
        
        if (lockHandle != null)
        {
            // We have the lock, process the order
            await ProcessOrderInternal(orderId);
            
            // Lock is automatically released when disposed
        }
        else
        {
            // Could not acquire lock, another process is handling it
            throw new InvalidOperationException($"Order {orderId} is being processed");
        }
    }
}
```

### Lock with Retry

Wait for lock availability with retry logic:

```csharp
public async Task ProcessWithRetryAsync(string resourceId)
{
    var lockHandle = await _distributedLock.AcquireLockAsync(
        resource: $"resource:{resourceId}",
        expiry: TimeSpan.FromSeconds(30),
        wait: TimeSpan.FromSeconds(10),     // Wait up to 10 seconds
        retry: TimeSpan.FromMilliseconds(100) // Retry every 100ms
    );
    
    if (lockHandle != null)
    {
        await using (lockHandle)
        {
            await PerformCriticalOperation(resourceId);
        }
    }
    else
    {
        throw new TimeoutException("Could not acquire lock within timeout period");
    }
}
```

## Advanced Features

### Automatic Lock Renewal

Keep locks alive for long-running operations:

```csharp
services.AddRedisKit(options =>
{
    options.DistributedLock = new DistributedLockOptions
    {
        EnableAutoRenewal = true,
        DefaultExpiry = TimeSpan.FromSeconds(30)
    };
});

// Lock will be automatically renewed every 10 seconds (1/3 of expiry)
await using var lockHandle = await _distributedLock.AcquireLockAsync(
    "long-running-task",
    TimeSpan.FromSeconds(30));

if (lockHandle != null)
{
    // Perform long-running operation
    // Lock is automatically renewed in the background
    await LongRunningOperation();
}
```

### Manual Lock Extension

Extend lock expiry for operations that take longer than expected:

```csharp
public async Task ProcessWithExtensionAsync(string taskId)
{
    var lockHandle = await _distributedLock.AcquireLockAsync(
        $"task:{taskId}",
        TimeSpan.FromSeconds(10));
    
    if (lockHandle != null)
    {
        await using (lockHandle)
        {
            // Start processing
            await StartProcessing(taskId);
            
            // Need more time? Extend the lock
            var extended = await lockHandle.ExtendAsync(TimeSpan.FromSeconds(30));
            if (extended)
            {
                await ContinueProcessing(taskId);
            }
            else
            {
                // Lost the lock, abort operation
                throw new InvalidOperationException("Lost lock during processing");
            }
        }
    }
}
```

### Multi-Resource Locking

Acquire multiple locks atomically:

```csharp
public async Task TransferFundsAsync(string fromAccount, string toAccount, decimal amount)
{
    // Lock both accounts atomically
    var resources = new[] { $"account:{fromAccount}", $"account:{toAccount}" };
    
    var multiLock = await _distributedLock.AcquireMultiLockAsync(
        resources,
        TimeSpan.FromSeconds(30));
    
    if (multiLock != null)
    {
        await using (multiLock)
        {
            // Both accounts are locked, perform transfer
            await DebitAccount(fromAccount, amount);
            await CreditAccount(toAccount, amount);
            await CommitTransaction();
        }
    }
    else
    {
        throw new InvalidOperationException("Could not lock both accounts");
    }
}
```

### Checking Lock Status

Check if a resource is currently locked:

```csharp
public async Task<bool> IsResourceAvailableAsync(string resourceId)
{
    return !await _distributedLock.IsLockedAsync($"resource:{resourceId}");
}

public async Task WaitForResourceAsync(string resourceId)
{
    // Wait for resource to become available
    await _distributedLock.WaitForUnlockAsync(
        $"resource:{resourceId}",
        TimeSpan.FromSeconds(60));
    
    // Resource is now unlocked, try to acquire it
    var lockHandle = await _distributedLock.AcquireLockAsync(
        $"resource:{resourceId}",
        TimeSpan.FromSeconds(30));
    
    // Use the resource...
}
```

## Common Patterns

### Leader Election

Use distributed locks for leader election:

```csharp
public class LeaderElectionService : BackgroundService
{
    private readonly IDistributedLock _distributedLock;
    private readonly ILogger<LeaderElectionService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Try to become leader
                await using var leaderLock = await _distributedLock.AcquireLockAsync(
                    "cluster:leader",
                    TimeSpan.FromSeconds(30),
                    cancellationToken: stoppingToken);
                
                if (leaderLock != null)
                {
                    _logger.LogInformation("Became cluster leader");
                    
                    // Perform leader duties
                    await PerformLeaderDuties(stoppingToken);
                }
                else
                {
                    // Not leader, wait and retry
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in leader election");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
```

### Preventing Duplicate Processing

Ensure operations are processed only once:

```csharp
public class MessageProcessor
{
    private readonly IDistributedLock _distributedLock;
    
    public async Task<bool> ProcessMessageAsync(Message message)
    {
        // Use message ID as lock key to prevent duplicate processing
        var lockKey = $"message:{message.Id}";
        
        // Try to acquire lock with long expiry
        var lockHandle = await _distributedLock.AcquireLockAsync(
            lockKey,
            TimeSpan.FromHours(24)); // Keep lock for 24 hours
        
        if (lockHandle != null)
        {
            try
            {
                // Process the message
                await ProcessInternal(message);
                
                // Keep the lock to prevent reprocessing
                // Don't dispose - let it expire naturally
                return true;
            }
            catch
            {
                // On error, release lock to allow retry
                await lockHandle.ReleaseAsync();
                throw;
            }
        }
        
        // Message already processed
        return false;
    }
}
```

### Rate Limiting

Implement distributed rate limiting:

```csharp
public class RateLimiter
{
    private readonly IDistributedLock _distributedLock;
    private readonly IRedisCacheService _cache;
    
    public async Task<bool> TryAcquireAsync(string clientId, int maxRequests, TimeSpan window)
    {
        var lockKey = $"ratelimit:{clientId}:lock";
        var counterKey = $"ratelimit:{clientId}:counter";
        
        // Use lock to ensure atomic operation
        await using var lockHandle = await _distributedLock.AcquireLockAsync(
            lockKey,
            TimeSpan.FromSeconds(1));
        
        if (lockHandle != null)
        {
            var count = await _cache.GetAsync<int?>(counterKey) ?? 0;
            
            if (count < maxRequests)
            {
                await _cache.IncrementAsync(counterKey);
                
                if (count == 0)
                {
                    // First request in window, set expiry
                    await _cache.ExpireAsync(counterKey, window);
                }
                
                return true;
            }
        }
        
        return false;
    }
}
```

### Job Queue Processing

Coordinate distributed job processing:

```csharp
public class JobProcessor
{
    private readonly IDistributedLock _distributedLock;
    private readonly IRedisStreamService _streams;
    
    public async Task ProcessJobsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Read pending jobs
            var jobs = await _streams.ReadAsync<Job>(
                "jobs:pending",
                count: 10);
            
            foreach (var (jobId, job) in jobs)
            {
                // Lock each job individually
                var lockHandle = await _distributedLock.AcquireLockAsync(
                    $"job:{jobId}",
                    TimeSpan.FromMinutes(5));
                
                if (lockHandle != null)
                {
                    try
                    {
                        await ProcessJob(job);
                        await _streams.AcknowledgeAsync("jobs:pending", "processors", jobId);
                    }
                    finally
                    {
                        await lockHandle.ReleaseAsync();
                    }
                }
            }
            
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}
```

## Configuration

### Lock Options

Configure distributed lock behavior:

```csharp
services.Configure<DistributedLockOptions>(options =>
{
    options.EnableAutoRenewal = true;
    options.DefaultExpiry = TimeSpan.FromSeconds(30);
    options.DefaultWaitTime = TimeSpan.FromSeconds(10);
    options.DefaultRetryInterval = TimeSpan.FromMilliseconds(100);
    options.EnableDeadlockDetection = true;
    options.DeadlockDetectionTimeout = TimeSpan.FromMinutes(5);
    options.EnableMetrics = true;
    options.KeyPrefix = "lock";
});
```

### Monitoring and Metrics

Track lock usage and performance:

```csharp
public class LockMetricsCollector
{
    private readonly IDistributedLock _distributedLock;
    private readonly IMetrics _metrics;
    
    public async Task<ILockHandle?> AcquireWithMetricsAsync(string resource, TimeSpan expiry)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var handle = await _distributedLock.AcquireLockAsync(resource, expiry);
            
            _metrics.Measure.Timer.Time("lock.acquisition.time", stopwatch.Elapsed);
            _metrics.Measure.Counter.Increment(handle != null ? "lock.acquired" : "lock.failed");
            
            return handle;
        }
        catch (Exception)
        {
            _metrics.Measure.Counter.Increment("lock.error");
            throw;
        }
    }
}
```

## Best Practices

1. **Always use using/await using** - Ensure locks are released even on exceptions
2. **Set appropriate expiry times** - Balance between safety and performance
3. **Handle lock acquisition failures** - Always check if lock was acquired
4. **Use meaningful resource names** - Include entity type and ID in lock keys
5. **Avoid nested locks** - Can lead to deadlocks
6. **Monitor lock metrics** - Track acquisition times and failure rates
7. **Implement retry logic** - For critical operations that must succeed
8. **Use lock hierarchies carefully** - Acquire locks in consistent order
9. **Consider lock granularity** - Balance between contention and complexity
10. **Test concurrent scenarios** - Ensure proper behavior under load

## Troubleshooting

### Lock Not Released

If locks are not being released:
- Check for exceptions preventing disposal
- Verify network connectivity
- Monitor Redis memory usage
- Check for process crashes

### High Lock Contention

If experiencing high contention:
- Increase lock granularity
- Reduce lock hold time
- Implement exponential backoff
- Consider alternative patterns

### Deadlock Detection

Enable deadlock detection to identify issues:
```csharp
options.EnableDeadlockDetection = true;
options.DeadlockDetectionTimeout = TimeSpan.FromMinutes(5);
```

The system will log warnings when potential deadlocks are detected.