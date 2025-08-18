using BenchmarkDotNet.Attributes;
using RedisKit.Serialization;

namespace RedisKit.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class SerializerBenchmarks
{
    private readonly SystemTextJsonRedisSerializer _jsonSerializer;
    private readonly TestData _largeObject;
    private readonly MessagePackRedisSerializer _messagePackSerializer;
    private readonly TestData[] _objectArray;
    private readonly TestData _smallObject;
    private byte[] _jsonData = Array.Empty<byte>();
    private byte[] _messagePackData = Array.Empty<byte>();

    public SerializerBenchmarks()
    {
        _jsonSerializer = new SystemTextJsonRedisSerializer();
        _messagePackSerializer = new MessagePackRedisSerializer();

        _smallObject = new TestData
        {
            Id = 123,
            Name = "John Doe",
            Email = "john.doe@example.com",
            Age = 30,
            IsActive = true,
            Tags = new[] { "user", "premium", "active" }
        };

        _largeObject = new TestData
        {
            Id = 456,
            Name = "Jane Smith",
            Email = "jane.smith@example.com",
            Age = 25,
            IsActive = true,
            Tags = Enumerable.Range(0, 100).Select(i => $"tag_{i}").ToArray(),
            Description = string.Join(" ", Enumerable.Range(0, 1000).Select(i => $"word_{i}"))
        };

        _objectArray = Enumerable.Range(0, 100)
            .Select(i => new TestData
            {
                Id = i,
                Name = $"User {i}",
                Email = $"user{i}@example.com",
                Age = 20 + i % 50,
                IsActive = i % 2 == 0,
                Tags = new[] { $"user_{i}", "batch" }
            })
            .ToArray();
    }

    [GlobalSetup]
    public void Setup()
    {
        _jsonData = _jsonSerializer.Serialize(_smallObject);
        _messagePackData = _messagePackSerializer.Serialize(_smallObject);
    }

    [Benchmark(Baseline = true)]
    public byte[] Json_Serialize_Small()
    {
        return _jsonSerializer.Serialize(_smallObject);
    }

    [Benchmark]
    public byte[] MessagePack_Serialize_Small()
    {
        return _messagePackSerializer.Serialize(_smallObject);
    }

    [Benchmark]
    public byte[] Json_Serialize_Large()
    {
        return _jsonSerializer.Serialize(_largeObject);
    }

    [Benchmark]
    public byte[] MessagePack_Serialize_Large()
    {
        return _messagePackSerializer.Serialize(_largeObject);
    }

    [Benchmark]
    public byte[] Json_Serialize_Array()
    {
        return _jsonSerializer.Serialize(_objectArray);
    }

    [Benchmark]
    public byte[] MessagePack_Serialize_Array()
    {
        return _messagePackSerializer.Serialize(_objectArray);
    }

    [Benchmark]
    public TestData? Json_Deserialize_Small()
    {
        return _jsonSerializer.Deserialize<TestData>(_jsonData);
    }

    [Benchmark]
    public TestData? MessagePack_Deserialize_Small()
    {
        return _messagePackSerializer.Deserialize<TestData>(_messagePackData);
    }

    [Benchmark]
    public async Task<byte[]> Json_SerializeAsync_Small()
    {
        return await _jsonSerializer.SerializeAsync(_smallObject);
    }

    [Benchmark]
    public async Task<byte[]> MessagePack_SerializeAsync_Small()
    {
        return await _messagePackSerializer.SerializeAsync(_smallObject);
    }

    [Benchmark]
    public async Task<TestData?> Json_DeserializeAsync_Small()
    {
        return await _jsonSerializer.DeserializeAsync<TestData>(_jsonData);
    }

    [Benchmark]
    public async Task<TestData?> MessagePack_DeserializeAsync_Small()
    {
        return await _messagePackSerializer.DeserializeAsync<TestData>(_messagePackData);
    }
}