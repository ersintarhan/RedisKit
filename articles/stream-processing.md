# Stream Processing with RedisKit

## Introduction to Redis Streams

Redis Streams provide a powerful append-only log data structure with consumer groups support, perfect for building event-driven architectures and real-time data processing pipelines.

## Basic Stream Operations

### Adding to Streams

```csharp
public class EventPublisher
{
    private readonly IRedisStreamService _streams;
    
    public EventPublisher(IRedisStreamService streams)
    {
        _streams = streams;
    }
    
    public async Task<string> PublishEventAsync(UserEvent userEvent)
    {
        // Add to stream with auto-generated ID
        var messageId = await _streams.AddAsync("events:stream", userEvent);
        Console.WriteLine($"Published event with ID: {messageId}");
        return messageId;
    }
    
    public async Task<string> PublishWithCustomIdAsync(UserEvent userEvent, string customId)
    {
        // Add with custom ID (must be greater than last ID)
        return await _streams.AddAsync("events:stream", userEvent, customId);
    }
}
```

### Reading from Streams

```csharp
public class StreamReader
{
    private readonly IRedisStreamService _streams;
    
    public async Task ReadLatestAsync(int count = 10)
    {
        // Read last N messages
        var messages = await _streams.ReadAsync<UserEvent>(
            "events:stream",
            "-",  // Start from beginning
            count);
        
        foreach (var (id, evt) in messages)
        {
            Console.WriteLine($"ID: {id}, Event: {evt.Type}");
        }
    }
    
    public async Task ReadRangeAsync(string startId, string endId)
    {
        // Read specific range
        var messages = await _streams.RangeAsync<UserEvent>(
            "events:stream",
            startId,
            endId);
        
        ProcessMessages(messages);
    }
}
```

## Consumer Groups

### Creating Consumer Groups

```csharp
public class StreamConsumerSetup
{
    private readonly IRedisStreamService _streams;
    
    public async Task SetupConsumerGroupAsync()
    {
        // Create consumer group starting from beginning
        await _streams.CreateConsumerGroupAsync(
            "events:stream",
            "processing-group",
            "$");  // Start from new messages only
        
        // Or start from beginning
        await _streams.CreateConsumerGroupAsync(
            "events:stream",
            "analytics-group",
            "0");  // Process all messages
    }
}
```

### Consuming with Groups

```csharp
public class StreamConsumer
{
    private readonly IRedisStreamService _streams;
    private readonly string _consumerName;
    
    public StreamConsumer(IRedisStreamService streams, string consumerName)
    {
        _streams = streams;
        _consumerName = consumerName;
    }
    
    public async Task StartConsumingAsync()
    {
        await _streams.ConsumeAsync<OrderEvent>(
            streamKey: "orders:stream",
            groupName: "order-processors",
            consumerName: _consumerName,
            handler: async (order, streamEntry) =>
            {
                try
                {
                    Console.WriteLine($"Processing order: {order.OrderId}");
                    await ProcessOrder(order);
                    
                    // Acknowledge message
                    await _streams.AcknowledgeAsync(
                        "orders:stream",
                        "order-processors",
                        streamEntry.Id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process: {ex.Message}");
                    // Message will be redelivered
                }
            });
    }
}
```

## Advanced Patterns

### Competing Consumers

Distribute work across multiple consumers:

```csharp
public class WorkerPool
{
    private readonly IRedisStreamService _streams;
    private readonly List<Task> _workers = new();
    
    public async Task StartWorkersAsync(int workerCount)
    {
        for (int i = 0; i < workerCount; i++)
        {
            var workerId = $"worker-{i}";
            var worker = StartWorkerAsync(workerId);
            _workers.Add(worker);
        }
        
        await Task.WhenAll(_workers);
    }
    
    private async Task StartWorkerAsync(string workerId)
    {
        while (true)
        {
            try
            {
                var messages = await _streams.ReadGroupAsync<WorkItem>(
                    streamKey: "tasks:stream",
                    groupName: "workers",
                    consumerName: workerId,
                    count: 10,
                    blockMilliseconds: 1000);
                
                foreach (var (id, item, entry) in messages)
                {
                    await ProcessWorkItem(item);
                    await _streams.AcknowledgeAsync(
                        "tasks:stream",
                        "workers",
                        id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Worker {workerId} error: {ex.Message}");
                await Task.Delay(5000); // Back off on error
            }
        }
    }
}
```

### Claim Pending Messages

Handle failed consumers and stuck messages:

```csharp
public class MessageReclaimer
{
    private readonly IRedisStreamService _streams;
    
    public async Task ReclaimStuckMessagesAsync()
    {
        // Get pending messages older than 5 minutes
        var pending = await _streams.GetPendingMessagesAsync(
            "events:stream",
            "processing-group",
            TimeSpan.FromMinutes(5));
        
        foreach (var message in pending)
        {
            if (message.DeliveryCount > 3)
            {
                // Move to dead letter stream
                await _streams.AddAsync("events:dlq", message.Data);
                await _streams.AcknowledgeAsync(
                    "events:stream",
                    "processing-group",
                    message.Id);
            }
            else
            {
                // Reclaim for processing
                await _streams.ClaimAsync(
                    "events:stream",
                    "processing-group",
                    "reclaimer",
                    message.Id);
            }
        }
    }
}
```

### Stream Aggregation

Aggregate data from multiple streams:

```csharp
public class StreamAggregator
{
    private readonly IRedisStreamService _streams;
    
    public async Task AggregateStreamsAsync()
    {
        var streams = new[]
        {
            "sensors:temperature",
            "sensors:humidity",
            "sensors:pressure"
        };
        
        var tasks = streams.Select(stream => 
            _streams.ReadAsync<SensorData>(stream, "-", 100));
        
        var results = await Task.WhenAll(tasks);
        
        var aggregated = new AggregatedSensorData
        {
            Timestamp = DateTime.UtcNow,
            Temperature = ExtractLatest(results[0]),
            Humidity = ExtractLatest(results[1]),
            Pressure = ExtractLatest(results[2])
        };
        
        await _streams.AddAsync("sensors:aggregated", aggregated);
    }
}
```

### Windowed Processing

Process streams in time windows:

```csharp
public class WindowedProcessor
{
    private readonly IRedisStreamService _streams;
    private readonly IRedisCacheService _cache;
    
    public async Task ProcessWindowAsync(TimeSpan windowSize)
    {
        var windowEnd = DateTime.UtcNow;
        var windowStart = windowEnd - windowSize;
        
        var messages = await _streams.RangeAsync<MetricEvent>(
            "metrics:stream",
            $"{windowStart.Ticks}-0",
            $"{windowEnd.Ticks}-0");
        
        var summary = new WindowSummary
        {
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            Count = messages.Count,
            Average = messages.Average(m => m.Item2.Value),
            Max = messages.Max(m => m.Item2.Value),
            Min = messages.Min(m => m.Item2.Value)
        };
        
        await _cache.SetAsync(
            $"window:{windowStart:yyyyMMddHHmmss}",
            summary,
            TimeSpan.FromHours(24));
    }
}
```

### Stream Trimming

Manage stream size:

```csharp
public class StreamMaintenance
{
    private readonly IRedisStreamService _streams;
    
    public async Task TrimByCountAsync(string streamKey, int maxLength)
    {
        // Keep only last N messages
        await _streams.TrimAsync(streamKey, maxLength);
    }
    
    public async Task TrimByAgeAsync(string streamKey, TimeSpan maxAge)
    {
        var cutoffTime = DateTime.UtcNow - maxAge;
        var cutoffId = $"{cutoffTime.Ticks}-0";
        
        // Remove messages older than cutoff
        await _streams.TrimByMinIdAsync(streamKey, cutoffId);
    }
    
    public async Task ApproximateTrimAsync(string streamKey, int approximateMax)
    {
        // Use ~ for approximate trimming (more efficient)
        await _streams.TrimApproximateAsync(streamKey, approximateMax);
    }
}
```

## Monitoring and Health

### Stream Health Monitoring

```csharp
public class StreamHealthMonitor
{
    private readonly IRedisStreamService _streams;
    
    public async Task<StreamHealthInfo> GetHealthAsync(string streamKey)
    {
        var info = await _streams.GetStreamInfoAsync(streamKey);
        var groups = await _streams.GetConsumerGroupsAsync(streamKey);
        
        var health = new StreamHealthInfo
        {
            StreamKey = streamKey,
            Length = info.Length,
            FirstEntry = info.FirstEntry,
            LastEntry = info.LastEntry,
            ConsumerGroups = groups.Select(g => new ConsumerGroupHealth
            {
                Name = g.Name,
                Pending = g.PendingCount,
                LastDeliveredId = g.LastDeliveredId,
                Consumers = g.Consumers
            }).ToList()
        };
        
        // Check for issues
        health.Issues = DetectIssues(health);
        
        return health;
    }
    
    private List<string> DetectIssues(StreamHealthInfo health)
    {
        var issues = new List<string>();
        
        if (health.Length > 1000000)
            issues.Add("Stream size exceeds 1M entries");
        
        foreach (var group in health.ConsumerGroups)
        {
            if (group.Pending > 1000)
                issues.Add($"Group {group.Name} has {group.Pending} pending messages");
                
            if (group.Consumers == 0)
                issues.Add($"Group {group.Name} has no active consumers");
        }
        
        return issues;
    }
}
```

### Consumer Monitoring

```csharp
public class ConsumerMonitor
{
    private readonly IRedisStreamService _streams;
    private readonly Dictionary<string, ConsumerMetrics> _metrics = new();
    
    public async Task MonitorConsumerAsync(
        string streamKey,
        string groupName,
        string consumerName)
    {
        var key = $"{streamKey}:{groupName}:{consumerName}";
        if (!_metrics.ContainsKey(key))
        {
            _metrics[key] = new ConsumerMetrics
            {
                StreamKey = streamKey,
                GroupName = groupName,
                ConsumerName = consumerName
            };
        }
        
        await _streams.ConsumeAsync<object>(
            streamKey,
            groupName,
            consumerName,
            async (message, entry) =>
            {
                var metrics = _metrics[key];
                var startTime = DateTime.UtcNow;
                
                try
                {
                    // Process message
                    await ProcessMessage(message);
                    
                    metrics.ProcessedCount++;
                    metrics.TotalProcessingTime += (DateTime.UtcNow - startTime).TotalMilliseconds;
                }
                catch (Exception ex)
                {
                    metrics.ErrorCount++;
                    metrics.LastError = ex.Message;
                }
                
                metrics.LastProcessedTime = DateTime.UtcNow;
                metrics.AverageProcessingTime = metrics.TotalProcessingTime / metrics.ProcessedCount;
            });
    }
}
```

## Best Practices

1. **Use consumer groups for scalability** - Distribute work across multiple consumers
2. **Implement proper acknowledgment** - Acknowledge only after successful processing
3. **Handle pending messages** - Implement reclaim logic for stuck messages
4. **Monitor stream size** - Trim old messages to control memory usage
5. **Use appropriate IDs** - Let Redis generate IDs unless you need custom ordering
6. **Batch operations** - Read multiple messages at once for efficiency
7. **Implement dead letter queues** - Handle permanently failed messages
8. **Monitor consumer health** - Track pending messages and consumer activity
9. **Use blocking reads wisely** - Balance between latency and CPU usage
10. **Consider retention policies** - Implement automatic trimming based on age or size