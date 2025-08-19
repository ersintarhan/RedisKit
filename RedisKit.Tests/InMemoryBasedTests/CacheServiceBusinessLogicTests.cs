using FluentAssertions;
using RedisKit.Tests.InMemory;
using Xunit;

namespace RedisKit.Tests.InMemoryBasedTests;

public class CacheServiceBusinessLogicTests
{
    private readonly InMemoryRedisCache _cache;

    public CacheServiceBusinessLogicTests()
    {
        _cache = new InMemoryRedisCache();
    }

    [Fact]
    public async Task SetAsync_Should_Store_Value()
    {
        // Arrange
        var key = "test-key";
        var value = new TestData { Id = 1, Name = "Test" };

        // Act
        await _cache.SetAsync(key, value);
        var result = await _cache.GetAsync<TestData>(key);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetAsync_Should_Return_Null_For_NonExistent_Key()
    {
        // Act
        var result = await _cache.GetAsync<TestData>("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_With_TTL_Should_Expire()
    {
        // Arrange
        var key = "expiring-key";
        var value = new TestData { Id = 1, Name = "Test" };

        // Act
        await _cache.SetAsync(key, value, TimeSpan.FromMilliseconds(100));
        var beforeExpiry = await _cache.GetAsync<TestData>(key);

        await Task.Delay(150);
        var afterExpiry = await _cache.GetAsync<TestData>(key);

        // Assert
        beforeExpiry.Should().NotBeNull();
        afterExpiry.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_Should_Remove_Key()
    {
        // Arrange
        var key = "delete-key";
        var value = new TestData { Id = 1, Name = "Test" };
        await _cache.SetAsync(key, value);

        // Act
        await _cache.DeleteAsync(key);
        var result = await _cache.GetAsync<TestData>(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_Should_Return_True_For_Existing_Key()
    {
        // Arrange
        var key = "exists-key";
        var value = new TestData { Id = 1, Name = "Test" };
        await _cache.SetAsync(key, value);

        // Act
        var exists = await _cache.ExistsAsync(key);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_Should_Return_False_For_NonExistent_Key()
    {
        // Act
        var exists = await _cache.ExistsAsync("non-existent");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetManyAsync_Should_Return_Multiple_Values()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        await _cache.SetAsync("key1", new TestData { Id = 1, Name = "One" });
        await _cache.SetAsync("key2", new TestData { Id = 2, Name = "Two" });
        // key3 is not set

        // Act
        var results = await _cache.GetManyAsync<TestData>(keys);

        // Assert
        results.Should().HaveCount(3);
        results["key1"]!.Id.Should().Be(1);
        results["key2"]!.Id.Should().Be(2);
        results["key3"].Should().BeNull();
    }

    [Fact]
    public async Task SetManyAsync_Should_Store_Multiple_Values()
    {
        // Arrange
        var values = new Dictionary<string, TestData>
        {
            ["key1"] = new() { Id = 1, Name = "One" },
            ["key2"] = new() { Id = 2, Name = "Two" }
        };

        // Act
        await _cache.SetManyAsync(values);
        var result1 = await _cache.GetAsync<TestData>("key1");
        var result2 = await _cache.GetAsync<TestData>("key2");

        // Assert
        result1!.Id.Should().Be(1);
        result2!.Id.Should().Be(2);
    }

    [Fact]
    public async Task SetBytesAsync_Should_Store_Byte_Array()
    {
        // Arrange
        var key = "bytes-key";
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        await _cache.SetBytesAsync(key, bytes);
        var result = await _cache.GetBytesAsync(key);

        // Assert
        result.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task GetBytesAsync_Should_Return_Null_For_NonExistent_Key()
    {
        // Act
        var result = await _cache.GetBytesAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetKeyPrefix_Should_Apply_Prefix_To_Operations()
    {
        // Arrange
        _cache.SetKeyPrefix("app:");
        var key = "test-key";
        var value = new TestData { Id = 1, Name = "Test" };

        // Act
        await _cache.SetAsync(key, value);
        var result = await _cache.GetAsync<TestData>(key);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteBatchAsync_Should_Execute_Multiple_Operations()
    {
        // Arrange
        await _cache.SetAsync("batch1", new TestData { Id = 1, Name = "One" });

        // Act
        var result = await _cache.ExecuteBatchAsync(batch =>
        {
            batch.SetAsync("batch2", new TestData { Id = 2, Name = "Two" });
            batch.GetAsync<TestData>("batch1");
            batch.DeleteAsync("batch3");
            batch.ExistsAsync("batch1");
        });

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify the operations were executed
        var batch2 = await _cache.GetAsync<TestData>("batch2");
        batch2.Should().NotBeNull();
        batch2!.Id.Should().Be(2);
    }

    [Fact]
    public async Task Clear_Should_Remove_All_Keys()
    {
        // Arrange
        await _cache.SetAsync("key1", new TestData { Id = 1 });
        await _cache.SetAsync("key2", new TestData { Id = 2 });
        await _cache.SetAsync("key3", new TestData { Id = 3 });

        // Act
        _cache.Clear();

        // Assert
        var result1 = await _cache.GetAsync<TestData>("key1");
        var result2 = await _cache.GetAsync<TestData>("key2");
        var result3 = await _cache.GetAsync<TestData>("key3");

        result1.Should().BeNull();
        result2.Should().BeNull();
        result3.Should().BeNull();
    }

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}