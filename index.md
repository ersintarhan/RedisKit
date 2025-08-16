# RedisKit Documentation

## Welcome to RedisKit

RedisKit is a production-ready, enterprise-grade Redis toolkit for .NET 9 with advanced caching, pub/sub, and streaming features.

## Quick Links

- 📚 **[Getting Started Guide](articles/getting-started.md)** - Learn how to use RedisKit
- 📖 **[API Documentation](api/RedisKit.html)** - Complete API reference with XML docs
- 💻 **[GitHub Repository](https://github.com/ersintarhan/RedisKit)** - Source code and issues
- 📦 **[NuGet Package](https://www.nuget.org/packages/RedisKit)** - Install via NuGet

## Features

- 🚀 **High-Performance Caching** with multiple serializers
- 📡 **Advanced Pub/Sub** with pattern matching
- 🌊 **Redis Streams** support
- 🛡️ **Enterprise Features** including Circuit Breaker
- ⚡ **Blazing Fast** - MessagePack 2-3x faster than JSON

## Installation

```bash
dotnet add package RedisKit
```

## Basic Usage

```csharp
using RedisKit.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRedisServices(options =>
{
    options.ConnectionString = "localhost:6379";
    options.Serializer = SerializerType.MessagePack;
});
```

## Performance

RedisKit uses MessagePack serialization by default, providing:
- **2.3x faster** serialization
- **5.6x less** memory usage
- **60% smaller** payload size

## Support

- [Report Issues](https://github.com/ersintarhan/RedisKit/issues)
- [Discussions](https://github.com/ersintarhan/RedisKit/discussions)
- [Wiki](https://github.com/ersintarhan/RedisKit/wiki)