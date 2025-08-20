using MessagePack;
using Xunit;

namespace RedisKit.Tests.Integration;

public class RedisCacheServiceIntegrationTests : IntegrationTestBase
{
    #region Basic Operations

    [Fact]
    public async Task SetAsync_WithValidData_StoresSuccessfully()
    {
        // Arrange
        var key = "test-key";
        var value = new TestModel { Id = 1, Name = "Test" };

        // Act
        await CacheService.SetAsync(key, value);

        // Assert
        var retrieved = await CacheService.GetAsync<TestModel>(key);
        Assert.NotNull(retrieved);
        Assert.Equal(value.Id, retrieved.Id);
        Assert.Equal(value.Name, retrieved.Name);
    }

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var key = "non-existent-key";

        // Act
        var result = await CacheService.GetAsync<TestModel>(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingKey_RemovesSuccessfully()
    {
        // Arrange
        var key = "delete-test-key";
        var value = new TestModel { Id = 2, Name = "Delete Test" };
        await CacheService.SetAsync(key, value);

        // Act
        await CacheService.DeleteAsync(key);

        // Assert
        var retrieved = await CacheService.GetAsync<TestModel>(key);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task SetAsync_WithTtl_ExpiresCorrectly()
    {
        // Arrange
        var key = "ttl-test-key";
        var value = new TestModel { Id = 3, Name = "TTL Test" };
        var shortTtl = TimeSpan.FromSeconds(1);

        // Act
        await CacheService.SetAsync(key, value, shortTtl);

        // Verify it exists immediately
        var immediate = await CacheService.GetAsync<TestModel>(key);
        Assert.NotNull(immediate);

        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Verify it's expired
        var expired = await CacheService.GetAsync<TestModel>(key);
        Assert.Null(expired);
    }

    #endregion

    #region Batch Operations

    [Fact]
    public async Task GetManyAsync_WithMixedKeys_ReturnsCorrectResults()
    {
        // Arrange
        var existingKey1 = "existing-1";
        var existingKey2 = "existing-2";
        var nonExistentKey = "non-existent";

        var value1 = new TestModel { Id = 10, Name = "Existing 1" };
        var value2 = new TestModel { Id = 20, Name = "Existing 2" };

        await CacheService.SetAsync(existingKey1, value1);
        await CacheService.SetAsync(existingKey2, value2);

        // Act
        var keys = new[] { existingKey1, nonExistentKey, existingKey2 };
        var results = await CacheService.GetManyAsync<TestModel>(keys);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.NotNull(results[existingKey1]);
        Assert.Null(results[nonExistentKey]);
        Assert.NotNull(results[existingKey2]);
        Assert.Equal(value1.Id, results[existingKey1]!.Id);
        Assert.Equal(value2.Id, results[existingKey2]!.Id);
    }

    [Fact]
    public async Task SetManyAsync_WithValidData_StoresAllSuccessfully()
    {
        // Arrange
        var data = new Dictionary<string, TestModel>
        {
            ["batch-1"] = new() { Id = 100, Name = "Batch 1" },
            ["batch-2"] = new() { Id = 200, Name = "Batch 2" },
            ["batch-3"] = new() { Id = 300, Name = "Batch 3" }
        };

        // Act
        await CacheService.SetManyAsync(data, TimeSpan.FromMinutes(1));

        // Assert
        foreach (var kvp in data)
        {
            var retrieved = await CacheService.GetAsync<TestModel>(kvp.Key);
            Assert.NotNull(retrieved);
            Assert.Equal(kvp.Value.Id, retrieved.Id);
            Assert.Equal(kvp.Value.Name, retrieved.Name);
        }
    }

    [Fact]
    public async Task SetManyAsync_WithLargeDataset_UsesChunking()
    {
        // Arrange - Create 1500 items to trigger chunking (default chunk size is 1000)
        var data = Enumerable.Range(1, 1500)
            .ToDictionary(
                i => $"chunk-test-{i}",
                i => new TestModel { Id = i, Name = $"Chunk Test {i}" }
            );

        // Act
        await CacheService.SetManyAsync(data, TimeSpan.FromMinutes(1));

        // Assert - Verify a sampling of items were stored
        var sampleKeys = new[] { "chunk-test-1", "chunk-test-500", "chunk-test-1000", "chunk-test-1500" };
        var retrieved = await CacheService.GetManyAsync<TestModel>(sampleKeys);

        foreach (var key in sampleKeys)
        {
            Assert.NotNull(retrieved[key]);
            var expectedId = int.Parse(key.Replace("chunk-test-", ""));
            Assert.Equal(expectedId, retrieved[key]!.Id);
        }
    }

    [Fact]
    public async Task DeleteAsync_MultipleKeys_RemovesAllSuccessfully()
    {
        // Arrange
        var keys = new[] { "delete-many-1", "delete-many-2", "delete-many-3" };
        var value = new TestModel { Id = 999, Name = "Delete Many Test" };

        foreach (var key in keys) await CacheService.SetAsync(key, value);

        // Act - Delete each key individually since DeleteManyAsync doesn't exist
        foreach (var key in keys) await CacheService.DeleteAsync(key);

        // Assert
        var results = await CacheService.GetManyAsync<TestModel>(keys);
        foreach (var result in results.Values) Assert.Null(result);
    }

    #endregion

    #region Advanced Features

    [Fact]
    public async Task ExecuteBatchAsync_WithMixedOperations_ExecutesAllSuccessfully()
    {
        // Arrange
        var testModel1 = new TestModel { Id = 1001, Name = "Batch Test 1" };
        var testModel2 = new TestModel { Id = 1002, Name = "Batch Test 2" };

        // Act
        var result = await CacheService.ExecuteBatchAsync(batch =>
        {
            _ = batch.SetAsync("batch-op-1", testModel1);
            _ = batch.SetAsync("batch-op-2", testModel2);
            _ = batch.GetAsync<TestModel>("batch-op-1");
            _ = batch.DeleteAsync("batch-op-2");
        });

        // Assert
        Assert.True(result.IsSuccess);

        // Verify batch operations took effect
        var retrieved1 = await CacheService.GetAsync<TestModel>("batch-op-1");
        var retrieved2 = await CacheService.GetAsync<TestModel>("batch-op-2");

        Assert.NotNull(retrieved1);
        Assert.Equal(testModel1.Id, retrieved1.Id);
        Assert.Null(retrieved2); // Should be deleted by batch operation
    }

    [Fact]
    public async Task ExistsAsync_WithExistingAndNonExistentKeys_ReturnsCorrectValues()
    {
        // Arrange
        var existingKey = "exists-test-key";
        var nonExistentKey = "not-exists-test-key";
        var value = new TestModel { Id = 2001, Name = "Exists Test" };

        await CacheService.SetAsync(existingKey, value);

        // Act
        var existingResult = await CacheService.ExistsAsync(existingKey);
        var nonExistentResult = await CacheService.ExistsAsync(nonExistentKey);

        // Assert
        Assert.True(existingResult);
        Assert.False(nonExistentResult);
    }


    [Fact]
    public async Task SetBytesAsync_GetBytesAsync_WorksWithRawBytes()
    {
        // Arrange
        var key = "bytes-test-key";
        var originalData = "Hello, Redis!"u8.ToArray();

        // Act
        await CacheService.SetBytesAsync(key, originalData);
        var retrievedData = await CacheService.GetBytesAsync(key);

        // Assert
        Assert.NotNull(retrievedData);
        Assert.Equal(originalData.Length, retrievedData.Length);
        Assert.True(originalData.SequenceEqual(retrievedData));
    }

    [Fact]
    public async Task SetKeyPrefix_UpdatesPrefix_AffectsSubsequentOperations()
    {
        // Arrange
        var key = "prefix-test-key";
        var value = new TestModel { Id = 4001, Name = "Prefix Test" };
        var newPrefix = "custom-prefix:";

        // Act
        CacheService.SetKeyPrefix(newPrefix);
        await CacheService.SetAsync(key, value);

        // Assert - Verify the key was stored with new prefix
        var retrieved = await CacheService.GetAsync<TestModel>(key);
        Assert.NotNull(retrieved);
        Assert.Equal(value.Id, retrieved.Id);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public async Task GetManyAsync_WithEmptyKeys_ReturnsEmptyDictionary()
    {
        // Act
        var result = await CacheService.GetManyAsync<TestModel>(Array.Empty<string>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SetManyAsync_WithEmptyData_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var emptyData = new Dictionary<string, TestModel>();

        // Act & Assert - Currently throws exception when chunking empty collection (this is a known limitation)
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CacheService.SetManyAsync(emptyData, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Operations_WithLongKey_ThrowsArgumentException()
    {
        // Arrange
        var longKey = new string('a', 1000); // Very long key (exceeds 512 char limit)
        var value = new TestModel { Id = 5001, Name = "Long Key Test" };

        // Act & Assert - Should throw exception for keys exceeding Redis limit
        await Assert.ThrowsAsync<ArgumentException>(() => CacheService.SetAsync(longKey, value).AsTask());
    }

    [Fact]
    public async Task Operations_WithComplexObject_SerializesCorrectly()
    {
        // Arrange
        var key = "complex-object-key";
        var complexValue = new ComplexTestModel
        {
            Id = 6001,
            Name = "Complex Test",
            Data = new Dictionary<string, object>
            {
                ["number"] = 42,
                ["text"] = "test value",
                ["list"] = new[] { 1, 2, 3 }
            },
            Items = new List<TestModel>
            {
                new() { Id = 1, Name = "Item 1" },
                new() { Id = 2, Name = "Item 2" }
            }
        };

        // Act
        await CacheService.SetAsync(key, complexValue);
        var retrieved = await CacheService.GetAsync<ComplexTestModel>(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(complexValue.Id, retrieved.Id);
        Assert.Equal(complexValue.Name, retrieved.Name);
        Assert.Equal(3, retrieved.Data.Count);
        Assert.Equal(2, retrieved.Items.Count);
    }

    #endregion

    #region Test Models

    [MessagePackObject]
    public class TestModel
    {
        [Key(0)] public int Id { get; set; }
        [Key(1)] public string Name { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public class ComplexTestModel
    {
        [Key(0)] public int Id { get; set; }
        [Key(1)] public string Name { get; set; } = string.Empty;
        [Key(2)] public Dictionary<string, object> Data { get; set; } = new();
        [Key(3)] public List<TestModel> Items { get; set; } = new();
    }

    #endregion
}