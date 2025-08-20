using System;
using RedisKit.Helpers;
using Xunit;

namespace RedisKit.Tests.Helpers;

public class StreamValidationHelperTests
{
    [Fact]
    public void ValidateStreamName_WithValidName_DoesNotThrow()
    {
        // Arrange
        var streamName = "valid-stream";

        // Act & Assert
        var exception = Record.Exception(() => StreamValidationHelper.ValidateStreamName(streamName));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateStreamName_WithInvalidName_ThrowsArgumentException(string? streamName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateStreamName(streamName!));
        Assert.Contains("Stream name cannot be null, empty, or whitespace", exception.Message);
    }

    [Fact]
    public void ValidateStreamName_WithCustomParamName_UsesItInException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateStreamName("", "customParam"));
        Assert.Equal("customParam", exception.ParamName);
    }

    [Fact]
    public void ValidateGroupName_WithValidName_DoesNotThrow()
    {
        // Arrange
        var groupName = "consumer-group";

        // Act & Assert
        var exception = Record.Exception(() => StreamValidationHelper.ValidateGroupName(groupName));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateGroupName_WithInvalidName_ThrowsArgumentException(string? groupName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateGroupName(groupName!));
        Assert.Contains("Group name cannot be null, empty, or whitespace", exception.Message);
    }

    [Fact]
    public void ValidateConsumerName_WithValidName_DoesNotThrow()
    {
        // Arrange
        var consumerName = "worker-1";

        // Act & Assert
        var exception = Record.Exception(() => StreamValidationHelper.ValidateConsumerName(consumerName));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateConsumerName_WithInvalidName_ThrowsArgumentException(string? consumerName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateConsumerName(consumerName!));
        Assert.Contains("Consumer name cannot be null, empty, or whitespace", exception.Message);
    }

    [Fact]
    public void ValidateBatchParameters_WithValidParameters_DoesNotThrow()
    {
        // Arrange
        var stream = "test-stream";
        var messages = new[] { "msg1", "msg2", "msg3" };

        // Act & Assert
        var exception = Record.Exception(() => 
            StreamValidationHelper.ValidateBatchParameters(stream, messages));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateBatchParameters_WithInvalidStream_ThrowsArgumentException(string? stream)
    {
        // Arrange
        var messages = new[] { "msg1" };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateBatchParameters(stream!, messages));
        Assert.Contains("Stream name cannot be null, empty, or whitespace", exception.Message);
    }

    [Fact]
    public void ValidateBatchParameters_WithNullMessages_ThrowsArgumentNullException()
    {
        // Arrange
        var stream = "test-stream";
        string[]? messages = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            StreamValidationHelper.ValidateBatchParameters(stream, messages!));
        Assert.Equal("messages", exception.ParamName);
    }

    [Fact]
    public void ValidateBatchParameters_WithEmptyMessages_ThrowsArgumentException()
    {
        // Arrange
        var stream = "test-stream";
        var messages = Array.Empty<string>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateBatchParameters(stream, messages));
        Assert.Contains("Messages array cannot be empty", exception.Message);
    }

    [Fact]
    public void ValidateRetryParameters_WithValidParameters_DoesNotThrow()
    {
        // Arrange
        var stream = "test-stream";
        var groupName = "group1";
        var consumerName = "consumer1";
        Func<string, Task<bool>> processor = _ => Task.FromResult(true);

        // Act & Assert
        var exception = Record.Exception(() => 
            StreamValidationHelper.ValidateRetryParameters(stream, groupName, consumerName, processor));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateRetryParameters_WithNullProcessor_ThrowsArgumentNullException()
    {
        // Arrange
        var stream = "test-stream";
        var groupName = "group1";
        var consumerName = "consumer1";
        Func<string, Task<bool>>? processor = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            StreamValidationHelper.ValidateRetryParameters(stream, groupName, consumerName, processor!));
        Assert.Equal("processor", exception.ParamName);
    }

    [Fact]
    public void ValidateMessageIds_WithValidIds_DoesNotThrow()
    {
        // Arrange
        var messageIds = new[] { "123-0", "124-0", "125-0" };

        // Act & Assert
        var exception = Record.Exception(() => StreamValidationHelper.ValidateMessageIds(messageIds));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateMessageIds_WithNullArray_ThrowsArgumentNullException()
    {
        // Arrange
        string[]? messageIds = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            StreamValidationHelper.ValidateMessageIds(messageIds!));
        Assert.Equal("messageIds", exception.ParamName);
    }

    [Fact]
    public void ValidateMessageIds_WithEmptyArray_ThrowsArgumentException()
    {
        // Arrange
        var messageIds = Array.Empty<string>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateMessageIds(messageIds));
        Assert.Contains("Message IDs array cannot be empty", exception.Message);
    }

    [Fact]
    public void ValidateMessageIds_WithNullOrEmptyId_ThrowsArgumentException()
    {
        // Arrange
        var messageIds = new[] { "123-0", "", "125-0" };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            StreamValidationHelper.ValidateMessageIds(messageIds));
        Assert.Contains("Message ID cannot be null, empty, or whitespace", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(10000)]
    public void ValidateMaxLength_WithValidLength_DoesNotThrow(int maxLength)
    {
        // Act & Assert
        var exception = Record.Exception(() => StreamValidationHelper.ValidateMaxLength(maxLength));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ValidateMaxLength_WithInvalidLength_ThrowsArgumentOutOfRangeException(int maxLength)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => 
            StreamValidationHelper.ValidateMaxLength(maxLength));
        Assert.Contains("Max length must be greater than zero", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(1000)]
    public void ValidateCount_WithValidCount_DoesNotThrow(int count)
    {
        // Act & Assert
        var exception = Record.Exception(() => StreamValidationHelper.ValidateCount(count));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ValidateCount_WithInvalidCount_ThrowsArgumentOutOfRangeException(int count)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => 
            StreamValidationHelper.ValidateCount(count));
        Assert.Contains("Count must be greater than zero", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(60000)]
    public void ValidateMinIdleTime_WithValidTime_DoesNotThrow(long minIdleTime)
    {
        // Act & Assert
        var exception = Record.Exception(() => StreamValidationHelper.ValidateMinIdleTime(minIdleTime));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void ValidateMinIdleTime_WithNegativeTime_ThrowsArgumentOutOfRangeException(long minIdleTime)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => 
            StreamValidationHelper.ValidateMinIdleTime(minIdleTime));
        Assert.Contains("Min idle time cannot be negative", exception.Message);
    }
}