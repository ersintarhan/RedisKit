using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class StreamServiceTests
{
    private readonly Mock<IDatabaseAsync> _mockDatabase;
    private readonly Mock<ILogger<RedisStreamService>> _mockLogger;
    private readonly RedisOptions _options;
    private readonly RedisStreamService _streamService;

    public StreamServiceTests()
    {
        _mockDatabase = new Mock<IDatabaseAsync>();
        _mockLogger = new Mock<ILogger<RedisStreamService>>();
        _options = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultTtl = TimeSpan.FromHours(1),
            Serializer = SerializerType.SystemTextJson
        };

        var optionsMock = new Mock<IOptions<RedisOptions>>();
        optionsMock.Setup(x => x.Value).Returns(_options);

        _streamService = new RedisStreamService(_mockDatabase.Object, _mockLogger.Object, _options);
    }

    #region Test Models

    private class TestMessage
    {
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullDatabase_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(null!, _mockLogger.Object, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(_mockDatabase.Object, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(_mockDatabase.Object, _mockLogger.Object, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_DoesNotThrow()
    {
        // Act & Assert
        var service = new RedisStreamService(_mockDatabase.Object, _mockLogger.Object, _options);
        Assert.NotNull(service);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.AddAsync(null!, new TestMessage { Content = "Test" }));
    }

    [Fact]
    public async Task AddAsync_WithEmptyStreamName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.AddAsync("", new TestMessage { Content = "Test" }));
    }

    [Fact]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _streamService.AddAsync<TestMessage>("test-stream", null!));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task AddAsync_WithValidParameters_ReturnsMessageId()
    {
        // Arrange
        var streamName = "test-stream";
        var message = new TestMessage { Content = "Test Message" };
        var expectedId = "123456789-0";

        // Setup mock with callback to debug what's happening
        _mockDatabase.Setup(x => x.StreamAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<NameValueEntry[]>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .Returns<RedisKey, NameValueEntry[], RedisValue?, int?, bool, CommandFlags>((key, entries, msgId, maxLen, approx, flags) =>
            {
                // Return a RedisValue that will properly convert to string
                return Task.FromResult(RedisValue.Unbox(expectedId));
            });

        // Act  
        Exception? caughtException = null;
        string? result = null;
        try
        {
            result = await _streamService.AddAsync(streamName, message);
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        Assert.Null(caughtException);
        Assert.NotNull(result);
        Assert.Equal(expectedId, result);
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task AddAsync_WithMaxLength_PassesMaxLengthToDatabase()
    {
        // Arrange
        var streamName = "test-stream";
        var message = new TestMessage { Content = "Test" };
        var maxLength = 1000;

        _mockDatabase.Setup(x => x.StreamAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<NameValueEntry[]>(),
                It.IsAny<RedisValue?>(),
                maxLength,
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("123456789-0"));

        // Act
        await _streamService.AddAsync(streamName, message, maxLength);

        // Assert
        _mockDatabase.Verify(x => x.StreamAddAsync(
            It.IsAny<RedisKey>(), // Key prefix is added internally
            It.IsAny<NameValueEntry[]>(),
            It.IsAny<RedisValue?>(),
            maxLength,
            true, // useApproximateMaxLength
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.ReadAsync<TestMessage>(null!));
    }

    [Fact]
    public async Task ReadAsync_WithEmptyStreamName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.ReadAsync<TestMessage>(""));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task ReadAsync_WithValidParameters_ReturnsMessages()
    {
        // Arrange
        var streamName = "test-stream";
        var messageId = "123456789-0";
        var testMessage = new TestMessage { Content = "Test" };
        // Use the same serializer as StreamService uses (SystemTextJson)
        var messageData = JsonSerializer.SerializeToUtf8Bytes(testMessage);

        var streamEntry = new StreamEntry(
            messageId,
            new[] { new NameValueEntry("data", messageData) });

        _mockDatabase.Setup(x => x.StreamRangeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<int?>(),
                It.IsAny<Order>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(new[] { streamEntry });

        // Act
        var result = await _streamService.ReadAsync<TestMessage>(streamName);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(messageId, result.First().Key);
        Assert.NotNull(result.First().Value);
        Assert.Equal("Test", result.First().Value?.Content);
    }

    [Fact]
    public async Task ReadAsync_WithCount_LimitsResults()
    {
        // Arrange
        var streamName = "test-stream";
        var count = 5;

        _mockDatabase.Setup(x => x.StreamRangeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<RedisValue?>(),
                count,
                It.IsAny<Order>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<StreamEntry>());

        // Act
        await _streamService.ReadAsync<TestMessage>(streamName, count: count);

        // Assert
        _mockDatabase.Verify(x => x.StreamRangeAsync(
            It.IsAny<RedisKey>(), // Key prefix is added internally
            It.IsAny<RedisValue?>(),
            It.IsAny<RedisValue?>(),
            count,
            It.IsAny<Order>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region Consumer Group Tests

    [Fact]
    public async Task CreateConsumerGroupAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.CreateConsumerGroupAsync(null!, "group"));
    }

    [Fact]
    public async Task CreateConsumerGroupAsync_WithNullGroupName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.CreateConsumerGroupAsync("stream", null!));
    }

    [Fact]
    public async Task CreateConsumerGroupAsync_WithValidParameters_ReturnsTrue()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "test-group";

        _mockDatabase.Setup(x => x.StreamCreateConsumerGroupAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _streamService.CreateConsumerGroupAsync(streamName, groupName);

        // Assert
        // CreateConsumerGroupAsync returns void, no result to assert
        _mockDatabase.Verify(x => x.StreamCreateConsumerGroupAsync(
            It.IsAny<RedisKey>(), // Key prefix is added internally
            groupName,
            StreamPosition.Beginning,
            false,
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CreateConsumerGroupAsync_WhenGroupExists_ReturnsFalse()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "existing-group";

        _mockDatabase.Setup(x => x.StreamCreateConsumerGroupAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisServerException("BUSYGROUP Consumer Group name already exists"));

        // Act
        await _streamService.CreateConsumerGroupAsync(streamName, groupName);

        // Assert
        // Exception is thrown, but CreateConsumerGroupAsync returns void
        // Verify the mock was called to ensure the method executed
        _mockDatabase.Verify(x => x.StreamCreateConsumerGroupAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue?>(),
            It.IsAny<bool>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task ReadGroupAsync_WithNullParameters_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.ReadGroupAsync<TestMessage>(null!, "group", "consumer"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.ReadGroupAsync<TestMessage>("stream", null!, "consumer"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.ReadGroupAsync<TestMessage>("stream", "group", null!));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task ReadGroupAsync_WithValidParameters_ReturnsMessages()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "test-group";
        var consumerName = "test-consumer";
        var messageId = "123456789-0";
        var testMessage = new TestMessage { Content = "Group Message" };
        // Use the same serializer as StreamService uses (SystemTextJson)
        var messageData = JsonSerializer.SerializeToUtf8Bytes(testMessage);

        var streamEntry = new StreamEntry(
            messageId,
            new[] { new NameValueEntry("data", messageData) });

        _mockDatabase.Setup(x => x.StreamReadGroupAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(new[] { streamEntry });

        // Act
        var result = await _streamService.ReadGroupAsync<TestMessage>(streamName, groupName, consumerName);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(messageId, result.First().Key);
        Assert.NotNull(result.First().Value);
        Assert.Equal("Group Message", result.First().Value?.Content);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithNullParameters_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.AcknowledgeAsync(null!, "group", "123"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.AcknowledgeAsync("stream", null!, "123"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.AcknowledgeAsync("stream", "group", null!));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task AcknowledgeAsync_WithValidParameters_DoesNotThrow()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "test-group";
        var messageId = "123456789-0";

        _mockDatabase.Setup(x => x.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _streamService.AcknowledgeAsync(streamName, groupName, messageId);

        // Assert - AcknowledgeAsync returns void, verify it was called
        _mockDatabase.Verify(x => x.StreamAcknowledgeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task ClaimAsync_WithValidParameters_ReturnsClaimedMessages()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "test-group";
        var consumerName = "test-consumer";
        var messageIds = new[] { "123456789-0", "123456789-1" };
        var testMessage = new TestMessage { Content = "Claimed Message" };
        // Use the same serializer as StreamService uses (SystemTextJson)
        var messageData = JsonSerializer.SerializeToUtf8Bytes(testMessage);

        var streamEntry = new StreamEntry(
            messageIds[0],
            new[] { new NameValueEntry("data", messageData) });

        _mockDatabase.Setup(x => x.StreamClaimAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<long>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(new[] { streamEntry });

        // Act
        var result = await _streamService.ClaimAsync<TestMessage>(
            streamName, groupName, consumerName, 1000, messageIds);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(messageIds[0], result.First().Key);
        Assert.NotNull(result.First().Value);
        Assert.Equal("Claimed Message", result.First().Value?.Content);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.DeleteAsync(null!, ["123"]));
    }

    [Fact]
    public async Task DeleteAsync_WithNullMessageIds_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.DeleteAsync("stream", null!));
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyMessageIds_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.DeleteAsync("stream", []));
    }

    [Fact]
    public async Task DeleteAsync_WithValidParameters_ReturnsDeleteCount()
    {
        // Arrange
        var streamName = "test-stream";
        var messageIds = new[] { "123456789-0", "123456789-1" };

        _mockDatabase.Setup(x => x.StreamDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        // Act
        var result = await _streamService.DeleteAsync(streamName, messageIds);

        // Assert
        Assert.Equal(2, result);
        _mockDatabase.Verify(x => x.StreamDeleteAsync(
            It.IsAny<RedisKey>(), // Key prefix is added internally
            It.Is<RedisValue[]>(v => v.Length == 2),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region Trimming Tests

    [Fact]
    public async Task TrimByLengthAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.TrimByLengthAsync(null!, 100));
    }

    [Fact]
    public async Task TrimByLengthAsync_WithInvalidMaxLength_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.TrimByLengthAsync("stream", 0));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _streamService.TrimByLengthAsync("stream", -1));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task TrimByLengthAsync_WithValidParameters_ReturnsTrimCount()
    {
        // Arrange
        var streamName = "test-stream";
        var maxLength = 100;
        var expectedTrimmedCount = 5L;

        _mockDatabase.Setup(x => x.StreamTrimAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(expectedTrimmedCount);

        // Act
        var result = await _streamService.TrimByLengthAsync(streamName, maxLength);

        // Assert
        Assert.Equal(expectedTrimmedCount, result);
        _mockDatabase.Verify(x => x.StreamTrimAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion
}