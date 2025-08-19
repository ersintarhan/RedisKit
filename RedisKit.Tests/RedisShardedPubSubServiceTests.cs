using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class RedisShardedPubSubServiceTests
{
    private readonly IRedisConnection _connection = Substitute.For<IRedisConnection>();
    private readonly ILogger<RedisShardedPubSubService> _logger = Substitute.For<ILogger<RedisShardedPubSubService>>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IOptions<RedisOptions> _options = Options.Create(new RedisOptions());
    private readonly RedisShardedPubSubService _service;
    private readonly ISubscriber _subscriber = Substitute.For<ISubscriber>();

    public RedisShardedPubSubServiceTests()
    {
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(_multiplexer));
        _connection.GetSubscriberAsync().Returns(Task.FromResult(_subscriber));
        _multiplexer.GetSubscriber(Arg.Any<object>()).Returns(_subscriber);

        _service = new RedisShardedPubSubService(_connection, _logger, _options);
    }

    [Fact]
    public async Task PublishAsync_Should_Use_Sharded_Channel()
    {
        // Arrange
        const string channel = "test-channel";
        var message = new TestMessage { Id = 1, Content = "Test" };
        _subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>()).Returns(Task.FromResult(5L));

        // Act
        var result = await _service.PublishAsync(channel, message);

        // Assert
        Assert.Equal(5, result);
        await _subscriber.Received(1).PublishAsync(
            Arg.Is<RedisChannel>(ch => ch.ToString() == channel),
            Arg.Any<RedisValue>()
        );
    }

    [Fact]
    public async Task SubscribeAsync_Should_Use_Sharded_Channel()
    {
        // Arrange
        const string channel = "test-channel";
        var handlerCalled = false;
        Func<ShardedChannelMessage<TestMessage>, CancellationToken, Task> handler = (msg, ct) =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        };

        // Act
        var token = await _service.SubscribeAsync(channel, handler);

        // Assert
        Assert.NotNull(token);
        Assert.Equal(channel, token.ChannelOrPattern);
        Assert.Equal(SubscriptionType.Channel, token.Type);

        await _subscriber.Received(1).SubscribeAsync(
            Arg.Is<RedisChannel>(ch => ch.ToString() == channel),
            Arg.Any<Func<RedisChannel, RedisValue, Task>>()
        );
    }

    [Fact]
    public async Task SubscribePatternAsync_Should_Throw_NotSupportedException()
    {
        // Arrange
        const string pattern = "test-*";
        Func<ShardedChannelMessage<TestMessage>, CancellationToken, Task> handler = (msg, ct) => Task.CompletedTask;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _service.SubscribePatternAsync(pattern, handler)
        );

        Assert.Contains("does not support pattern subscriptions", ex.Message);
    }

    [Fact]
    public async Task UnsubscribePatternAsync_Should_Throw_NotSupportedException()
    {
        // Arrange
        const string pattern = "test-*";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _service.UnsubscribePatternAsync(pattern)
        );

        Assert.Contains("does not support pattern subscriptions", ex.Message);
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Use_Sharded_Channel()
    {
        // Arrange
        const string channel = "test-channel";

        // Subscribe first
        var token = await _service.SubscribeAsync<TestMessage>(
            channel,
            (msg, ct) => Task.CompletedTask
        );

        // Act
        await _service.UnsubscribeAsync(channel);

        // Assert
        await _subscriber.Received(1).UnsubscribeAsync(
            Arg.Is<RedisChannel>(ch => ch.ToString() == channel)
        );
    }

    [Fact]
    public async Task IsSupportedAsync_Should_Check_Support_Using_Sharded_Publish()
    {
        // Arrange
        _subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>())
            .Returns(Task.FromResult(0L));

        // Act
        var isSupported = await _service.IsSupportedAsync();

        // Assert
        Assert.True(isSupported);
        await _subscriber.Received(1).PublishAsync(
            Arg.Is<RedisChannel>(ch => ch.ToString() == "test"),
            Arg.Is<RedisValue>(v => v.ToString() == "test")
        );
    }

    [Fact]
    public async Task IsSupportedAsync_Should_Return_False_When_Not_Supported()
    {
        // Arrange
        _subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>())
            .Returns<long>(x => throw new RedisServerException("unknown command"));

        // Act
        var isSupported = await _service.IsSupportedAsync();

        // Assert
        Assert.False(isSupported);
    }

    [Fact]
    public async Task Dispose_Should_Unsubscribe_All_Channels()
    {
        // Arrange
        await _service.SubscribeAsync<TestMessage>("channel1", (msg, ct) => Task.CompletedTask);
        await _service.SubscribeAsync<TestMessage>("channel2", (msg, ct) => Task.CompletedTask);

        // Act
        _service.Dispose();

        // Assert
        await _subscriber.Received().UnsubscribeAsync(
            Arg.Is<RedisChannel>(ch => ch.ToString() == "channel1")
        );
        await _subscriber.Received().UnsubscribeAsync(
            Arg.Is<RedisChannel>(ch => ch.ToString() == "channel2")
        );
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Content { get; set; } = "";
    }
}