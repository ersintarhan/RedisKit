# RedisKit API Documentation

Welcome to the RedisKit API reference documentation. This documentation is automatically generated from XML documentation comments in the source code.

## Namespaces

### [RedisKit.Interfaces](RedisKit.Interfaces.html)
Core interfaces for RedisKit services:
- **IRedisCacheService** - Main caching operations interface
- **IRedisPubSubService** - Pub/Sub messaging interface  
- **IRedisStreamService** - Stream processing interface

### [RedisKit.Services](RedisKit.Services.html)
Service implementations:
- **RedisCacheService** - High-performance caching with circuit breaker
- **PubSubService** - Advanced pub/sub with pattern matching
- **RedisStreamService** - Stream processing with consumer groups

### [RedisKit.Models](RedisKit.Models.html)
Configuration and data models:
- **RedisOptions** - Main configuration options
- **CircuitBreakerSettings** - Circuit breaker configuration
- **RetryConfiguration** - Retry policy settings
- **ConnectionHealthStatus** - Connection health monitoring

### [RedisKit.Serialization](RedisKit.Serialization.html)
Serialization providers:
- **IRedisSerializer** - Serialization interface
- **SystemTextJsonRedisSerializer** - JSON serialization
- **MessagePackRedisSerializer** - High-performance MessagePack serialization
- **RedisSerializerFactory** - Factory for creating serializers

### [RedisKit.Extensions](RedisKit.Extensions.html)
Extension methods:
- **ServiceCollectionExtensions** - DI container registration extensions

### [RedisKit.Exceptions](RedisKit.Exceptions.html)
Custom exceptions:
- **RedisCircuitOpenException** - Thrown when circuit breaker is open

## Quick Start

### Basic Setup
```csharp
using RedisKit.Extensions;

// Add RedisKit to DI container
services.AddRedisKit(options =>
{
    options.ConnectionString = "localhost:6379";
    options.SerializerType = SerializerType.MessagePack;
});
```

### Using Cache Service
```csharp
public class MyService
{
    private readonly IRedisCacheService _cache;
    
    public MyService(IRedisCacheService cache)
    {
        _cache = cache;
    }
    
    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory)
    {
        var cached = await _cache.GetAsync<T>(key);
        if (cached != null) return cached;
        
        var value = await factory();
        await _cache.SetAsync(key, value, TimeSpan.FromMinutes(5));
        return value;
    }
}
```

### Using Pub/Sub
```csharp
// Subscribe to messages
await _pubSub.SubscribeAsync<MyMessage>("channel", async (ch, msg) =>
{
    await ProcessMessage(msg);
});

// Publish messages
await _pubSub.PublishAsync("channel", new MyMessage { Text = "Hello" });
```

### Using Streams
```csharp
// Add to stream
var messageId = await _streams.AddAsync("mystream", new MyEvent());

// Consume from stream
await _streams.ConsumeAsync<MyEvent>("mystream", "group", "consumer",
    async (msg, entry) =>
    {
        await ProcessEvent(msg);
    });
```

## Key Features

### 🚀 Performance
- MessagePack serialization: 2-3x faster than JSON
- Connection pooling and multiplexing
- Async/await throughout

### 🛡️ Reliability  
- Circuit breaker pattern
- Automatic retry with exponential backoff
- Connection health monitoring

### 📊 Monitoring
- Detailed metrics and statistics
- Health check endpoints
- Subscription and stream monitoring

### 🔧 Flexibility
- Multiple serialization options
- Configurable timeouts and retries
- Pattern-based subscriptions

## Common Patterns

- [Cache-Aside Pattern](../articles/caching-patterns.html#cache-aside-pattern)
- [Write-Through Caching](../articles/caching-patterns.html#write-through-pattern)
- [Pub/Sub Messaging](../articles/pubsub-patterns.html)
- [Stream Processing](../articles/stream-processing.html)

## Additional Resources

- [Getting Started Guide](../articles/getting-started.html)
- [GitHub Repository](https://github.com/ersintarhan/RedisKit)
- [NuGet Package](https://www.nuget.org/packages/RedisKit)
- [Report Issues](https://github.com/ersintarhan/RedisKit/issues)