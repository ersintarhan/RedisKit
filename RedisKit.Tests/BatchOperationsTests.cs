using FluentAssertions;
using NSubstitute;
using RedisKit.Serialization;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class BatchOperationsTests
{
    private readonly IBatch _batch;
    private readonly BatchOperations _batchOperations;
    private readonly IRedisSerializer _serializer;

    public BatchOperationsTests()
    {
        _batch = Substitute.For<IBatch>();
        _serializer = Substitute.For<IRedisSerializer>();
        _batchOperations = new BatchOperations(_batch, _serializer);
    }


    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        const string key = "test:key";
        _batch.KeyDeleteAsync(key)
            .Returns(Task.FromResult(true));

        // Act
        var result = await _batchOperations.DeleteAsync(key);

        // Assert
        result.Should().BeTrue();
        await _batch.Received(1).KeyDeleteAsync(key);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        const string key = "test:nonexistent";
        _batch.KeyDeleteAsync(key)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _batchOperations.DeleteAsync(key);

        // Assert
        result.Should().BeFalse();
        await _batch.Received(1).KeyDeleteAsync(key);
    }

    [Fact]
    public async Task DeleteAsync_WithNullKey_PassesToRedis()
    {
        // Arrange
        string? key = null;
        _batch.KeyDeleteAsync(key!)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _batchOperations.DeleteAsync(key!);

        // Assert
        result.Should().BeFalse();
        await _batch.Received(1).KeyDeleteAsync(key!);
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyKey_PassesToRedis()
    {
        // Arrange
        const string key = "";
        _batch.KeyDeleteAsync(key)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _batchOperations.DeleteAsync(key);

        // Assert
        result.Should().BeFalse();
        await _batch.Received(1).KeyDeleteAsync(key);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        const string key = "test:existing";
        _batch.KeyExistsAsync(key)
            .Returns(Task.FromResult(true));

        // Act
        var result = await _batchOperations.ExistsAsync(key);

        // Assert
        result.Should().BeTrue();
        await _batch.Received(1).KeyExistsAsync(key);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        const string key = "test:nonexistent";
        _batch.KeyExistsAsync(key)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _batchOperations.ExistsAsync(key);

        // Assert
        result.Should().BeFalse();
        await _batch.Received(1).KeyExistsAsync(key);
    }

    [Fact]
    public async Task ExistsAsync_WithNullKey_PassesToRedis()
    {
        // Arrange
        string? key = null;
        _batch.KeyExistsAsync(key!)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _batchOperations.ExistsAsync(key!);

        // Assert
        result.Should().BeFalse();
        await _batch.Received(1).KeyExistsAsync(key!);
    }

    [Fact]
    public async Task ExistsAsync_WithEmptyKey_PassesToRedis()
    {
        // Arrange
        const string key = "";
        _batch.KeyExistsAsync(key)
            .Returns(Task.FromResult(false));

        // Act
        var result = await _batchOperations.ExistsAsync(key);

        // Assert
        result.Should().BeFalse();
        await _batch.Received(1).KeyExistsAsync(key);
    }

    #endregion

    #region Combined Operations Tests

    [Fact]
    public async Task DeleteAsync_And_ExistsAsync_InSameBatch_WorkCorrectly()
    {
        // Arrange
        const string keyToDelete = "test:delete";
        const string keyToCheck = "test:check";

        _batch.KeyDeleteAsync(keyToDelete)
            .Returns(Task.FromResult(true));
        _batch.KeyExistsAsync(keyToCheck)
            .Returns(Task.FromResult(true));

        // Act
        var deleteTask = _batchOperations.DeleteAsync(keyToDelete);
        var existsTask = _batchOperations.ExistsAsync(keyToCheck);

        var deleteResult = await deleteTask;
        var existsResult = await existsTask;

        // Assert
        deleteResult.Should().BeTrue();
        existsResult.Should().BeTrue();

        await _batch.Received(1).KeyDeleteAsync(keyToDelete);
        await _batch.Received(1).KeyExistsAsync(keyToCheck);
    }

    [Fact]
    public async Task Multiple_DeleteAsync_Operations_InBatch_WorkCorrectly()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        var results = new[] { true, false, true };

        for (var i = 0; i < keys.Length; i++)
            _batch.KeyDeleteAsync(keys[i])
                .Returns(Task.FromResult(results[i]));

        // Act
        var tasks = keys.Select(key => _batchOperations.DeleteAsync(key)).ToArray();
        var actualResults = await Task.WhenAll(tasks);

        // Assert
        actualResults.Should().BeEquivalentTo(results);

        foreach (var key in keys) await _batch.Received(1).KeyDeleteAsync(key);
    }

    [Fact]
    public async Task Multiple_ExistsAsync_Operations_InBatch_WorkCorrectly()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3", "key4" };
        var results = new[] { true, true, false, true };

        for (var i = 0; i < keys.Length; i++)
            _batch.KeyExistsAsync(keys[i])
                .Returns(Task.FromResult(results[i]));

        // Act
        var tasks = keys.Select(key => _batchOperations.ExistsAsync(key)).ToArray();
        var actualResults = await Task.WhenAll(tasks);

        // Assert
        actualResults.Should().BeEquivalentTo(results);

        foreach (var key in keys) await _batch.Received(1).KeyExistsAsync(key);
    }

    #endregion

    #region GetResultsAsync Tests

    [Fact]
    public async Task GetResultsAsync_WithSuccessfulOperations_ReturnsSuccess()
    {
        // Arrange
        const string key = "test:key";
        _batch.KeyDeleteAsync(key)
            .Returns(Task.FromResult(true));

        // Act
        _ = await _batchOperations.DeleteAsync(key);
        var result = await _batchOperations.GetResultsAsync();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetResultsAsync_WithNoOperations_ReturnsSuccess()
    {
        // Act
        var result = await _batchOperations.GetResultsAsync();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}