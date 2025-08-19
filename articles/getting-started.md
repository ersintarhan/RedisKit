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

### Cache Service with ValueTask (Performance Optimized)

```csharp
using RedisKit.Interfaces;

public class WeatherService
{
    private readonly IRedisCacheService _cache;
    
    public WeatherService(IRedisCacheService cache)
    {
        _cache = cache;
    }
    
    // Using ValueTask for hot path optimization
    public async ValueTask<WeatherData> GetWeatherAsync(string city)
    {
        var key = $"weather:{city}";
        
        // Try to get from cache - ValueTask reduces allocations
        var cached = await _cache.GetAsync<WeatherData>(key);
        if (cached != null)
            return cached;
        
        // Fetch from API
        var weather = await FetchWeatherFromApi(city);
        
        // Store in cache for 5 minutes - ValueTask for performance
        await _cache.SetAsync(key, weather, TimeSpan.FromMinutes(5));
        
        return weather;
    }
    
    // Check if data exists without fetching
    public async ValueTask<bool> HasWeatherDataAsync(string city)
    {
        return await _cache.ExistsAsync($"weather:{city}");
    }
}
```

### High-Performance Batch Operations

```csharp
using RedisKit.Interfaces;

public class DashboardService
{
    private readonly IRedisCacheService _cache;
    
    public DashboardService(IRedisCacheService cache)
    {
        _cache = cache;
    }
    
    // ExecuteBatchAsync - Multiple operations in single round-trip
    public async Task<DashboardData> GetDashboardDataAsync(string userId)
    {
        var result = await _cache.ExecuteBatchAsync(batch =>
        {
            batch.GetAsync<UserProfile>($"profile:{userId}");
            batch.GetAsync<List<Activity>>($"activities:{userId}");
            batch.GetAsync<UserStats>($"stats:{userId}");
            batch.ExistsAsync($"premium:{userId}");
            batch.GetAsync<int>($"notifications:count:{userId}");
        });
        
        return new DashboardData
        {
            Profile = result.GetResult<UserProfile>(0),
            RecentActivities = result.GetResult<List<Activity>>(1) ?? new(),
            Statistics = result.GetResult<UserStats>(2),
            IsPremiumUser = result.GetResult<bool>(3),
            NotificationCount = result.GetResult<int>(4) ?? 0
        };
    }
    
    // Batch update multiple values
    public async Task UpdateUserDataAsync(string userId, UserUpdate update)
    {
        await _cache.ExecuteBatchAsync(batch =>
        {
            batch.SetAsync($"profile:{userId}", update.Profile, TimeSpan.FromHours(1));
            batch.SetAsync($"stats:{userId}", update.Stats, TimeSpan.FromHours(1));
            batch.DeleteAsync($"cache:old:{userId}");
            batch.ExpireAsync($"session:{userId}", TimeSpan.FromMinutes(30));
        });
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

## Redis 7.x Features

### Using Redis Functions

Redis Functions are a new way to extend Redis with server-side scripts. They replace the older EVAL/EVALSHA commands with a more structured approach.

```csharp
using RedisKit.Builders;
using RedisKit.Interfaces;

// Get the Redis Functions service
var functionService = services.GetRequiredService<IRedisFunction>();

// Check if Redis 7.x is supported
if (await functionService.IsSupportedAsync())
{
    // Create a function library
    var library = new FunctionLibraryBuilder()
        .WithName("myapp")
        .WithDescription("My application functions")
        .AddFunction("get_user_score", @"
            function(keys, args)
                local user_id = args[1]
                local score = redis.call('GET', 'user:' .. user_id .. ':score')
                return score or 0
            end
        ")
        .AddReadOnlyFunction("count_users", @"
            function(keys, args)
                return redis.call('DBSIZE')
            end
        ")
        .Build();
    
    // Load the library
    await functionService.LoadAsync(library);
    
    // Call functions
    var score = await functionService.CallAsync<long>("get_user_score", args: new[] { "123" });
    var userCount = await functionService.CallReadOnlyAsync<long>("count_users");
}
```

### Using Sharded Pub/Sub

Sharded Pub/Sub distributes messages across cluster shards for better scalability:

```csharp
using RedisKit.Interfaces;

// Get the Sharded Pub/Sub service
var shardedPubSub = services.GetRequiredService<IRedisShardedPubSub>();

// Check if Sharded Pub/Sub is supported (Redis 7.0+)
if (await shardedPubSub.IsSupportedAsync())
{
    // Subscribe to a sharded channel
    var subscription = await shardedPubSub.SubscribeAsync<NotificationMessage>(
        "notifications:user:123",
        async (message, ct) =>
        {
            Console.WriteLine($"Received on shard {message.ShardId}: {message.Data.Text}");
            await ProcessNotification(message.Data);
        });
    
    // Publish to sharded channel
    var subscribers = await shardedPubSub.PublishAsync(
        "notifications:user:123",
        new NotificationMessage { Text = "Hello from shard!" });
    
    // Unsubscribe when done
    await shardedPubSub.UnsubscribeAsync(subscription);
}

// Note: Sharded Pub/Sub does NOT support pattern subscriptions
// This will throw NotSupportedException:
// await shardedPubSub.SubscribePatternAsync<T>("pattern:*", handler);
```

## Next Steps

- [Advanced Caching Patterns](caching-patterns.md)
- [Pub/Sub Patterns](pubsub-patterns.md)
- [Stream Processing](stream-processing.md)
- [Redis Functions Guide](redis-functions.md)
- [Sharded Pub/Sub Guide](sharded-pubsub.md)
- [Performance Tuning](performance-tuning.md)
- [API Reference](/api/RedisKit.html)