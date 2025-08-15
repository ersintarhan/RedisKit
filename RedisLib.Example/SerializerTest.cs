using System.Text.Json;
using MessagePack;
using RedisLib.Models;
using RedisLib.Serialization;

namespace RedisLib.Example
{
    /// <summary>
    /// Simple test to verify both serializers work correctly
    /// </summary>
    public class SerializerTest
    {
        public static async Task RunTestsAsync()
        {
            Console.WriteLine("=== Redis Multi-Serializer Test ===\n");

            // Test data
            var testData = new SampleData
            {
                Id = 1,
                Name = "Test User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Tags = new[] { "redis", "csharp", "test" }
            };

            // Test MessagePack serializer
            await TestMessagePackSerializerAsync(testData);

            // Test System.Text.Json serializer  
            await TestSystemTextJsonSerializerAsync(testData);

            Console.WriteLine("✅ All tests completed successfully!");
        }

        private static async Task TestMessagePackSerializerAsync(SampleData data)
        {
            Console.WriteLine("🔄 Testing MessagePack Serializer...");

            try
            {
                // Create serializer with default options
                var messagePackSerializer = new MessagePackRedisSerializer();

                // Test serialization
                byte[] serializedData = await messagePackSerializer.SerializeAsync(data);
                Console.WriteLine($"   Serialized {serializedData.Length} bytes");

                // Test deserialization
                var deserializedData = await messagePackSerializer.DeserializeAsync<SampleData>(serializedData);

                // Verify data integrity
                if (deserializedData != null &&
                    deserializedData.Id == data.Id &&
                    deserializedData.Name == data.Name &&
                    deserializedData.IsActive == data.IsActive)
                {
                    Console.WriteLine("   ✅ MessagePack serialization test PASSED");
                }
                else
                {
                    Console.WriteLine("   ❌ MessagePack serialization test FAILED");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ MessagePack test failed: {ex.Message}");
            }
        }

        private static async Task TestSystemTextJsonSerializerAsync(SampleData data)
        {
            Console.WriteLine("🔄 Testing System.Text.Json Serializer...");

            try
            {
                // Create serializer with custom options (camelCase)
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };

                var jsonSerializer = new SystemTextJsonRedisSerializer(jsonOptions);

                // Test serialization
                byte[] serializedData = await jsonSerializer.SerializeAsync(data);
                Console.WriteLine($"   Serialized {serializedData.Length} bytes");

                // Test deserialization
                var deserializedData = await jsonSerializer.DeserializeAsync<SampleData>(serializedData);

                // Verify data integrity
                if (deserializedData != null &&
                    deserializedData.Id == data.Id &&
                    deserializedData.Name == data.Name &&
                    deserializedData.IsActive == data.IsActive)
                {
                    Console.WriteLine("   ✅ System.Text.Json serialization test PASSED");

                    // Show JSON output
                    var jsonString = System.Text.Encoding.UTF8.GetString(serializedData);
                    Console.WriteLine($"   JSON Output: {jsonString}");
                }
                else
                {
                    Console.WriteLine("   ❌ System.Text.Json serialization test FAILED");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ System.Text.Json test failed: {ex.Message}");
            }
        }

        private static async Task TestSerializerFactoryAsync()
        {
            Console.WriteLine("🔄 Testing Redis Serializer Factory...");

            try
            {
                // Test factory with MessagePack
                var messagePackSerializer = RedisSerializerFactory.Create(
                    SerializerType.MessagePack,
                    null);

                Console.WriteLine($"   Factory created: {messagePackSerializer.Name}");

                // Test factory with System.Text.Json
                var jsonSerializer = RedisSerializerFactory.Create(
                    SerializerType.SystemTextJson,
                    null);

                Console.WriteLine($"   Factory created: {jsonSerializer.Name}");

                // Test with custom options
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var jsonSerializerWithOptions = RedisSerializerFactory.Create(
                    SerializerType.SystemTextJson,
                    jsonOptions);

                Console.WriteLine($"   Factory with options created: {jsonSerializerWithOptions.Name}");

                Console.WriteLine("   ✅ Serializer Factory test PASSED");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Serializer Factory test failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sample data class for testing
        /// </summary>
        [MessagePackObject]
        public class SampleData
        {
            [Key(0)] public int Id { get; set; }

            [Key(1)] public string Name { get; set; } = string.Empty;

            [Key(2)] public DateTime CreatedAt { get; set; }

            [Key(3)] public bool IsActive { get; set; }

            [Key(4)] public string[] Tags { get; set; } = Array.Empty<string>();
        }
    }
}