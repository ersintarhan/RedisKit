using NSubstitute;
using RedisKit.Helpers;
using RedisKit.Serialization;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests.Helpers;

public class StreamSerializationHelperTests
{
    private readonly IRedisSerializer _serializer;

    public StreamSerializationHelperTests()
    {
        _serializer = Substitute.For<IRedisSerializer>();
    }

    [Fact]
    public void CreateDataField_WithValidData_CreatesNameValueEntry()
    {
        // Arrange
        var data = new TestData { Id = "1", Name = "Test", Value = 42 };
        var serializedData = new byte[] { 1, 2, 3, 4 };
        _serializer.Serialize(data).Returns(serializedData);

        // Act
        var result = StreamSerializationHelper.CreateDataField(data, _serializer);

        // Assert
        Assert.Equal("data", result.Name);
        Assert.Equal(serializedData, (byte[]?)result.Value);
        _serializer.Received(1).Serialize(data);
    }

    [Fact]
    public void CreateDataFields_WithMultipleMessages_CreatesArrayOfEntries()
    {
        // Arrange
        var messages = new[]
        {
            new TestData { Id = "1", Name = "Test1", Value = 1 },
            new TestData { Id = "2", Name = "Test2", Value = 2 },
            new TestData { Id = "3", Name = "Test3", Value = 3 }
        };

        _serializer.Serialize(Arg.Any<TestData>()).Returns(x => new[] { (byte)((TestData)x[0]).Value });

        // Act
        var result = StreamSerializationHelper.CreateDataFields(messages, _serializer);

        // Assert
        Assert.Equal(3, result.Length);
        for (var i = 0; i < messages.Length; i++)
        {
            Assert.Equal("data", result[i].Name);
            var expectedBytes = new[] { (byte)messages[i].Value };
            Assert.Equal(expectedBytes, (byte[]?)result[i].Value);
        }

        _serializer.Received(3).Serialize(Arg.Any<TestData>());
    }

    [Fact]
    public void DeserializeStreamEntry_WithValidEntry_ReturnsDeserializedData()
    {
        // Arrange
        var expectedData = new TestData { Id = "1", Name = "Test", Value = 42 };
        var serializedData = new byte[] { 1, 2, 3, 4 };

        var entry = new StreamEntry(
            "123-0",
            new[] { new NameValueEntry("data", serializedData) });

        _serializer.Deserialize<TestData>(serializedData).Returns(expectedData);

        // Act
        var result = StreamSerializationHelper.DeserializeStreamEntry<TestData>(entry, _serializer);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedData.Id, result.Id);
        Assert.Equal(expectedData.Name, result.Name);
        Assert.Equal(expectedData.Value, result.Value);
        _serializer.Received(1).Deserialize<TestData>(serializedData);
    }

    [Fact]
    public void DeserializeStreamEntry_WithNoDataField_ReturnsNull()
    {
        // Arrange
        var entry = new StreamEntry(
            "123-0",
            new[] { new NameValueEntry("other", "value") });

        // Act
        var result = StreamSerializationHelper.DeserializeStreamEntry<TestData>(entry, _serializer);

        // Assert
        Assert.Null(result);
        _serializer.DidNotReceive().Deserialize<TestData>(Arg.Any<byte[]>());
    }

    [Fact]
    public void DeserializeStreamEntry_WithNullDataField_ReturnsNull()
    {
        // Arrange
        var entry = new StreamEntry(
            "123-0",
            new[] { new NameValueEntry("data", RedisValue.Null) });

        // Act
        var result = StreamSerializationHelper.DeserializeStreamEntry<TestData>(entry, _serializer);

        // Assert
        Assert.Null(result);
        _serializer.DidNotReceive().Deserialize<TestData>(Arg.Any<byte[]>());
    }

    [Fact]
    public void DeserializeStreamEntries_WithMultipleEntries_ReturnsListOfData()
    {
        // Arrange
        var entries = new[]
        {
            new StreamEntry("123-0", new[] { new NameValueEntry("data", new byte[] { 1 }) }),
            new StreamEntry("124-0", new[] { new NameValueEntry("data", new byte[] { 2 }) }),
            new StreamEntry("125-0", new[] { new NameValueEntry("data", new byte[] { 3 }) })
        };

        _serializer.Deserialize<TestData>(Arg.Any<byte[]>()).Returns(x => new TestData { Id = ((byte[])x[0])[0].ToString(), Value = ((byte[])x[0])[0] });

        // Act
        var result = StreamSerializationHelper.DeserializeStreamEntries<TestData>(entries, _serializer);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("123-0", result[0].id);
        Assert.Equal("1", result[0].data.Id);
        Assert.Equal(1, result[0].data.Value);
        _serializer.Received(3).Deserialize<TestData>(Arg.Any<byte[]>());
    }

    [Fact]
    public void DeserializeStreamEntries_WithNullData_SkipsEntry()
    {
        // Arrange
        var entries = new[]
        {
            new StreamEntry("123-0", new[] { new NameValueEntry("data", new byte[] { 1 }) }),
            new StreamEntry("124-0", new[] { new NameValueEntry("other", "value") }),
            new StreamEntry("125-0", new[] { new NameValueEntry("data", new byte[] { 3 }) })
        };

        _serializer.Deserialize<TestData>(Arg.Any<byte[]>()).Returns(x => new TestData { Id = ((byte[])x[0])[0].ToString() });

        // Act
        var result = StreamSerializationHelper.DeserializeStreamEntries<TestData>(entries, _serializer);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("123-0", result[0].id);
        Assert.Equal("125-0", result[1].id);
    }

    [Fact]
    public void GetDataFieldValue_WithDataField_ReturnsValue()
    {
        // Arrange
        var dataValue = new byte[] { 1, 2, 3 };
        var entry = new StreamEntry(
            "123-0",
            new[] { new NameValueEntry("data", dataValue) });

        // Act
        var result = StreamSerializationHelper.GetDataFieldValue(entry);

        // Assert
        Assert.True(result.HasValue);
        Assert.Equal(dataValue, (byte[]?)result);
    }

    [Fact]
    public void GetDataFieldValue_WithoutDataField_ReturnsNull()
    {
        // Arrange
        var entry = new StreamEntry(
            "123-0",
            new[] { new NameValueEntry("other", "value") });

        // Act
        var result = StreamSerializationHelper.GetDataFieldValue(entry);

        // Assert
        Assert.False(result.HasValue);
        Assert.True(result.IsNull);
    }

    [Fact]
    public void GetDataFieldValue_WithMultipleFields_ReturnsDataField()
    {
        // Arrange
        var dataValue = new byte[] { 1, 2, 3 };
        var entry = new StreamEntry(
            "123-0",
            new[]
            {
                new NameValueEntry("field1", "value1"),
                new NameValueEntry("data", dataValue),
                new NameValueEntry("field2", "value2")
            });

        // Act
        var result = StreamSerializationHelper.GetDataFieldValue(entry);

        // Assert
        Assert.True(result.HasValue);
        Assert.Equal(dataValue, (byte[]?)result);
    }

    [Fact]
    public void CreateMetadataField_WithValidMetadata_CreatesNameValueEntry()
    {
        // Arrange
        var metadata = new { UserId = "123", Timestamp = DateTime.UtcNow };
        var serializedMetadata = new byte[] { 5, 6, 7, 8 };
        _serializer.Serialize(metadata).Returns(serializedMetadata);

        // Act
        var result = StreamSerializationHelper.CreateMetadataField(metadata, _serializer);

        // Assert
        Assert.Equal("metadata", result.Name);
        Assert.Equal(serializedMetadata, (byte[])result.Value!);
        _serializer.Received(1).Serialize(metadata);
    }

    [Fact]
    public void CreateFieldsWithMetadata_CreatesDataAndMetadataFields()
    {
        // Arrange
        var data = new TestData { Id = "1", Name = "Test", Value = 42 };
        var metadata = new { UserId = "123", Timestamp = DateTime.UtcNow };
        var serializedData = new byte[] { 1, 2, 3, 4 };
        var serializedMetadata = new byte[] { 5, 6, 7, 8 };

        _serializer.Serialize(data).Returns(serializedData);
        _serializer.Serialize(metadata).Returns(serializedMetadata);

        // Act
        var result = StreamSerializationHelper.CreateFieldsWithMetadata(data, metadata, _serializer);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("data", result[0].Name);
        Assert.Equal(serializedData, (byte[])result[0].Value!);
        Assert.Equal("metadata", result[1].Name);
        Assert.Equal(serializedMetadata, (byte[])result[1].Value!);
    }

    [Fact]
    public async Task BatchSerializeAsync_WithSmallBatch_ProcessesAllMessages()
    {
        // Arrange
        var messages = new[]
        {
            new TestData { Id = "1", Name = "Test1", Value = 1 },
            new TestData { Id = "2", Name = "Test2", Value = 2 },
            new TestData { Id = "3", Name = "Test3", Value = 3 }
        };

        _serializer.Serialize(Arg.Any<TestData>()).Returns(x => new[] { (byte)((TestData)x[0]).Value });

        // Act
        var result = await StreamSerializationHelper.BatchSerializeAsync(messages, _serializer, 10);

        // Assert
        Assert.Equal(3, result.Length);
        for (var i = 0; i < messages.Length; i++)
        {
            Assert.Single(result[i]);
            Assert.Equal("data", result[i][0].Name);
            var expectedBytes = new[] { (byte)messages[i].Value };
            Assert.Equal(expectedBytes, (byte[]?)result[i][0].Value);
        }
    }

    [Fact]
    public async Task BatchSerializeAsync_WithLargeBatch_ProcessesInBatches()
    {
        // Arrange
        var messages = new TestData[250];
        for (var i = 0; i < messages.Length; i++) messages[i] = new TestData { Id = i.ToString(), Name = $"Test{i}", Value = i };

        _serializer.Serialize(Arg.Any<TestData>()).Returns(x => new[] { (byte)((TestData)x[0]).Value });

        // Act
        var result = await StreamSerializationHelper.BatchSerializeAsync(messages, _serializer);

        // Assert
        Assert.Equal(250, result.Length);
        for (var i = 0; i < messages.Length; i++)
        {
            Assert.Single(result[i]);
            Assert.Equal("data", result[i][0].Name);
        }

        // Should be called 250 times (once per message)
        _serializer.Received(250).Serialize(Arg.Any<TestData>());
    }

    [Fact]
    public async Task BatchSerializeAsync_WithEmptyArray_ReturnsEmptyResult()
    {
        // Arrange
        var messages = Array.Empty<TestData>();

        // Act
        var result = await StreamSerializationHelper.BatchSerializeAsync(messages, _serializer);

        // Assert
        Assert.Empty(result);
        _serializer.DidNotReceive().Serialize(Arg.Any<TestData>());
    }

    public class TestData
    {
        public string Id { get; set; } = default!;
        public string Name { get; set; } = default!;
        public int Value { get; set; }
    }
}