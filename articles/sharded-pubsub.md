# Sharded Pub/Sub Guide

Sharded Pub/Sub is a new feature introduced in Redis 7.0 that provides better scalability for pub/sub operations in Redis Cluster environments by distributing messages across shards.

## What is Sharded Pub/Sub?

Traditional Redis Pub/Sub broadcasts messages to all nodes in a cluster, which can create significant network overhead. Sharded Pub/Sub solves this by:

- Routing messages only to the shard that owns the channel
- Distributing channels across shards using the same hash slot mechanism as keys
- Reducing inter-node communication in clusters
- Providing better horizontal scalability

## Key Differences from Regular Pub/Sub

| Feature | Regular Pub/Sub | Sharded Pub/Sub |
|---------|----------------|-----------------|
| Message Distribution | All nodes | Single shard |
| Pattern Subscriptions | Supported | **Not Supported** |
| Cluster Scalability | Limited | Excellent |
| Network Usage | High (broadcast) | Low (targeted) |
| Commands | PUBLISH/SUBSCRIBE | SPUBLISH/SSUBSCRIBE |

## Using Sharded Pub/Sub with RedisKit

### Checking Support

Sharded Pub/Sub requires Redis 7.0 or later:

```csharp
var shardedPubSub = services.GetRequiredService<IRedisShardedPubSub>();

if (await shardedPubSub.IsSupportedAsync())
{
    // Redis 7.0+ with sharded pub/sub support
}
else
{
    // Fall back to regular pub/sub
    var regularPubSub = services.GetRequiredService<IRedisPubSubService>();
}
```

### Basic Publishing

```csharp
// Publish a message to a sharded channel
var subscribers = await shardedPubSub.PublishAsync(
    "orders:new",
    new OrderMessage 
    { 
        OrderId = 12345,
        CustomerId = "CUST-001",
        Amount = 99.99m,
        Items = new[] { "Item1", "Item2" }
    });

Console.WriteLine($"Message delivered to {subscribers} subscribers");
```

### Basic Subscription

```csharp
// Subscribe to a sharded channel
var subscriptionToken = await shardedPubSub.SubscribeAsync<OrderMessage>(
    "orders:new",
    async (message, cancellationToken) =>
    {
        Console.WriteLine($"Order received on shard: {message.ShardId}");
        Console.WriteLine($"Order ID: {message.Data.OrderId}");
        Console.WriteLine($"Amount: {message.Data.Amount}");
        
        await ProcessOrder(message.Data);
    });

// Keep the subscription token to unsubscribe later
// The subscription will remain active until explicitly unsubscribed
```

### Unsubscribing

```csharp
// Method 1: Using the subscription token
await shardedPubSub.UnsubscribeAsync(subscriptionToken);

// Method 2: Unsubscribe from channel directly
await shardedPubSub.UnsubscribeAsync("orders:new");
```

## Important Limitation: No Pattern Support

**Sharded Pub/Sub does NOT support pattern subscriptions.** This is a Redis limitation, not a library limitation.

```csharp
// This will throw NotSupportedException
try
{
    await shardedPubSub.SubscribePatternAsync<Message>(
        "orders:*",  // Pattern not supported!
        handler);
}
catch (NotSupportedException ex)
{
    Console.WriteLine("Pattern subscriptions are not supported in sharded pub/sub");
    // Use regular pub/sub for patterns instead
}
```

### Workaround for Pattern-like Behavior

If you need pattern-like behavior with sharded pub/sub, subscribe to multiple specific channels:

```csharp
// Instead of pattern "user:*:notifications"
// Subscribe to specific channels
var channels = new[] 
{ 
    "user:123:notifications",
    "user:456:notifications",
    "user:789:notifications"
};

var subscriptions = new List<SubscriptionToken>();
foreach (var channel in channels)
{
    var token = await shardedPubSub.SubscribeAsync<Notification>(
        channel,
        async (msg, ct) => await HandleNotification(msg));
    subscriptions.Add(token);
}
```

## Statistics and Monitoring

```csharp
// Get sharded pub/sub statistics
var stats = await shardedPubSub.GetStatsAsync();
Console.WriteLine($"Total Channels: {stats.TotalChannels}");
Console.WriteLine($"Total Subscribers: {stats.TotalSubscribers}");
Console.WriteLine($"Collected At: {stats.CollectedAt}");

// Get subscriber count for a specific channel
var count = await shardedPubSub.GetSubscriberCountAsync("orders:new");
Console.WriteLine($"Subscribers for 'orders:new': {count}");
```

## Best Practices

### 1. Channel Naming for Optimal Sharding

Design channel names to distribute evenly across shards:

```csharp
// Good: Includes variable component for distribution
$"notifications:user:{userId}"
$"events:session:{sessionId}"
$"updates:product:{productId}"

// Bad: Static names that always go to same shard
"notifications:all"
"events:global"
"updates:system"
```

### 2. Use Sharded for High-Volume Channels

```csharp
// High-volume: Use sharded pub/sub
await shardedPubSub.PublishAsync($"telemetry:device:{deviceId}", data);

// Low-volume or needs patterns: Use regular pub/sub
await regularPubSub.PublishAsync("system:alerts", alert);
```

### 3. Message Size Considerations

Sharded pub/sub is ideal for frequent, smaller messages:

```csharp
// Good: Frequent small updates
public class LocationUpdate
{
    public string DeviceId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}

// Consider alternatives for large payloads
// Maybe store in Redis and send notification
public class LargeDataNotification
{
    public string DataKey { get; set; }  // Key to fetch actual data
    public string DataType { get; set; }
    public long Size { get; set; }
}
```

### 4. Error Handling

```csharp
var subscription = await shardedPubSub.SubscribeAsync<OrderMessage>(
    "orders:new",
    async (message, cancellationToken) =>
    {
        try
        {
            await ProcessOrder(message.Data);
        }
        catch (Exception ex)
        {
            // Log error but don't throw
            // Throwing would affect other subscribers
            logger.LogError(ex, "Error processing order {OrderId}", 
                message.Data.OrderId);
            
            // Consider dead letter queue
            await StoreFailedMessage(message);
        }
    });
```

### 5. Graceful Shutdown

```csharp
public class OrderProcessingService : IHostedService
{
    private readonly List<SubscriptionToken> _subscriptions = new();
    private readonly IRedisShardedPubSub _shardedPubSub;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = await _shardedPubSub.SubscribeAsync<OrderMessage>(
            "orders:new",
            ProcessOrder);
        _subscriptions.Add(token);
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Unsubscribe all on shutdown
        foreach (var token in _subscriptions)
        {
            await _shardedPubSub.UnsubscribeAsync(token, cancellationToken);
        }
    }
}
```

## When to Use Sharded vs Regular Pub/Sub

### Use Sharded Pub/Sub When:

- Running Redis Cluster (or planning to)
- High message volume per channel
- Need horizontal scalability
- Can work without pattern subscriptions
- Channel names are well-distributed

### Use Regular Pub/Sub When:

- Need pattern subscriptions
- Running standalone Redis
- Broadcasting to many nodes is desired
- Low message volume
- Need Redis < 7.0 compatibility

## Hybrid Approach

You can use both in the same application:

```csharp
public class MessagingService
{
    private readonly IRedisShardedPubSub _shardedPubSub;
    private readonly IRedisPubSubService _regularPubSub;
    
    public async Task PublishUserEventAsync(string userId, UserEvent evt)
    {
        // Use sharded for user-specific high-volume events
        await _shardedPubSub.PublishAsync($"user:{userId}:events", evt);
    }
    
    public async Task PublishSystemAlertAsync(SystemAlert alert)
    {
        // Use regular for system-wide broadcasts
        await _regularPubSub.PublishAsync("system:alerts:*", alert);
    }
    
    public async Task SubscribeToUserEventsAsync(string userId)
    {
        // Sharded subscription for specific user
        await _shardedPubSub.SubscribeAsync<UserEvent>(
            $"user:{userId}:events",
            HandleUserEvent);
    }
    
    public async Task SubscribeToAllAlertsAsync()
    {
        // Regular pattern subscription for all alerts
        await _regularPubSub.SubscribePatternAsync<SystemAlert>(
            "system:alerts:*",
            HandleSystemAlert);
    }
}
```

## Performance Considerations

### Throughput Comparison

In a 3-node Redis Cluster:

```
Regular Pub/Sub:
- 1 publish = 3 network hops (broadcast to all)
- 10,000 msg/sec = 30,000 network operations

Sharded Pub/Sub:
- 1 publish = 1 network hop (targeted shard)
- 10,000 msg/sec = 10,000 network operations
```

### Latency Benefits

```csharp
// Benchmark results (example)
public class PubSubBenchmark
{
    // Regular: ~2.5ms average (cluster broadcast)
    // Sharded: ~0.8ms average (single shard)
    
    [Benchmark]
    public async Task RegularPublish()
    {
        await regularPubSub.PublishAsync("test", message);
    }
    
    [Benchmark]
    public async Task ShardedPublish()
    {
        await shardedPubSub.PublishAsync("test", message);
    }
}
```

## Troubleshooting

### Issue: NotSupportedException

```csharp
// Problem: Pattern subscription attempted
await shardedPubSub.SubscribePatternAsync<T>("pattern:*", handler);
// Error: NotSupportedException

// Solution: Use regular pub/sub for patterns
await regularPubSub.SubscribePatternAsync<T>("pattern:*", handler);
```

### Issue: Messages Not Received

```csharp
// Check if actually subscribed
var stats = await shardedPubSub.GetStatsAsync();
if (stats.TotalChannels == 0)
{
    Console.WriteLine("No active subscriptions");
}

// Verify Redis version supports sharded pub/sub
if (!await shardedPubSub.IsSupportedAsync())
{
    Console.WriteLine("Redis 7.0+ required for sharded pub/sub");
}
```

### Issue: Uneven Shard Distribution

```csharp
// Problem: All messages going to same shard
// Bad channel naming
for (int i = 0; i < 1000; i++)
{
    await shardedPubSub.PublishAsync("channel", message);
}

// Solution: Include variable in channel name
for (int i = 0; i < 1000; i++)
{
    await shardedPubSub.PublishAsync($"channel:{i}", message);
}
```

## Migration Guide

### From Regular to Sharded Pub/Sub

```csharp
// Before: Regular pub/sub
public class OldMessaging
{
    private readonly IRedisPubSubService _pubSub;
    
    public async Task PublishAsync(string channel, Message msg)
    {
        await _pubSub.PublishAsync(channel, msg);
    }
    
    public async Task SubscribeAsync(string pattern)
    {
        // Pattern subscription
        await _pubSub.SubscribePatternAsync<Message>(
            pattern, 
            HandleMessage);
    }
}

// After: Sharded pub/sub with fallback
public class NewMessaging
{
    private readonly IRedisShardedPubSub _shardedPubSub;
    private readonly IRedisPubSubService _regularPubSub;
    
    public async Task PublishAsync(string channel, Message msg)
    {
        // Use sharded for better performance
        if (await _shardedPubSub.IsSupportedAsync())
        {
            await _shardedPubSub.PublishAsync(channel, msg);
        }
        else
        {
            // Fallback to regular
            await _regularPubSub.PublishAsync(channel, msg);
        }
    }
    
    public async Task SubscribeAsync(string channelOrPattern)
    {
        if (channelOrPattern.Contains("*") || channelOrPattern.Contains("?"))
        {
            // Pattern: must use regular
            await _regularPubSub.SubscribePatternAsync<Message>(
                channelOrPattern, 
                HandleMessage);
        }
        else if (await _shardedPubSub.IsSupportedAsync())
        {
            // Specific channel: use sharded
            await _shardedPubSub.SubscribeAsync<Message>(
                channelOrPattern,
                async (msg, ct) => await HandleMessage(msg.Data, ct));
        }
        else
        {
            // Fallback to regular
            await _regularPubSub.SubscribeAsync<Message>(
                channelOrPattern,
                HandleMessage);
        }
    }
}
```

## Next Steps

- [Redis Functions Guide](redis-functions.md)
- [Pub/Sub Patterns](pubsub-patterns.md)
- [Performance Tuning](performance-tuning.md)
- [Stream Processing](stream-processing.md)