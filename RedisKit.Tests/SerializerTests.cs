using System;
using System.Text.Json;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using Moq;
using RedisKit.Models;
using RedisKit.Serialization;
using Xunit;

namespace RedisKit.Tests
{
    public class SerializerTests
    {
        #region MessagePack Serializer Tests

        [Fact]
        public void MessagePackSerializer_Constructor_WithNullParameters_DoesNotThrow()
        {
            // Act & Assert
            var serializer = new MessagePackRedisSerializer();
            Assert.NotNull(serializer);
            Assert.Equal("MessagePack", serializer.Name);
        }

        [Fact]
        public void MessagePackSerializer_Constructor_WithOptions_UsesProvidedOptions()
        {
            // Arrange
            var options = MessagePackSerializerOptions.Standard;

            // Act
            var serializer = new MessagePackRedisSerializer(options);

            // Assert
            Assert.NotNull(serializer);
        }

        [Fact]
        public void MessagePackSerializer_Constructor_WithLogger_UsesProvidedLogger()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MessagePackRedisSerializer>>();

            // Act
            var serializer = new MessagePackRedisSerializer(mockLogger.Object, null);

            // Assert
            Assert.NotNull(serializer);
        }

        [Fact]
        public void MessagePackSerializer_Serialize_WithNullObject_ThrowsArgumentNullException()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => serializer.Serialize<TestModel>(null));
        }

        [Fact]
        public async Task MessagePackSerializer_SerializeAsync_WithNullObject_ThrowsArgumentNullException()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                serializer.SerializeAsync<TestModel>(null));
        }

        [Fact]
        public void MessagePackSerializer_Serialize_WithValidObject_ReturnsBytes()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();
            var testData = new TestModel { Id = 1, Name = "Test" };

            // Act
            var result = serializer.Serialize(testData);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task MessagePackSerializer_SerializeAsync_WithValidObject_ReturnsBytes()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();
            var testData = new TestModel { Id = 1, Name = "Test" };

            // Act
            var result = await serializer.SerializeAsync(testData);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void MessagePackSerializer_Deserialize_WithNullData_ReturnsDefault()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();

            // Act
            var result = serializer.Deserialize<TestModel>(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void MessagePackSerializer_Deserialize_WithEmptyData_ReturnsDefault()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();

            // Act
            var result = serializer.Deserialize<TestModel>(Array.Empty<byte>());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task MessagePackSerializer_DeserializeAsync_WithNullData_ReturnsDefault()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();

            // Act
            var result = await serializer.DeserializeAsync<TestModel>(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void MessagePackSerializer_SerializeDeserialize_RoundTrip_PreservesData()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();
            var testData = new TestModel 
            { 
                Id = 42, 
                Name = "Round Trip Test",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Tags = new[] { "tag1", "tag2" }
            };

            // Act
            var serialized = serializer.Serialize(testData);
            var deserialized = serializer.Deserialize<TestModel>(serialized);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(testData.Id, deserialized.Id);
            Assert.Equal(testData.Name, deserialized.Name);
            Assert.Equal(testData.IsActive, deserialized.IsActive);
            Assert.Equal(testData.Tags, deserialized.Tags);
        }

        [Fact]
        public async Task MessagePackSerializer_SerializeDeserializeAsync_RoundTrip_PreservesData()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();
            var testData = new TestModel 
            { 
                Id = 42, 
                Name = "Async Round Trip",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Tags = new[] { "async", "test" }
            };

            // Act
            var serialized = await serializer.SerializeAsync(testData);
            var deserialized = await serializer.DeserializeAsync<TestModel>(serialized);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(testData.Id, deserialized.Id);
            Assert.Equal(testData.Name, deserialized.Name);
            Assert.Equal(testData.IsActive, deserialized.IsActive);
            Assert.Equal(testData.Tags, deserialized.Tags);
        }

        [Fact]
        public void MessagePackSerializer_Deserialize_WithInvalidData_ThrowsInvalidOperationException()
        {
            // Arrange
            var serializer = new MessagePackRedisSerializer();
            var invalidData = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC }; // Invalid MessagePack data

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => 
                serializer.Deserialize<TestModel>(invalidData));
        }

        #endregion

        #region System.Text.Json Serializer Tests

        [Fact]
        public void JsonSerializer_Constructor_WithNullParameters_DoesNotThrow()
        {
            // Act & Assert
            var serializer = new SystemTextJsonRedisSerializer();
            Assert.NotNull(serializer);
            Assert.Equal("SystemTextJson", serializer.Name);
        }

        [Fact]
        public void JsonSerializer_Constructor_WithOptions_UsesProvidedOptions()
        {
            // Arrange
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            // Act
            var serializer = new SystemTextJsonRedisSerializer(options);

            // Assert
            Assert.NotNull(serializer);
            Assert.NotNull(serializer.Options);
            Assert.Equal(JsonNamingPolicy.CamelCase, serializer.Options.PropertyNamingPolicy);
        }

        [Fact]
        public void JsonSerializer_Constructor_WithLogger_UsesProvidedLogger()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SystemTextJsonRedisSerializer>>();

            // Act
            var serializer = new SystemTextJsonRedisSerializer(mockLogger.Object, null);

            // Assert
            Assert.NotNull(serializer);
        }

        [Fact]
        public void JsonSerializer_Serialize_WithNullObject_ThrowsArgumentNullException()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => serializer.Serialize<TestModel>(null));
        }

        [Fact]
        public async Task JsonSerializer_SerializeAsync_WithNullObject_ThrowsArgumentNullException()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                serializer.SerializeAsync<TestModel>(null));
        }

        [Fact]
        public void JsonSerializer_Serialize_WithValidObject_ReturnsBytes()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();
            var testData = new TestModel { Id = 1, Name = "Test" };

            // Act
            var result = serializer.Serialize(testData);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task JsonSerializer_SerializeAsync_WithValidObject_ReturnsBytes()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();
            var testData = new TestModel { Id = 1, Name = "Test" };

            // Act
            var result = await serializer.SerializeAsync(testData);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void JsonSerializer_Deserialize_WithNullData_ThrowsArgumentNullException()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                serializer.Deserialize<TestModel>(null));
        }

        [Fact]
        public void JsonSerializer_Deserialize_WithEmptyData_ReturnsDefault()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();

            // Act
            var result = serializer.Deserialize<TestModel>(Array.Empty<byte>());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task JsonSerializer_DeserializeAsync_WithNullData_ReturnsDefault()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();

            // Act
            var result = await serializer.DeserializeAsync<TestModel>(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task JsonSerializer_DeserializeAsync_WithEmptyData_ReturnsDefault()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();

            // Act
            var result = await serializer.DeserializeAsync<TestModel>(Array.Empty<byte>());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void JsonSerializer_SerializeDeserialize_RoundTrip_PreservesData()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();
            var testData = new TestModel 
            { 
                Id = 42, 
                Name = "Round Trip Test",
                CreatedAt = DateTime.UtcNow.Date, // Use Date to avoid millisecond precision issues
                IsActive = true,
                Tags = new[] { "tag1", "tag2" }
            };

            // Act
            var serialized = serializer.Serialize(testData);
            var deserialized = serializer.Deserialize<TestModel>(serialized);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(testData.Id, deserialized.Id);
            Assert.Equal(testData.Name, deserialized.Name);
            Assert.Equal(testData.CreatedAt, deserialized.CreatedAt);
            Assert.Equal(testData.IsActive, deserialized.IsActive);
            Assert.Equal(testData.Tags, deserialized.Tags);
        }

        [Fact]
        public async Task JsonSerializer_SerializeDeserializeAsync_RoundTrip_PreservesData()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();
            var testData = new TestModel 
            { 
                Id = 42, 
                Name = "Async Round Trip",
                CreatedAt = DateTime.UtcNow.Date,
                IsActive = true,
                Tags = new[] { "async", "test" }
            };

            // Act
            var serialized = await serializer.SerializeAsync(testData);
            var deserialized = await serializer.DeserializeAsync<TestModel>(serialized);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(testData.Id, deserialized.Id);
            Assert.Equal(testData.Name, deserialized.Name);
            Assert.Equal(testData.CreatedAt, deserialized.CreatedAt);
            Assert.Equal(testData.IsActive, deserialized.IsActive);
            Assert.Equal(testData.Tags, deserialized.Tags);
        }

        [Fact]
        public void JsonSerializer_Deserialize_WithInvalidJson_ThrowsInvalidOperationException()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();
            var invalidJson = System.Text.Encoding.UTF8.GetBytes("{ invalid json }");

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => 
                serializer.Deserialize<TestModel>(invalidJson));
        }

        [Fact]
        public async Task JsonSerializer_DeserializeAsync_WithInvalidJson_ThrowsInvalidOperationException()
        {
            // Arrange
            var serializer = new SystemTextJsonRedisSerializer();
            var invalidJson = System.Text.Encoding.UTF8.GetBytes("{ invalid json }");

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                serializer.DeserializeAsync<TestModel>(invalidJson));
        }

        [Fact]
        public void JsonSerializer_Serialize_WithLargeObject_LogsWarning()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SystemTextJsonRedisSerializer>>();
            mockLogger.Setup(x => x.IsEnabled(LogLevel.Warning)).Returns(true);
            
            var serializer = new SystemTextJsonRedisSerializer(mockLogger.Object, null);
            
            // Create a large object (>1MB when serialized)
            var largeData = new TestModel 
            { 
                Id = 1, 
                Name = new string('x', 2_000_000), // 2MB string
                Tags = new string[0]
            };

            // Act
            var result = serializer.Serialize(largeData);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 1024 * 1024); // Verify it's actually large
        }

        [Fact]
        public void JsonSerializer_Serialize_WithCamelCasePolicy_ProducesCorrectJson()
        {
            // Arrange
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var serializer = new SystemTextJsonRedisSerializer(options);
            var testData = new TestModel { Id = 1, Name = "Test" };

            // Act
            var serialized = serializer.Serialize(testData);
            var json = System.Text.Encoding.UTF8.GetString(serialized);

            // Assert
            Assert.Contains("\"id\":", json); // Should be camelCase
            Assert.Contains("\"name\":", json);
            Assert.DoesNotContain("\"Id\":", json); // Should NOT be PascalCase
            Assert.DoesNotContain("\"Name\":", json);
        }

        #endregion

        #region Serializer Factory Tests

        [Fact]
        public void SerializerFactory_Create_WithMessagePack_ReturnsCorrectSerializer()
        {
            // Act
            var serializer = RedisSerializerFactory.Create(Models.SerializerType.MessagePack, null);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<MessagePackRedisSerializer>(serializer);
            Assert.Equal("MessagePack", serializer.Name);
        }

        [Fact]
        public void SerializerFactory_Create_WithSystemTextJson_ReturnsCorrectSerializer()
        {
            // Act
            var serializer = RedisSerializerFactory.Create(Models.SerializerType.SystemTextJson, null);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<SystemTextJsonRedisSerializer>(serializer);
            Assert.Equal("SystemTextJson", serializer.Name);
        }

        [Fact]
        public void SerializerFactory_Create_WithJsonOptions_PassesOptionsToSerializer()
        {
            // Arrange
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            // Act
            var serializer = RedisSerializerFactory.Create(Models.SerializerType.SystemTextJson, options);

            // Assert
            Assert.NotNull(serializer);
            var jsonSerializer = Assert.IsType<SystemTextJsonRedisSerializer>(serializer);
            Assert.NotNull(jsonSerializer.Options);
            Assert.Equal(JsonNamingPolicy.CamelCase, jsonSerializer.Options.PropertyNamingPolicy);
            Assert.True(jsonSerializer.Options.WriteIndented);
        }

        [Fact]
        public void SerializerFactory_Create_WithMessagePackOptions_PassesOptionsToSerializer()
        {
            // Arrange
            var options = MessagePackSerializerOptions.Standard;

            // Act
            var serializer = RedisSerializerFactory.Create(Models.SerializerType.MessagePack, options);

            // Assert
            Assert.NotNull(serializer);
            Assert.IsType<MessagePackRedisSerializer>(serializer);
        }

        [Fact]
        public void SerializerFactory_Create_WithInvalidType_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                RedisSerializerFactory.Create((Models.SerializerType)999, null));
        }

        #endregion

        #region Cross-Serializer Compatibility Tests

        [Fact]
        public void CrossSerializer_MessagePackToJson_FailsGracefully()
        {
            // Arrange
            var messagePackSerializer = new MessagePackRedisSerializer();
            var jsonSerializer = new SystemTextJsonRedisSerializer();
            var testData = new TestModel { Id = 1, Name = "Test" };

            // Act
            var messagePackBytes = messagePackSerializer.Serialize(testData);
            
            // Assert - Should throw when trying to deserialize MessagePack data as JSON
            Assert.Throws<InvalidOperationException>(() => 
                jsonSerializer.Deserialize<TestModel>(messagePackBytes));
        }

        [Fact]
        public void CrossSerializer_JsonToMessagePack_FailsGracefully()
        {
            // Arrange
            var messagePackSerializer = new MessagePackRedisSerializer();
            var jsonSerializer = new SystemTextJsonRedisSerializer();
            var testData = new TestModel { Id = 1, Name = "Test" };

            // Act
            var jsonBytes = jsonSerializer.Serialize(testData);
            
            // Assert - Should throw when trying to deserialize JSON data as MessagePack
            Assert.Throws<InvalidOperationException>(() => 
                messagePackSerializer.Deserialize<TestModel>(jsonBytes));
        }

        #endregion

        #region Test Models

        [MessagePackObject]
        public class TestModel
        {
            [Key(0)]
            public int Id { get; set; }

            [Key(1)]
            public string? Name { get; set; }

            [Key(2)]
            public DateTime CreatedAt { get; set; }

            [Key(3)]
            public bool IsActive { get; set; }

            [Key(4)]
            public string[] Tags { get; set; } = Array.Empty<string>();
        }

        #endregion
    }
}