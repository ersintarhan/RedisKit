# Pub/Sub Patterns with RedisKit

## Basic Pub/Sub

### Simple Subscribe and Publish

```csharp
public class NotificationService
{
    private readonly IRedisPubSubService _pubSub;
    
    public NotificationService(IRedisPubSubService pubSub)
    {
        _pubSub = pubSub;
    }
    
    // Publisher
    public async Task SendNotificationAsync(string userId, Notification notification)
    {
        await _pubSub.PublishAsync($"user:{userId}:notifications", notification);
    }
    
    // Subscriber
    public async Task StartListeningAsync(string userId)
    {
        await _pubSub.SubscribeAsync<Notification>(
            $"user:{userId}:notifications",
            async (channel, notification) =>
            {
                Console.WriteLine($"New notification: {notification.Title}");
                await ProcessNotification(notification);
            });
    }
}
```

## Pattern-Based Subscriptions

Subscribe to multiple channels using patterns:

```csharp
public class EventMonitor
{
    private readonly IRedisPubSubService _pubSub;
    
    public async Task MonitorAllEventsAsync()
    {
        // Subscribe to all event channels
        await _pubSub.PatternSubscribeAsync<Event>(
            "events:*",
            async (pattern, channel, evt) =>
            {
                Console.WriteLine($"Event on {channel}: {evt.Type}");
                
                // Route based on channel
                if (channel.Contains("critical"))
                {
                    await HandleCriticalEvent(evt);
                }
                else if (channel.Contains("warning"))
                {
                    await HandleWarningEvent(evt);
                }
            });
    }
}
```

## Fan-Out Pattern

Broadcast messages to multiple subscribers:

```csharp
public class BroadcastService
{
    private readonly IRedisPubSubService _pubSub;
    
    public async Task BroadcastUpdateAsync<T>(string category, T update)
    {
        // Publish to multiple channels
        var channels = new[]
        {
            $"updates:{category}:all",
            $"updates:{category}:{DateTime.UtcNow:yyyy-MM-dd}",
            "updates:global"
        };
        
        foreach (var channel in channels)
        {
            await _pubSub.PublishAsync(channel, update);
        }
    }
}
```

## Request-Reply Pattern

Implement request-reply using temporary channels:

```csharp
public class RequestReplyService
{
    private readonly IRedisPubSubService _pubSub;
    
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        string targetService,
        TRequest request)
    {
        var replyChannel = $"reply:{Guid.NewGuid()}";
        var tcs = new TaskCompletionSource<TResponse>();
        
        // Subscribe to reply channel
        var subscription = await _pubSub.SubscribeAsync<TResponse>(
            replyChannel,
            async (channel, response) =>
            {
                tcs.SetResult(response);
            });
        
        // Send request with reply channel
        var envelope = new RequestEnvelope<TRequest>
        {
            Request = request,
            ReplyChannel = replyChannel
        };
        
        await _pubSub.PublishAsync($"service:{targetService}", envelope);
        
        // Wait for response with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        cts.Token.Register(() => tcs.TrySetCanceled());
        
        try
        {
            return await tcs.Task;
        }
        finally
        {
            await subscription.UnsubscribeAsync();
        }
    }
}
```

## Event Sourcing Pattern

Use pub/sub for event streaming:

```csharp
public class EventStore
{
    private readonly IRedisPubSubService _pubSub;
    private readonly IRedisCacheService _cache;
    
    public async Task AppendEventAsync(string aggregateId, DomainEvent evt)
    {
        // Store event
        var key = $"events:{aggregateId}";
        await _cache.ListRightPushAsync(key, evt);
        
        // Publish to subscribers
        await _pubSub.PublishAsync($"events:{evt.GetType().Name}", evt);
        await _pubSub.PublishAsync($"aggregate:{aggregateId}", evt);
    }
    
    public async Task SubscribeToAggregateAsync(
        string aggregateId,
        Func<DomainEvent, Task> handler)
    {
        await _pubSub.SubscribeAsync<DomainEvent>(
            $"aggregate:{aggregateId}",
            async (channel, evt) => await handler(evt));
    }
}
```

## Subscription Management

Track and manage subscriptions:

```csharp
public class SubscriptionManager
{
    private readonly IRedisPubSubService _pubSub;
    private readonly Dictionary<string, SubscriptionToken> _subscriptions = new();
    
    public async Task<string> AddSubscriptionAsync<T>(
        string channel,
        Func<T, Task> handler)
    {
        var subscriptionId = Guid.NewGuid().ToString();
        
        var token = await _pubSub.SubscribeAsync<T>(
            channel,
            async (ch, message) =>
            {
                try
                {
                    await handler(message);
                }
                catch (Exception ex)
                {
                    await HandleError(subscriptionId, ex);
                }
            });
        
        _subscriptions[subscriptionId] = token;
        return subscriptionId;
    }
    
    public async Task RemoveSubscriptionAsync(string subscriptionId)
    {
        if (_subscriptions.TryGetValue(subscriptionId, out var token))
        {
            await token.UnsubscribeAsync();
            _subscriptions.Remove(subscriptionId);
        }
    }
    
    public async Task RemoveAllSubscriptionsAsync()
    {
        var tasks = _subscriptions.Values.Select(t => t.UnsubscribeAsync());
        await Task.WhenAll(tasks);
        _subscriptions.Clear();
    }
}
```

## Rate Limiting

Control message flow with rate limiting:

```csharp
public class RateLimitedSubscriber
{
    private readonly IRedisPubSubService _pubSub;
    private readonly SemaphoreSlim _semaphore;
    
    public RateLimitedSubscriber(IRedisPubSubService pubSub, int maxConcurrency)
    {
        _pubSub = pubSub;
        _semaphore = new SemaphoreSlim(maxConcurrency);
    }
    
    public async Task SubscribeWithRateLimitAsync<T>(
        string channel,
        Func<T, Task> handler)
    {
        await _pubSub.SubscribeAsync<T>(channel, async (ch, message) =>
        {
            await _semaphore.WaitAsync();
            try
            {
                await handler(message);
            }
            finally
            {
                _semaphore.Release();
            }
        });
    }
}
```

## Dead Letter Queue

Handle failed messages:

```csharp
public class ReliableSubscriber
{
    private readonly IRedisPubSubService _pubSub;
    private readonly IRedisCacheService _cache;
    
    public async Task SubscribeWithDLQAsync<T>(
        string channel,
        Func<T, Task> handler,
        int maxRetries = 3)
    {
        await _pubSub.SubscribeAsync<T>(channel, async (ch, message) =>
        {
            var retryCount = 0;
            var success = false;
            
            while (retryCount < maxRetries && !success)
            {
                try
                {
                    await handler(message);
                    success = true;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    
                    if (retryCount >= maxRetries)
                    {
                        // Send to dead letter queue
                        var dlqMessage = new DeadLetterMessage<T>
                        {
                            Message = message,
                            Channel = channel,
                            Error = ex.Message,
                            Timestamp = DateTime.UtcNow,
                            RetryCount = retryCount
                        };
                        
                        await _cache.ListRightPushAsync($"dlq:{channel}", dlqMessage);
                        
                        // Publish DLQ event
                        await _pubSub.PublishAsync("dlq:notifications", dlqMessage);
                    }
                    else
                    {
                        // Exponential backoff
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                    }
                }
            }
        });
    }
}
```

## Monitoring and Metrics

Track pub/sub metrics:

```csharp
public class PubSubMonitor
{
    private readonly IRedisPubSubService _pubSub;
    private readonly Dictionary<string, ChannelMetrics> _metrics = new();
    
    public async Task<SubscriptionToken> MonitorChannelAsync<T>(string channel)
    {
        if (!_metrics.ContainsKey(channel))
        {
            _metrics[channel] = new ChannelMetrics { Channel = channel };
        }
        
        return await _pubSub.SubscribeAsync<T>(channel, async (ch, message) =>
        {
            var metrics = _metrics[channel];
            metrics.MessageCount++;
            metrics.LastMessageTime = DateTime.UtcNow;
            metrics.TotalBytes += EstimateMessageSize(message);
            
            // Track message rate
            metrics.UpdateMessageRate();
        });
    }
    
    public ChannelMetrics GetMetrics(string channel)
    {
        return _metrics.TryGetValue(channel, out var metrics) 
            ? metrics 
            : null;
    }
}

public class ChannelMetrics
{
    public string Channel { get; set; }
    public long MessageCount { get; set; }
    public DateTime LastMessageTime { get; set; }
    public long TotalBytes { get; set; }
    public double MessagesPerSecond { get; private set; }
    
    private readonly Queue<DateTime> _recentMessages = new();
    
    public void UpdateMessageRate()
    {
        var now = DateTime.UtcNow;
        _recentMessages.Enqueue(now);
        
        // Keep only messages from last minute
        while (_recentMessages.Count > 0 && 
               (now - _recentMessages.Peek()).TotalMinutes > 1)
        {
            _recentMessages.Dequeue();
        }
        
        MessagesPerSecond = _recentMessages.Count / 60.0;
    }
}
```

## Best Practices

1. **Use appropriate serialization** - MessagePack for performance, JSON for debugging
2. **Implement error handling** - Always handle exceptions in subscribers
3. **Manage subscriptions** - Clean up subscriptions when done
4. **Use patterns wisely** - Pattern subscriptions can be expensive
5. **Monitor performance** - Track message rates and sizes
6. **Implement retry logic** - Handle transient failures gracefully
7. **Consider message ordering** - Redis pub/sub doesn't guarantee order across channels
8. **Use dead letter queues** - Capture and analyze failed messages