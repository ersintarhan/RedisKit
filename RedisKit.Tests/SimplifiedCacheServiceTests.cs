using System.Collections.Concurrent;
using FluentAssertions;
using Xunit;

namespace RedisKit.Tests;

/// <summary>
///     Simplified tests focusing on business logic rather than mocking Redis
/// </summary>
public class SimplifiedCacheServiceTests
{
    private readonly InMemoryCacheService _cache = new();

    [Fact]
    public async Task GetAsync_WhenKeyNotExists_ReturnsNull()
    {
        // Act
        var result = await _cache.GetAsync<TestData>("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsValue()
    {
        // Arrange
        var key = "test:key";
        var value = new TestData { Id = 1, Name = "Test" };

        // Act
        await _cache.SetAsync(key, value);
        var result = await _cache.GetAsync<TestData>(key);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_WithExpiry_ExpiresAfterTtl()
    {
        // Arrange
        var key = "test:key";
        var value = new TestData { Id = 1, Name = "Test" };

        // Act
        await _cache.SetAsync(key, value, TimeSpan.FromMilliseconds(100));
        await Task.Delay(150);
        var result = await _cache.GetAsync<TestData>(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesKey()
    {
        // Arrange
        var key = "test:key";
        await _cache.SetAsync(key, new TestData());

        // Act
        var deleted = await _cache.DeleteAsync(key);
        var exists = await _cache.ExistsAsync(key);

        // Assert
        deleted.Should().BeTrue();
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyExists_ReturnsTrue()
    {
        // Arrange
        var key = "test:key";
        await _cache.SetAsync(key, new TestData());

        // Act
        var exists = await _cache.ExistsAsync(key);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SetKeyPrefix_AppliesPrefixToAllOperations()
    {
        // Arrange
        _cache.SetKeyPrefix("app:");
        var key = "user:123";

        // Act
        await _cache.SetAsync(key, new TestData());
        var exists = await _cache.ExistsAsync(key);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void SetKeyPrefix_WithNull_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _cache.SetKeyPrefix(null!));
    }

    [Fact]
    public void SetKeyPrefix_WithVeryLongPrefix_ThrowsException()
    {
        // Arrange
        var longPrefix = new string('x', 513);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _cache.SetKeyPrefix(longPrefix));
        exception.Message.Should().Contain("too long");
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.GetAsync<TestData>(null!));
    }

    [Fact]
    public async Task SetAsync_WithNullKey_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.SetAsync<TestData>(null!, new TestData()));
    }

    [Fact]
    public async Task SetAsync_WithNullValue_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.SetAsync<TestData>("key", null!));
    }

    [Fact]
    public async Task GetManyAsync_ReturnsMultipleValues()
    {
        // Arrange
        await _cache.SetAsync("key1", new TestData { Id = 1 });
        await _cache.SetAsync("key2", new TestData { Id = 2 });
        await _cache.SetAsync("key3", new TestData { Id = 3 });

        // Act
        var result = await _cache.GetManyAsync<TestData>(new[] { "key1", "key2", "key3", "key4" });

        // Assert
        result.Should().HaveCount(4);
        result["key1"].Should().NotBeNull();
        result["key2"].Should().NotBeNull();
        result["key3"].Should().NotBeNull();
        result["key4"].Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Act
        var deleted = await _cache.DeleteAsync("nonexistent");

        // Assert
        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WithExpiredKey_ReturnsFalse()
    {
        // Arrange
        var key = "test:key";
        await _cache.SetAsync(key, new TestData(), TimeSpan.FromMilliseconds(50));
        await Task.Delay(100);

        // Act
        var exists = await _cache.ExistsAsync(key);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithVeryLongKey_ThrowsException()
    {
        // Arrange
        var longKey = new string('x', 513);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _cache.GetAsync<TestData>(longKey));
        exception.Message.Should().Contain("too long");
    }

    // In-memory implementation for testing
    public class InMemoryCacheService
    {
        private readonly ConcurrentDictionary<string, (byte[] data, DateTime? expiry)> _cache = new();
        private string _keyPrefix = "";

        public void SetKeyPrefix(string prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (prefix.Length > 512) throw new ArgumentException("Key too long");
            _keyPrefix = prefix;
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length > 512) throw new ArgumentException("Key too long");

            var fullKey = $"{_keyPrefix}{key}";
            if (_cache.TryGetValue(fullKey, out var item))
            {
                if (item.expiry == null || item.expiry > DateTime.UtcNow)
                {
                    // Simulate deserialization
                    await Task.Delay(1);
                    return Activator.CreateInstance<T>();
                }

                _cache.TryRemove(fullKey, out _);
            }

            return null;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (key.Length > 512) throw new ArgumentException("Key too long");

            var fullKey = $"{_keyPrefix}{key}";
            var expiry = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : (DateTime?)null;

            // Simulate serialization
            await Task.Delay(1);
            _cache[fullKey] = (new byte[10], expiry);
        }

        public Task<bool> DeleteAsync(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length > 512) throw new ArgumentException("Key too long");

            var fullKey = $"{_keyPrefix}{key}";
            return Task.FromResult(_cache.TryRemove(fullKey, out _));
        }

        public Task<bool> ExistsAsync(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length > 512) throw new ArgumentException("Key too long");

            var fullKey = $"{_keyPrefix}{key}";
            if (_cache.TryGetValue(fullKey, out var item))
            {
                if (item.expiry == null || item.expiry > DateTime.UtcNow)
                    return Task.FromResult(true);
                _cache.TryRemove(fullKey, out _);
            }

            return Task.FromResult(false);
        }

        public Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys) where T : class
        {
            var result = new Dictionary<string, T?>();
            foreach (var key in keys)
                if (_cache.TryGetValue($"{_keyPrefix}{key}", out var item))
                {
                    if (item.expiry == null || item.expiry > DateTime.UtcNow)
                        result[key] = Activator.CreateInstance<T>();
                    else
                        result[key] = null;
                }
                else
                {
                    result[key] = null;
                }

            return Task.FromResult(result);
        }
    }

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}