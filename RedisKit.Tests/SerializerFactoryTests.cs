using System;
using Microsoft.Extensions.Logging;
using Moq;
using RedisKit.Models;
using RedisKit.Serialization;
using Xunit;

namespace RedisKit.Tests
{
    public class SerializerFactoryTests
    {
        private readonly Mock<ILoggerFactory> _mockLoggerFactory;
        private readonly Mock<ILogger<MessagePackRedisSerializer>> _mockMessagePackLogger;
        private readonly Mock<ILogger<SystemTextJsonRedisSerializer>> _mockJsonLogger;

        public SerializerFactoryTests()
        {
            _mockLoggerFactory = new Mock<ILoggerFactory>();
            _mockMessagePackLogger = new Mock<ILogger<MessagePackRedisSerializer>>();
            _mockJsonLogger = new Mock<ILogger<SystemTextJsonRedisSerializer>>();

            _mockLoggerFactory
                .Setup(x => x.CreateLogger(typeof(MessagePackRedisSerializer).FullName!))
                .Returns(_mockMessagePackLogger.Object);
            _mockLoggerFactory
                .Setup(x => x.CreateLogger(typeof(SystemTextJsonRedisSerializer).FullName!))
                .Returns(_mockJsonLogger.Object);
        }

        [Fact]
        public void Create_WithMessagePackType_ReturnsMessagePackSerializer()
        {
            // Act
            var serializer = RedisSerializerFactory.Create(SerializerType.MessagePack, _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<MessagePackRedisSerializer>(serializer);
        }

        [Fact]
        public void Create_WithSystemTextJsonType_ReturnsJsonSerializer()
        {
            // Act
            var serializer = RedisSerializerFactory.Create(SerializerType.SystemTextJson, _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<SystemTextJsonRedisSerializer>(serializer);
        }

        [Fact]
        public void Create_WithCustomType_ThrowsArgumentException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                RedisSerializerFactory.Create(SerializerType.Custom, _mockLoggerFactory.Object));
            
            Assert.Contains("Custom serializer type requires using CreateCustom method", exception.Message);
        }

        [Fact]
        public void Create_WithInvalidType_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RedisSerializerFactory.Create((SerializerType)999, _mockLoggerFactory.Object));
        }

        [Fact]
        public void Create_CachesSerializers()
        {
            // Arrange
            RedisSerializerFactory.ClearCache();

            // Act
            var serializer1 = RedisSerializerFactory.Create(SerializerType.MessagePack, _mockLoggerFactory.Object);
            var serializer2 = RedisSerializerFactory.Create(SerializerType.MessagePack, _mockLoggerFactory.Object);

            // Assert
            Assert.Same(serializer1, serializer2);
            Assert.Equal(1, RedisSerializerFactory.CacheSize);
        }

        [Fact]
        public void CreateWithOptions_WithValidMessagePackOptions_ReturnsSerializer()
        {
            // Arrange
            var options = MessagePack.MessagePackSerializerOptions.Standard;

            // Act
            var serializer = RedisSerializerFactory.CreateWithOptions(
                SerializerType.MessagePack, 
                options, 
                _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<MessagePackRedisSerializer>(serializer);
        }

        [Fact]
        public void CreateWithOptions_WithValidJsonOptions_ReturnsSerializer()
        {
            // Arrange
            var options = new System.Text.Json.JsonSerializerOptions();

            // Act
            var serializer = RedisSerializerFactory.CreateWithOptions(
                SerializerType.SystemTextJson, 
                options, 
                _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<SystemTextJsonRedisSerializer>(serializer);
        }

        [Fact]
        public void CreateWithOptions_WithNullOptions_CallsCreateWithoutOptions()
        {
            // Act
            var serializer = RedisSerializerFactory.CreateWithOptions(
                SerializerType.MessagePack, 
                null, 
                _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<MessagePackRedisSerializer>(serializer);
        }

        [Fact]
        public void CreateWithOptions_WithInvalidOptionsType_ThrowsArgumentException()
        {
            // Arrange
            var invalidOptions = "invalid options";

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                RedisSerializerFactory.CreateWithOptions(
                    SerializerType.MessagePack, 
                    invalidOptions, 
                    _mockLoggerFactory.Object));

            Assert.Contains("Invalid options type", exception.Message);
        }

        [Fact]
        public void CreateTyped_WithMessagePackOptions_ReturnsSerializer()
        {
            // Arrange
            var options = MessagePack.MessagePackSerializerOptions.Standard;

            // Act
            var serializer = RedisSerializerFactory.Create(
                SerializerType.MessagePack, 
                options, 
                _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<MessagePackRedisSerializer>(serializer);
        }

        [Fact]
        public void CreateTyped_WithJsonOptions_ReturnsSerializer()
        {
            // Arrange
            var options = new System.Text.Json.JsonSerializerOptions();

            // Act
            var serializer = RedisSerializerFactory.Create(
                SerializerType.SystemTextJson, 
                options, 
                _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<SystemTextJsonRedisSerializer>(serializer);
        }

        [Fact]
        public void CreateTyped_WithWrongOptionsType_ThrowsArgumentException()
        {
            // Arrange
            var jsonOptions = new System.Text.Json.JsonSerializerOptions();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                RedisSerializerFactory.Create(
                    SerializerType.MessagePack, 
                    jsonOptions, 
                    _mockLoggerFactory.Object));

            Assert.Contains("Invalid options type", exception.Message);
        }

        [Fact]
        public void CreateCustom_WithValidSerializerType_ReturnsSerializer()
        {
            // Arrange
            var customType = typeof(TestCustomSerializer);

            // Act
            var serializer = RedisSerializerFactory.CreateCustom(customType, _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<TestCustomSerializer>(serializer);
        }

        [Fact]
        public void CreateCustom_WithInvalidType_ThrowsArgumentException()
        {
            // Arrange
            var invalidType = typeof(string);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                RedisSerializerFactory.CreateCustom(invalidType, _mockLoggerFactory.Object));

            Assert.Contains("must implement IRedisSerializer", exception.Message);
        }

        [Fact]
        public void CreateCustom_WithOptions_ReturnsSerializer()
        {
            // Arrange
            var customType = typeof(TestCustomSerializerWithOptions);
            var options = "test options";

            // Act
            var serializer = RedisSerializerFactory.CreateCustom(customType, options, _mockLoggerFactory.Object);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<TestCustomSerializerWithOptions>(serializer);
        }

        [Fact]
        public void RegisterCustomSerializer_WithValidType_RegistersSuccessfully()
        {
            // Arrange
            var customType = typeof(TestCustomSerializer);

            // Act
            RedisSerializerFactory.RegisterCustomSerializer(customType);

            // Assert
            Assert.True(RedisSerializerFactory.TryGetCustomSerializer<TestCustomSerializer>(out var serializer));
            Assert.NotNull(serializer);
        }

        [Fact]
        public void RegisterCustomSerializer_WithInstance_RegistersSuccessfully()
        {
            // Arrange
            var customSerializer = new TestCustomSerializer();

            // Act
            RedisSerializerFactory.RegisterCustomSerializer(customSerializer);

            // Assert
            Assert.True(RedisSerializerFactory.TryGetCustomSerializer<TestCustomSerializer>(out var serializer));
            Assert.Same(customSerializer, serializer);
        }

        [Fact]
        public void RegisterCustomSerializer_WithNullInstance_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                RedisSerializerFactory.RegisterCustomSerializer<TestCustomSerializer>(null!));
        }

        [Fact]
        public void TryGetCustomSerializer_WhenNotRegistered_ReturnsFalse()
        {
            // Arrange
            RedisSerializerFactory.ClearCache();

            // Act
            var result = RedisSerializerFactory.TryGetCustomSerializer<TestCustomSerializer>(out var serializer);

            // Assert
            Assert.False(result);
            Assert.Null(serializer);
        }

        [Fact]
        public void ClearCache_RemovesAllCachedSerializers()
        {
            // Arrange
            RedisSerializerFactory.Create(SerializerType.MessagePack, _mockLoggerFactory.Object);
            RedisSerializerFactory.RegisterCustomSerializer(new TestCustomSerializer());
            
            Assert.True(RedisSerializerFactory.CacheSize > 0);
            Assert.True(RedisSerializerFactory.CustomSerializerCount > 0);

            // Act
            RedisSerializerFactory.ClearCache();

            // Assert
            Assert.Equal(0, RedisSerializerFactory.CacheSize);
            Assert.Equal(0, RedisSerializerFactory.CustomSerializerCount);
        }

        // Test helper classes
        private class TestCustomSerializer : IRedisSerializer
        {
            public string Name => "TestCustomSerializer";
            
            public byte[] Serialize<T>(T obj) => Array.Empty<byte>();
            
            public T? Deserialize<T>(byte[] data) => default;
            
            public Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default) 
                => Task.FromResult(Array.Empty<byte>());
                
            public Task<T?> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) 
                => Task.FromResult<T?>(default);
                
            public Task<object?> DeserializeAsync(byte[] data, Type type, CancellationToken cancellationToken = default)
                => Task.FromResult<object?>(null);
        }

        private class TestCustomSerializerWithOptions : IRedisSerializer
        {
            public TestCustomSerializerWithOptions() { }
            public TestCustomSerializerWithOptions(string options) { }
            
            public string Name => "TestCustomSerializerWithOptions";
            
            public byte[] Serialize<T>(T obj) => Array.Empty<byte>();
            
            public T? Deserialize<T>(byte[] data) => default;
            
            public Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default) 
                => Task.FromResult(Array.Empty<byte>());
                
            public Task<T?> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) 
                => Task.FromResult<T?>(default);
                
            public Task<object?> DeserializeAsync(byte[] data, Type type, CancellationToken cancellationToken = default)
                => Task.FromResult<object?>(null);
        }
    }
}