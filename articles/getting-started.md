# Getting Started with RedisKit

## Installation

Install RedisKit via NuGet:

```bash
dotnet add package RedisKit
```

## Basic Setup

### 1. Configure Services

Add RedisKit to your service collection in `Program.cs`:

```csharp
using RedisKit.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add RedisKit with default configuration
builder.Services.AddRedisKit("localhost:6379");

// Or with custom configuration
builder.Services.AddRedisKit(options =>
{
    options.ConnectionString = "localhost:6379";
    options.DefaultDatabase = 0;
    options.ConnectTimeout = 5000;
    options.SyncTimeout = 5000;
    options.AsyncTimeout = 5000;
    options.SerializerType = SerializerType.SystemTextJson;
});

var app = builder.Build();
```

## Basic Usage

### Cache Service

```csharp
using RedisKit.Interfaces;

public class WeatherService
{
    private readonly IRedisCacheService _cache;
    
    public WeatherService(IRedisCacheService cache)
    {
        _cache = cache;
    }
    
    public async Task<WeatherData> GetWeatherAsync(string city)
    {
        var key = $"weather:{city}";
        
        // Try to get from cache
        var cached = await _cache.GetAsync<WeatherData>(key);
        if (cached != null)
            return cached;
        
        // Fetch from API
        var weather = await FetchWeatherFromApi(city);
        
        // Store in cache for 5 minutes
        await _cache.SetAsync(key, weather, TimeSpan.FromMinutes(5));
        
        return weather;
    }
}
```

### Pub/Sub Service

```csharp
using RedisKit.Interfaces;

public class NotificationService
{
    private readonly IRedisPubSubService _pubSub;
    
    public NotificationService(IRedisPubSubService pubSub)
    {
        _pubSub = pubSub;
    }
    
    // Publisher
    public async Task SendNotificationAsync(string channel, Notification notification)
    {
        await _pubSub.PublishAsync(channel, notification);
    }
    
    // Subscriber
    public async Task StartListeningAsync()
    {
        await _pubSub.SubscribeAsync<Notification>("notifications", async (channel, notification) =>
        {
            Console.WriteLine($"Received: {notification.Message}");
            await ProcessNotification(notification);
        });
    }
}
```

### Stream Service

```csharp
using RedisKit.Interfaces;

public class EventProcessor
{
    private readonly IRedisStreamService _streams;
    
    public EventProcessor(IRedisStreamService streams)
    {
        _streams = streams;
    }
    
    // Producer
    public async Task<string> PublishEventAsync(UserEvent userEvent)
    {
        return await _streams.AddAsync("events:stream", userEvent);
    }
    
    // Consumer
    public async Task StartConsumerAsync()
    {
        await _streams.CreateConsumerGroupAsync("events:stream", "processors");
        
        await _streams.ConsumeAsync<UserEvent>(
            streamKey: "events:stream",
            groupName: "processors",
            consumerName: "worker-1",
            handler: async (message, streamEntry) =>
            {
                Console.WriteLine($"Processing event: {message.EventType}");
                await ProcessEvent(message);
            });
    }
}
```

## Configuration Options

### Connection Configuration

```csharp
builder.Services.AddRedisKit(options =>
{
    // Connection settings
    options.ConnectionString = "localhost:6379,localhost:6380";
    options.Password = "your-password";
    options.DefaultDatabase = 0;
    
    // Timeouts
    options.ConnectTimeout = 5000;
    options.SyncTimeout = 5000;
    options.AsyncTimeout = 5000;
    
    // Retry configuration
    options.ConnectRetry = 3;
    options.ReconnectRetryPolicy = ReconnectRetryPolicy.ExponentialBackoff;
    
    // Serialization
    options.SerializerType = SerializerType.SystemTextJson;
    // or SerializerType.MessagePack for better performance
});
```

### Circuit Breaker Configuration

```csharp
builder.Services.AddRedisKit(options =>
{
    options.CircuitBreaker = new CircuitBreakerSettings
    {
        Enabled = true,
        FailureThreshold = 5,
        SamplingDuration = TimeSpan.FromSeconds(60),
        MinimumThroughput = 10,
        DurationOfBreak = TimeSpan.FromSeconds(30)
    };
});
```

### Health Monitoring

```csharp
builder.Services.AddRedisKit(options =>
{
    options.HealthMonitoring = new HealthMonitoringSettings
    {
        Enabled = true,
        HealthCheckInterval = TimeSpan.FromSeconds(30),
        UnhealthyThreshold = 3
    };
});
```

## Next Steps

- [Advanced Caching Patterns](caching-patterns.md)
- [Pub/Sub Patterns](pubsub-patterns.md)
- [Stream Processing](stream-processing.md)
- [Performance Tuning](performance-tuning.md)
- [API Reference](/api/RedisKit.html)