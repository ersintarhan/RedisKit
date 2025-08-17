using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedisKit.Extensions;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using System.Collections.Concurrent;

namespace RedisKit.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class PubSubBenchmarks : IDisposable
{
    private ServiceProvider _serviceProvider = null!;
    private IRedisPubSubService _pubSubService = null!;
    private readonly TestMessage _testMessage;
    private readonly ConcurrentBag<TestMessage> _receivedMessages = new();
    private bool _disposed;

    public PubSubBenchmarks()
    {
        _testMessage = new TestMessage
        {
            Id = Guid.NewGuid(),
            Content = "Benchmark message content",
            Timestamp = DateTime.UtcNow,
            Tags = new[] { "benchmark", "test", "performance" }
        };
    }

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        services.AddRedisServices(options =>
        {
            options.ConnectionString = "localhost:6379"; // Will be mocked
            options.Serializer = SerializerType.MessagePack;
        });

        _serviceProvider = services.BuildServiceProvider();
        _pubSubService = _serviceProvider.GetRequiredService<IRedisPubSubService>();

        // Setup subscriber for receive benchmarks
        await _pubSubService.SubscribeAsync<TestMessage>(
            "benchmark:channel",
            async (message, ct) =>
            {
                _receivedMessages.Add(message);
                await Task.CompletedTask;
            });
    }

    [Benchmark(Baseline = true)]
    public async Task Publish_Single_Message()
    {
        await _pubSubService.PublishAsync("benchmark:single", _testMessage);
    }

    [Benchmark]
    public async Task Publish_To_Channel()
    {
        await _pubSubService.PublishAsync("benchmark:channel", _testMessage);
    }

    [Benchmark]
    public async Task Publish_Multiple_Channels()
    {
        var tasks = new Task[5];
        for (int i = 0; i < 5; i++)
        {
            var channel = $"benchmark:multi:{i}";
            tasks[i] = _pubSubService.PublishAsync(channel, _testMessage);
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task Subscribe_And_Unsubscribe()
    {
        var token = await _pubSubService.SubscribeAsync<TestMessage>(
            "benchmark:temp",
            async (msg, ct) => await Task.CompletedTask);

        await token.UnsubscribeAsync();
    }

    [Benchmark]
    public async Task Pattern_Subscribe()
    {
        var token = await _pubSubService.SubscribePatternAsync<TestMessage>(
            "benchmark:pattern:*",
            async (msg, ct) => await Task.CompletedTask);

        await token.UnsubscribeAsync();
    }

    [Benchmark]
    public async Task Get_Subscriber_Count()
    {
        await _pubSubService.GetSubscriberCountAsync("benchmark:channel");
    }

    [Benchmark]
    public async Task Publish_Batch_Sequential()
    {
        for (int i = 0; i < 10; i++)
        {
            await _pubSubService.PublishAsync($"benchmark:batch:{i}", _testMessage);
        }
    }

    [Benchmark]
    public async Task Publish_Batch_Parallel()
    {
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var channel = $"benchmark:batch:{i}";
            tasks[i] = _pubSubService.PublishAsync(channel, _testMessage);
        }
        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
            }
            _disposed = true;
        }
    }
}

[MessagePack.MessagePackObject]
public class TestMessage
{
    [MessagePack.Key(0)]
    public Guid Id { get; set; }

    [MessagePack.Key(1)]
    public string Content { get; set; } = string.Empty;

    [MessagePack.Key(2)]
    public DateTime Timestamp { get; set; }

    [MessagePack.Key(3)]
    public string[] Tags { get; set; } = Array.Empty<string>();

    [MessagePack.Key(4)]
    public Dictionary<string, object> Data { get; set; } = new();
}