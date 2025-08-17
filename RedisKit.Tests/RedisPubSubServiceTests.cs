using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using RedisKit.Models;
using RedisKit.Serialization;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests
{
    public class RedisPubSubServiceTests : IDisposable
    {
        private readonly Mock<ISubscriber> _mockSubscriber;
        private readonly Mock<ILogger<RedisPubSubService>> _mockLogger;
        private readonly Mock<IRedisSerializer> _mockSerializer;
        private readonly RedisPubSubService _pubSubService;
        private readonly RedisOptions _options;
        private readonly CancellationTokenSource _cts;

        public RedisPubSubServiceTests()
        {
            _mockSubscriber = new Mock<ISubscriber>();
            _mockLogger = new Mock<ILogger<RedisPubSubService>>();
            _mockSerializer = new Mock<IRedisSerializer>();
            _cts = new CancellationTokenSource();

            _options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                Serializer = SerializerType.SystemTextJson
            };

            // Setup mock serializer
            _mockSerializer.Setup(x => x.Name).Returns("TestSerializer");

            // Mock the factory to return our mock serializer
            var originalFactory = RedisSerializerFactory.Create;
            RedisSerializerFactory.RegisterCustomSerializer(_mockSerializer.Object);

            _pubSubService = new RedisPubSubService(
                _mockSubscriber.Object,
                _mockLogger.Object,
                _options);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullSubscriber_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RedisPubSubService(null!, _mockLogger.Object, _options));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RedisPubSubService(_mockSubscriber.Object, null!, _options));
        }

        [Fact]
        public void Constructor_WithNullOptions_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RedisPubSubService(_mockSubscriber.Object, _mockLogger.Object, null!));
        }

        #endregion

        #region Publish Tests

        [Fact]
        public async Task PublishAsync_WithValidMessage_ReturnsSubscriberCount()
        {
            // Arrange
            var channel = "test:channel";
            var message = new TestMessage { Id = 1, Content = "Test" };
            var serializedData = new byte[] { 1, 2, 3, 4 };
            var expectedCount = 3L;

            _mockSerializer.Setup(x => x.SerializeAsync(message, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serializedData);
            _mockSubscriber.Setup(x => x.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(expectedCount);

            // Act
            var result = await _pubSubService.PublishAsync(channel, message, _cts.Token);

            // Assert
            Assert.Equal(expectedCount, result);
            _mockSubscriber.Verify(x => x.PublishAsync(
                It.Is<RedisChannel>(c => c.ToString() == channel),
                serializedData,
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task PublishAsync_WithNullChannel_ThrowsArgumentException()
        {
            // Arrange
            var message = new TestMessage { Id = 1, Content = "Test" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _pubSubService.PublishAsync<TestMessage>(null!, message, _cts.Token));
        }

        [Fact]
        public async Task PublishAsync_WithEmptyChannel_ThrowsArgumentException()
        {
            // Arrange
            var message = new TestMessage { Id = 1, Content = "Test" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _pubSubService.PublishAsync("", message, _cts.Token));
        }

        [Fact]
        public async Task PublishAsync_WithNullMessage_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _pubSubService.PublishAsync<TestMessage>("channel", null!, _cts.Token));
        }

        #endregion

        #region Subscribe Tests

        [Fact]
        public async Task SubscribeAsync_WithValidHandler_ReturnsToken()
        {
            // Arrange
            var channel = "test:channel";
            var messageReceived = false;
            Func<TestMessage, CancellationToken, Task> handler = async (msg, ct) =>
            {
                messageReceived = true;
                await Task.CompletedTask;
            };

            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            var token = await _pubSubService.SubscribeAsync(channel, handler, _cts.Token);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(channel, token.ChannelOrPattern);
            Assert.Equal(SubscriptionType.Channel, token.Type);
        }

        [Fact]
        public async Task SubscribeAsync_WithNullChannel_ThrowsArgumentException()
        {
            // Arrange
            Func<TestMessage, CancellationToken, Task> handler = async (msg, ct) => await Task.CompletedTask;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _pubSubService.SubscribeAsync<TestMessage>(null!, handler, _cts.Token));
        }

        [Fact]
        public async Task SubscribeAsync_WithNullHandler_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _pubSubService.SubscribeAsync<TestMessage>("channel", null!, _cts.Token));
        }

        [Fact]
        public async Task SubscribeWithMetadataAsync_ReceivesChannelInfo()
        {
            // Arrange
            var channel = "test:channel";
            string? receivedChannel = null;
            TestMessage? receivedMessage = null;
            
            Func<TestMessage, string, CancellationToken, Task> handler = async (msg, ch, ct) =>
            {
                receivedMessage = msg;
                receivedChannel = ch;
                await Task.CompletedTask;
            };

            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            var token = await _pubSubService.SubscribeWithMetadataAsync(channel, handler, _cts.Token);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(channel, token.ChannelOrPattern);
        }

        #endregion

        #region Pattern Subscribe Tests

        [Fact]
        public async Task SubscribePatternAsync_WithValidPattern_ReturnsToken()
        {
            // Arrange
            var pattern = "test:*";
            Func<TestMessage, CancellationToken, Task> handler = async (msg, ct) => await Task.CompletedTask;

            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            var token = await _pubSubService.SubscribePatternAsync(pattern, handler, _cts.Token);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(pattern, token.ChannelOrPattern);
            Assert.Equal(SubscriptionType.Pattern, token.Type);
        }

        [Fact]
        public async Task SubscribePatternAsync_WithInvalidPattern_ThrowsArgumentException()
        {
            // Arrange
            var invalidPattern = "[unclosed";
            Func<TestMessage, CancellationToken, Task> handler = async (msg, ct) => await Task.CompletedTask;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _pubSubService.SubscribePatternAsync(invalidPattern, handler, _cts.Token));
        }

        [Fact]
        public async Task SubscribePatternWithMetadataAsync_ReceivesChannelInfo()
        {
            // Arrange
            var pattern = "test:*";
            string? receivedChannel = null;
            
            Func<TestMessage, string, CancellationToken, Task> handler = async (msg, ch, ct) =>
            {
                receivedChannel = ch;
                await Task.CompletedTask;
            };

            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            var token = await _pubSubService.SubscribePatternWithMetadataAsync(pattern, handler, _cts.Token);

            // Assert
            Assert.NotNull(token);
            Assert.Equal(pattern, token.ChannelOrPattern);
            Assert.Equal(SubscriptionType.Pattern, token.Type);
        }

        #endregion

        #region Unsubscribe Tests

        [Fact]
        public async Task UnsubscribeAsync_WithChannel_UnsubscribesSuccessfully()
        {
            // Arrange
            var channel = "test:channel";
            
            // First subscribe
            Func<TestMessage, CancellationToken, Task> handler = async (msg, ct) => await Task.CompletedTask;
            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);
            
            var token = await _pubSubService.SubscribeAsync(channel, handler, _cts.Token);

            _mockSubscriber.Setup(x => x.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            await _pubSubService.UnsubscribeAsync(channel, _cts.Token);

            // Assert
            _mockSubscriber.Verify(x => x.UnsubscribeAsync(
                It.Is<RedisChannel>(c => c.ToString() == channel),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task UnsubscribeAsync_WithToken_UnsubscribesSuccessfully()
        {
            // Arrange
            var channel = "test:channel";
            Func<TestMessage, CancellationToken, Task> handler = async (msg, ct) => await Task.CompletedTask;
            
            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);
            
            var token = await _pubSubService.SubscribeAsync(channel, handler, _cts.Token);

            _mockSubscriber.Setup(x => x.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            await _pubSubService.UnsubscribeAsync(token, _cts.Token);

            // Assert
            _mockSubscriber.Verify(x => x.UnsubscribeAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task UnsubscribeAsync_WithNullToken_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _pubSubService.UnsubscribeAsync((SubscriptionToken)null!, _cts.Token));
        }

        [Fact]
        public async Task UnsubscribePatternAsync_WithPattern_UnsubscribesSuccessfully()
        {
            // Arrange
            var pattern = "test:*";
            
            _mockSubscriber.Setup(x => x.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            await _pubSubService.UnsubscribePatternAsync(pattern, _cts.Token);

            // Assert
            _mockSubscriber.Verify(x => x.UnsubscribeAsync(
                It.Is<RedisChannel>(c => c.IsPattern && c.ToString() == pattern),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task UnsubscribeAllAsync_UnsubscribesAllChannelsAndPatterns()
        {
            // Arrange
            var channel = "test:channel";
            var pattern = "test:*";
            
            // Subscribe to both channel and pattern
            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);
            
            await _pubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask, _cts.Token);
            await _pubSubService.SubscribePatternAsync<TestMessage>(pattern, async (msg, ct) => await Task.CompletedTask, _cts.Token);

            _mockSubscriber.Setup(x => x.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            await _pubSubService.UnsubscribeAllAsync(_cts.Token);

            // Assert
            _mockSubscriber.Verify(x => x.UnsubscribeAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()), Times.AtLeastOnce);
        }

        #endregion

        #region Statistics Tests

        [Fact]
        public async Task GetStatistics_ReturnsEmptyWhenNoSubscriptions()
        {
            // Act
            var stats = _pubSubService.GetStatistics();

            // Assert
            Assert.NotNull(stats);
            Assert.Empty(stats);
        }

        [Fact]
        public async Task GetStatistics_WithChannel_ReturnsStats()
        {
            // Arrange
            var channel = "test:channel";
            
            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);
            
            await _pubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask, _cts.Token);

            // Act
            var stats = _pubSubService.GetStatistics(channel);

            // Assert
            Assert.NotNull(stats);
            Assert.Equal(channel, stats.ChannelOrPattern);
            Assert.Equal(SubscriptionType.Channel, stats.Type);
        }

        [Fact]
        public async Task GetSubscriptionStatsAsync_ReturnsAllStats()
        {
            // Arrange
            var channel = "test:channel";
            
            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);
            
            await _pubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask, _cts.Token);

            // Act
            var stats = await _pubSubService.GetSubscriptionStatsAsync(_cts.Token);

            // Assert
            Assert.NotNull(stats);
            Assert.NotEmpty(stats);
            Assert.Contains(stats, s => s.ChannelOrPattern == channel);
        }

        #endregion

        #region HasSubscribers Tests

        [Fact]
        public async Task HasSubscribersAsync_WithSubscribers_ReturnsTrue()
        {
            // Arrange
            var channel = "test:channel";
            
            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);
            
            await _pubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask, _cts.Token);

            // Act
            var hasSubscribers = await _pubSubService.HasSubscribersAsync(channel, _cts.Token);

            // Assert
            Assert.True(hasSubscribers);
        }

        [Fact]
        public async Task HasSubscribersAsync_WithNoSubscribers_ReturnsFalse()
        {
            // Arrange
            var channel = "test:channel";

            // Act
            var hasSubscribers = await _pubSubService.HasSubscribersAsync(channel, _cts.Token);

            // Assert
            Assert.False(hasSubscribers);
        }

        [Fact]
        public async Task GetSubscriberCountAsync_ReturnsCorrectCount()
        {
            // Arrange
            var channel = "test:channel";
            
            _mockSubscriber.Setup(x => x.SubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);
            
            // Subscribe multiple handlers
            await _pubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask, _cts.Token);
            await _pubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask, _cts.Token);

            // Act
            var count = await _pubSubService.GetSubscriberCountAsync(channel, _cts.Token);

            // Assert
            Assert.Equal(2, count);
        }

        #endregion

        #region Disposal Tests

        [Fact]
        public void Dispose_UnsubscribesAll()
        {
            // Arrange
            _mockSubscriber.Setup(x => x.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), 
                It.IsAny<Action<RedisChannel, RedisValue>>(), 
                It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            // Act
            _pubSubService.Dispose();

            // Assert
            // Verify cleanup was attempted
            Assert.Throws<ObjectDisposedException>(() => 
                _pubSubService.PublishAsync("channel", new TestMessage(), CancellationToken.None).GetAwaiter().GetResult());
        }

        [Fact]
        public async Task Operations_AfterDispose_ThrowObjectDisposedException()
        {
            // Arrange
            _pubSubService.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _pubSubService.PublishAsync("channel", new TestMessage(), _cts.Token));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _pubSubService.SubscribeAsync<TestMessage>("channel", async (msg, ct) => await Task.CompletedTask, _cts.Token));
        }

        #endregion

        public void Dispose()
        {
            _cts?.Dispose();
            _pubSubService?.Dispose();
            RedisSerializerFactory.ClearCache();
        }

        private class TestMessage
        {
            public int Id { get; set; }
            public string Content { get; set; } = string.Empty;
        }
    }
}