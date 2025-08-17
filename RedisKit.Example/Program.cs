using System.Text.Json;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedisKit.Extensions;
using RedisKit.Interfaces;
using RedisKit.Models;

namespace TestMultiSerializer;

[MessagePackObject]
public class TestData
{
    [Key(0)] public int Id { get; set; }

    [Key(1)] public string Name { get; set; } = string.Empty;

    [Key(2)] public DateTime Created { get; set; }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Testing Multi-Serializer Support\n");
        Console.WriteLine("=================================\n");

        // Test 1: MessagePack (default)
        Console.WriteLine("Test 1: MessagePack Serializer (Default)");
        var services1 = new ServiceCollection();
        services1.AddLogging(builder => builder.AddConsole());
        services1.AddRedisServices(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.Serializer = SerializerType.MessagePack;
        });

        var provider1 = services1.BuildServiceProvider();
        var cache1 = provider1.GetRequiredService<IRedisCacheService>();

        try
        {
            var testData = new TestData { Id = 1, Name = "MessagePack Test", Created = DateTime.Now };
            await cache1.SetAsync("test:mp", testData, TimeSpan.FromMinutes(1));
            var retrieved = await cache1.GetAsync<TestData>("test:mp");
            Console.WriteLine($"✅ MessagePack: Stored and retrieved: {retrieved?.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ MessagePack Error: {ex.Message}");
        }

        // Test 2: System.Text.Json
        Console.WriteLine("\nTest 2: System.Text.Json Serializer");
        var services2 = new ServiceCollection();
        services2.AddLogging(builder => builder.AddConsole());
        services2.AddRedisServices(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.Serializer = SerializerType.SystemTextJson;
            options.JsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        });

        var provider2 = services2.BuildServiceProvider();
        var cache2 = provider2.GetRequiredService<IRedisCacheService>();

        try
        {
            var testData = new TestData { Id = 2, Name = "JSON Test", Created = DateTime.Now };
            await cache2.SetAsync("test:json", testData, TimeSpan.FromMinutes(1));
            var retrieved = await cache2.GetAsync<TestData>("test:json");
            Console.WriteLine($"✅ System.Text.Json: Stored and retrieved: {retrieved?.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ System.Text.Json Error: {ex.Message}");
        }

        // Test 3: Cross-serializer compatibility (write with MP, read with JSON should fail gracefully)
        Console.WriteLine("\nTest 3: Cross-Serializer Compatibility");
        try
        {
            var mpData = await cache2.GetAsync<TestData>("test:mp"); // Try to read MP data with JSON
            Console.WriteLine($"❌ Should not be able to read MP data with JSON: {mpData?.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✅ Expected incompatibility error: {ex.GetType().Name}");
        }

        // Test 4: Streams with different serializers
        Console.WriteLine("\nTest 4: Streams with Serializers");
        var stream1 = provider1.GetRequiredService<IRedisStreamService>();
        var stream2 = provider2.GetRequiredService<IRedisStreamService>();
        try
        {
            var msgId1 = await stream1.AddAsync("stream:mp", new TestData { Id = 3, Name = "Stream MP" });
            Console.WriteLine($"✅ Stream with MessagePack: Added message {msgId1}");

            var msgId2 = await stream2.AddAsync("stream:json", new TestData { Id = 4, Name = "Stream JSON" });
            Console.WriteLine($"✅ Stream with JSON: Added message {msgId2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Stream Error: {ex.Message}");
        }

        Console.WriteLine("\n=================================");
        Console.WriteLine("Multi-Serializer Testing Complete!");
    }
}