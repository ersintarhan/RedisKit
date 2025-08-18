using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class PubSubServiceTests
{
    private readonly ILogger<RedisPubSubService> _logger;
    private readonly IOptions<RedisOptions> _options;
    private readonly ISubscriber _subscriber;
    private readonly IRedisConnection _connection;

    public PubSubServiceTests()
    {
        _subscriber = Substitute.For<ISubscriber>();
        _logger = Substitute.For<ILogger<RedisPubSubService>>();

        var redisOptions = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultTtl = TimeSpan.FromHours(1),
            Serializer = SerializerType.SystemTextJson
        };
        _options = Options.Create(redisOptions);

        _connection = Substitute.For<IRedisConnection>();
        _connection.GetSubscriberAsync().Returns(_subscriber);
    }

    private RedisPubSubService CreateSut()
    {
        return new RedisPubSubService(_connection, _logger, _options);
    }

    #region Dispose Tests

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        // Arrange
        var service = CreateSut();

        // Act & Assert
        await service.DisposeAsync(); // Should not throw
    }

    #endregion

    #region Test Models

    public class TestMessage
    {
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_DoesNotThrow()
    {
        // Arrange & Act
        var sut = CreateSut();

        // Assert
        Assert.NotNull(sut);
    }

    [Fact]
    public void Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisPubSubService(null!, _logger, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisPubSubService(_connection, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisPubSubService(_connection, _logger, null!));
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WithNullChannel_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.PublishAsync(null!, new TestMessage()));
    }

    [Fact]
    public async Task PublishAsync_WithEmptyChannel_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.PublishAsync("", new TestMessage()));
    }

    [Fact]
    public async Task PublishAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.PublishAsync<TestMessage>("test-channel", null!));
    }

    [Fact]
    public async Task PublishAsync_WithValidParameters_PublishesMessage()
    {
        // Arrange
        var channel = "test-channel";
        var message = new TestMessage { Content = "Test Content" };
        var sut = CreateSut();

        _subscriber.PublishAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<RedisValue>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(1L));

        // Act
        await sut.PublishAsync(channel, message);

        // Assert
        await _subscriber.Received(1).PublishAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == channel),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>());
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WithNullChannel_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SubscribeAsync<TestMessage>(null!, (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task SubscribeAsync_WithEmptyChannel_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SubscribeAsync<TestMessage>("", (msg, ct) => Task.CompletedTask));
    }

    [Fact]
    public async Task SubscribeAsync_WithNullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.SubscribeAsync<TestMessage>("test-channel", null!));
    }

    [Fact]
    public async Task SubscribeAsync_WithValidParameters_SubscribesToChannel()
    {
        // Arrange
        var channel = "test-channel";
        Func<TestMessage, CancellationToken, Task> handler = (msg, ct) => { return Task.CompletedTask; };
        var sut = CreateSut();

        _subscriber.SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        // Act
        await sut.SubscribeAsync(channel, handler);

        // Assert
        await _subscriber.Received(1).SubscribeAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == channel),
            Arg.Any<Action<RedisChannel, RedisValue>>(),
            Arg.Any<CommandFlags>());
    }

    #endregion

    #region UnsubscribeAsync Tests

    [Fact]
    public async Task UnsubscribeAsync_WithNullChannel_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.UnsubscribeAsync((string)null!));
    }

    [Fact]
    public async Task UnsubscribeAsync_WithEmptyChannel_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.UnsubscribeAsync(""));
    }

    [Fact]
    public async Task UnsubscribeAsync_WithValidChannel_UnsubscribesFromChannel()
    {
        // Arrange
        var channel = "test-channel";
        var sut = CreateSut();

        // First subscribe to create a handler
        _subscriber.SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        _subscriber.UnsubscribeAsync(
                Arg.Any<RedisChannel>(),
                null,
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        // Act - Subscribe first
        await sut.SubscribeAsync<TestMessage>(channel, Handler);

        // Then unsubscribe
        await sut.UnsubscribeAsync(channel);

        // Assert
        await _subscriber.Received(1).UnsubscribeAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == channel),
            null,
            Arg.Any<CommandFlags>());
    }

    private async Task Handler(TestMessage msg, CancellationToken ct)
    {
        _ = true;
        await Task.CompletedTask;
    }

    #endregion

    #region Pattern Subscription Tests

    [Fact]
    public async Task SubscribePatternAsync_WithNullPattern_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SubscribePatternAsync<TestMessage>(null!, (msg, ct) => Task.CompletedTask));
    }

    [Fact]
    public async Task SubscribePatternAsync_WithEmptyPattern_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SubscribePatternAsync<TestMessage>("", (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task SubscribePatternAsync_WithNullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.SubscribePatternAsync<TestMessage>("test-*", null!));
    }

    [Fact]
    public async Task SubscribePatternAsync_WithValidParameters_SubscribesToPattern()
    {
        // Arrange
        var pattern = "test-*";
        Func<TestMessage, CancellationToken, Task> handler = (msg, ct) => Task.CompletedTask;
        var sut = CreateSut();

        _subscriber.SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        // Act
        await sut.SubscribePatternAsync(pattern, handler);

        // Assert
        await _subscriber.Received(1).SubscribeAsync(
            Arg.Is<RedisChannel>(c => c.IsPattern && c.ToString() == pattern),
            Arg.Any<Action<RedisChannel, RedisValue>>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task UnsubscribePatternAsync_WithNullPattern_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.UnsubscribePatternAsync(null!));
    }

    [Fact]
    public async Task UnsubscribePatternAsync_WithEmptyPattern_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.UnsubscribePatternAsync(""));
    }

    [Fact]
    public async Task UnsubscribePatternAsync_WithValidPattern_UnsubscribesFromPattern()
    {
        // Arrange
        var pattern = "test-*";
        var sut = CreateSut();

        // First subscribe to create a handler
        _subscriber.SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        _subscriber.UnsubscribeAsync(
                Arg.Any<RedisChannel>(),
                null,
                Arg.Any<CommandFlags>())
            .Returns(Task.CompletedTask);

        // Act - Subscribe first
        var token = await sut.SubscribePatternAsync<TestMessage>(pattern, async (msg, ct) => { await Task.CompletedTask; });

        // Then unsubscribe
        await sut.UnsubscribePatternAsync(pattern);

        // Assert
        await _subscriber.Received(1).UnsubscribeAsync(
            Arg.Is<RedisChannel>(c => c.IsPattern && c.ToString() == pattern),
            null,
            Arg.Any<CommandFlags>());
    }

    #endregion
}