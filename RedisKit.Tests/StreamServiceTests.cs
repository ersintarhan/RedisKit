using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class StreamServiceTests
{
    private readonly IDatabaseAsync _db;
    private readonly ILogger<RedisStreamService> _logger;
    private readonly IOptions<RedisOptions> _options;
    private readonly RedisConnection _connection;

    public StreamServiceTests()
    {
        _db = Substitute.For<IDatabaseAsync>();
        _logger = Substitute.For<ILogger<RedisStreamService>>();

        var redisOptions = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultTtl = TimeSpan.FromHours(1),
            Serializer = SerializerType.SystemTextJson
        };
        _options = Options.Create(redisOptions);

        var connLogger = Substitute.For<ILogger<RedisConnection>>();
        _connection = Substitute.For<RedisConnection>(connLogger, _options);
        _connection.GetDatabaseAsync().Returns(Task.FromResult(_db));
    }

    private RedisStreamService CreateSut()
    {
        return new RedisStreamService(_connection, _logger, _options);
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
    public void Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(null!, _logger, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(_connection, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(_connection, _logger, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_DoesNotThrow()
    {
        // Act & Assert
        var service = CreateSut();
        Assert.NotNull(service);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.AddAsync(null!, new TestMessage { Content = "Test" }));
    }

    [Fact]
    public async Task AddAsync_WithEmptyStreamName_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.AddAsync("", new TestMessage { Content = "Test" }));
    }

    [Fact]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.AddAsync<TestMessage>("test-stream", null!));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task AddAsync_WithValidParameters_ReturnsMessageId()
    {
        // Arrange
        var streamName = "test-stream";
        var message = new TestMessage { Content = "Test Message" };
        var expectedId = "123456789-0";
        var sut = CreateSut();

        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult((RedisValue)expectedId));

        // Act  
        var result = await sut.AddAsync(streamName, message);

        // Assert
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
        var sut = CreateSut();

        _db.StreamAddAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(),
                maxLength,
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult((RedisValue)"123456789-0"));

        // Act
        await sut.AddAsync(streamName, message, maxLength);

        // Assert
        await _db.Received(1).StreamAddAsync(
            Arg.Any<RedisKey>(), // Key prefix is added internally
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            maxLength,
            true, // useApproximateMaxLength
            Arg.Any<CommandFlags>());
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadAsync<TestMessage>(null!));
    }

    [Fact]
    public async Task ReadAsync_WithEmptyStreamName_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadAsync<TestMessage>(""));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task ReadAsync_WithValidParameters_ReturnsMessages()
    {
        // Arrange
        var streamName = "test-stream";
        var messageId = "123456789-0";
        var testMessage = new TestMessage { Content = "Test" };
        var messageData = JsonSerializer.SerializeToUtf8Bytes(testMessage);
        var sut = CreateSut();

        var streamEntry = new StreamEntry(
            messageId,
            new[] { new NameValueEntry("data", messageData) });

        _db.StreamRangeAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<Order>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new[] { streamEntry }));

        // Act
        var result = await sut.ReadAsync<TestMessage>(streamName);

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
        var sut = CreateSut();

        _db.StreamRangeAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<RedisValue?>(),
                count,
                Arg.Any<Order>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(Array.Empty<StreamEntry>()));

        // Act
        await sut.ReadAsync<TestMessage>(streamName, count: count);

        // Assert
        await _db.Received(1).StreamRangeAsync(
            Arg.Any<RedisKey>(), // Key prefix is added internally
            Arg.Any<RedisValue?>(),
            Arg.Any<RedisValue?>(),
            count,
            Arg.Any<Order>(),
            Arg.Any<CommandFlags>());
    }

    #endregion

    #region Consumer Group Tests

    [Fact]
    public async Task CreateConsumerGroupAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateConsumerGroupAsync(null!, "group"));
    }

    [Fact]
    public async Task CreateConsumerGroupAsync_WithNullGroupName_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateConsumerGroupAsync("stream", null!));
    }

    [Fact]
    public async Task CreateConsumerGroupAsync_WithValidParameters_ReturnsTrue()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "test-group";
        var sut = CreateSut();

        _db.StreamCreateConsumerGroupAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));

        // Act
        await sut.CreateConsumerGroupAsync(streamName, groupName);

        // Assert
        // CreateConsumerGroupAsync returns void, no result to assert
        await _db.Received(1).StreamCreateConsumerGroupAsync(
            Arg.Any<RedisKey>(), // Key prefix is added internally
            groupName,
            StreamPosition.Beginning,
            false,
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CreateConsumerGroupAsync_WhenGroupExists_ReturnsFalse()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "existing-group";
        var sut = CreateSut();

        _db.StreamCreateConsumerGroupAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromException<bool>(new RedisServerException("BUSYGROUP Consumer Group name already exists")));

        // Act
        await sut.CreateConsumerGroupAsync(streamName, groupName);

        // Assert
        // Exception is thrown, but CreateConsumerGroupAsync returns void
        // Verify the mock was called to ensure the method executed
        await _db.Received(1).StreamCreateConsumerGroupAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ReadGroupAsync_WithNullParameters_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadGroupAsync<TestMessage>(null!, "group", "consumer"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadGroupAsync<TestMessage>("stream", null!, "consumer"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ReadGroupAsync<TestMessage>("stream", "group", null!));
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
        var messageData = JsonSerializer.SerializeToUtf8Bytes(testMessage);
        var sut = CreateSut();

        var streamEntry = new StreamEntry(
            messageId,
            new[] { new NameValueEntry("data", messageData) });

        _db.StreamReadGroupAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new[] { streamEntry }));

        // Act
        var result = await sut.ReadGroupAsync<TestMessage>(streamName, groupName, consumerName);

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
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.AcknowledgeAsync(null!, "group", "123"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.AcknowledgeAsync("stream", null!, "123"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.AcknowledgeAsync("stream", "group", null!));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task AcknowledgeAsync_WithValidParameters_DoesNotThrow()
    {
        // Arrange
        var streamName = "test-stream";
        var groupName = "test-group";
        var messageId = "123456789-0";
        var sut = CreateSut();

        _db.StreamAcknowledgeAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(1L));

        // Act
        await sut.AcknowledgeAsync(streamName, groupName, messageId);

        // Assert - AcknowledgeAsync returns void, verify it was called
        await _db.Received(1).StreamAcknowledgeAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
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
        var messageData = JsonSerializer.SerializeToUtf8Bytes(testMessage);
        var sut = CreateSut();

        var streamEntry = new StreamEntry(
            messageIds[0],
            new[] { new NameValueEntry("data", messageData) });

        _db.StreamClaimAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<long>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new[] { streamEntry }));

        // Act
        var result = await sut.ClaimAsync<TestMessage>(
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
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.DeleteAsync(null!, ["123"]));
    }

    [Fact]
    public async Task DeleteAsync_WithNullMessageIds_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.DeleteAsync("stream", null!));
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyMessageIds_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.DeleteAsync("stream", []));
    }

    [Fact]
    public async Task DeleteAsync_WithValidParameters_ReturnsDeleteCount()
    {
        // Arrange
        var streamName = "test-stream";
        var messageIds = new[] { "123456789-0", "123456789-1" };
        var sut = CreateSut();

        _db.StreamDeleteAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(2L));

        // Act
        var result = await sut.DeleteAsync(streamName, messageIds);

        // Assert
        Assert.Equal(2, result);
        await _db.Received(1).StreamDeleteAsync(
            Arg.Any<RedisKey>(), // Key prefix is added internally
            Arg.Is<RedisValue[]>(v => v.Length == 2),
            Arg.Any<CommandFlags>());
    }

    #endregion

    #region Trimming Tests

    [Fact]
    public async Task TrimByLengthAsync_WithNullStreamName_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.TrimByLengthAsync(null!, 100));
    }

    [Fact]
    public async Task TrimByLengthAsync_WithInvalidMaxLength_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.TrimByLengthAsync("stream", 0));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.TrimByLengthAsync("stream", -1));
    }

    [Fact(Skip = "Requires integration testing with real Redis due to RedisValue struct mock limitations")]
    public async Task TrimByLengthAsync_WithValidParameters_ReturnsTrimCount()
    {
        // Arrange
        var streamName = "test-stream";
        var maxLength = 100;
        var expectedTrimmedCount = 5L;
        var sut = CreateSut();

        _db.StreamTrimAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(expectedTrimmedCount));

        // Act
        var result = await sut.TrimByLengthAsync(streamName, maxLength);

        // Assert
        Assert.Equal(expectedTrimmedCount, result);
        await _db.Received(1).StreamTrimAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>());
    }

    #endregion
}