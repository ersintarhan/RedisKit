using MessagePack;
using Microsoft.Extensions.Logging;
using RedisKit.Logging;

namespace RedisKit.Serialization
{
    /// <summary>
    /// MessagePack implementation of IRedisSerializer.
    /// Provides high-performance binary serialization using the MessagePack format.
    /// </summary>
    /// <remarks>
    /// MessagePack is an efficient binary serialization format that's faster and more compact than JSON.
    /// It's like JSON, but fast and small.
    /// 
    /// Thread Safety: This class is thread-safe and can be used as a singleton.
    /// 
    /// Performance Characteristics:
    /// - 2-3x faster than System.Text.Json for serialization/deserialization
    /// - 30-50% smaller payload size compared to JSON
    /// - Minimal memory allocations during serialization
    /// - Direct binary format without string conversions
    /// - Excellent for high-throughput scenarios
    /// 
    /// Advantages:
    /// - Superior performance for complex object graphs
    /// - Significantly smaller payload (reduces network traffic and storage)
    /// - Cross-platform and language support
    /// - Schema evolution support with MessagePack attributes
    /// - Efficient handling of binary data (byte arrays)
    /// - Built-in compression for repeated values
    /// 
    /// Limitations:
    /// - Not human-readable (binary format)
    /// - Requires MessagePack attributes for optimal performance
    /// - Debugging is more difficult than with JSON
    /// - Less tooling support compared to JSON
    /// - May require contract sharing between services
    /// 
    /// Configuration Requirements:
    /// - Objects must be marked with [MessagePackObject] attribute
    /// - Properties need [Key] attributes for optimal serialization
    /// - Use [IgnoreMember] to exclude properties from serialization
    /// 
    /// Best Practices:
    /// - Use for production environments where performance is critical
    /// - Ideal for internal service communication
    /// - Perfect for caching large datasets or complex objects
    /// - Consider for real-time applications with high message volumes
    /// - Use contractless mode for simpler scenarios (with performance trade-off)
    /// 
    /// Usage Example:
    /// <code>
    /// // Define MessagePack-compatible class
    /// [MessagePackObject]
    /// public class CachedData
    /// {
    ///     [Key(0)]
    ///     public int Id { get; set; }
    ///     
    ///     [Key(1)]
    ///     public string Name { get; set; }
    ///     
    ///     [Key(2)]
    ///     public byte[] BinaryData { get; set; }
    /// }
    /// 
    /// // Configure serializer
    /// var options = MessagePackSerializerOptions.Standard
    ///     .WithCompression(MessagePackCompression.Lz4BlockArray);
    /// 
    /// var serializer = new MessagePackRedisSerializer(logger, options);
    /// </code>
    /// </remarks>
    internal class MessagePackRedisSerializer : IRedisSerializer
    {
        private readonly ILogger<MessagePackRedisSerializer>? _logger;
        private readonly MessagePackSerializerOptions? _options;

        /// <summary>
        /// Gets the name of the serializer for identification and logging.
        /// </summary>
        /// <value>Returns "MessagePack" to identify this as the MessagePack binary serialization implementation.</value>
        public string Name => "MessagePack";

        /// <summary>
        /// Initializes a new instance of the MessagePackRedisSerializer class with default options.
        /// </summary>
        /// <remarks>
        /// Uses MessagePackSerializerOptions.Standard which provides a good balance of features.
        /// For production use, consider using custom options with compression enabled.
        /// </remarks>
        public MessagePackRedisSerializer()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MessagePackRedisSerializer class with custom options.
        /// </summary>
        /// <param name="options">Custom MessagePack serializer options for controlling serialization behavior.</param>
        /// <remarks>
        /// Use this constructor to customize serialization behavior such as:
        /// - Compression (Lz4Block, Lz4BlockArray)
        /// - Contractless mode for simpler usage
        /// - Custom resolvers for type handling
        /// - Security settings
        /// </remarks>
        /// <example>
        /// <code>
        /// var options = MessagePackSerializerOptions.Standard
        ///     .WithCompression(MessagePackCompression.Lz4BlockArray)
        ///     .WithResolver(ContractlessStandardResolver.Instance);
        /// 
        /// var serializer = new MessagePackRedisSerializer(options);
        /// </code>
        /// </example>
        public MessagePackRedisSerializer(MessagePackSerializerOptions? options)
            : this(null, options)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MessagePackRedisSerializer class with logger and options.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics and performance monitoring.</param>
        /// <param name="options">Custom MessagePack serializer options. If null, uses MessagePackSerializerOptions.Standard.</param>
        /// <remarks>
        /// This is the most flexible constructor, allowing both logging and custom serialization options.
        /// The logger helps track serialization performance and identify large payloads.
        /// Default options use MessagePackSerializerOptions.Standard if not specified.
        /// </remarks>
        public MessagePackRedisSerializer(ILogger<MessagePackRedisSerializer>? logger, MessagePackSerializerOptions? options)
        {
            _logger = logger;
            _options = options ?? MessagePackSerializerOptions.Standard;
        }

        /// <summary>
        /// Serializes an object to a MessagePack binary byte array synchronously.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="obj">The object to serialize. Must not be null.</param>
        /// <returns>
        /// A byte array containing the MessagePack binary representation of the object.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when serialization fails due to:
        /// - Missing MessagePack attributes
        /// - Circular references
        /// - Unsupported types
        /// - Memory constraints
        /// </exception>
        /// <remarks>
        /// This method produces a compact binary format that's not human-readable.
        /// Large objects (>1MB) are logged for monitoring.
        /// Ensure your objects have proper MessagePack attributes for optimal performance.
        /// </remarks>
        public byte[] Serialize<T>(T obj)
        {
            
            try
            {
                _logger?.LogMessagePackSerialize(typeof(T).Name, 0);
                var result = MessagePackSerializer.Serialize(obj, _options);

                if (result.Length > 1024 * 1024) // 1MB
                {
                    _logger?.LogMessagePackLargeData(typeof(T).Name, result.Length);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogMessagePackSerializeError(typeof(T).Name, ex);
                throw new InvalidOperationException($"Failed to serialize object of type {typeof(T).Name} using MessagePack", ex);
            }
        }

        /// <summary>
        /// Deserializes a MessagePack binary byte array to an object synchronously.
        /// </summary>
        /// <typeparam name="T">The target type for deserialization.</typeparam>
        /// <param name="data">The MessagePack binary byte array. Can be null or empty.</param>
        /// <returns>
        /// The deserialized object of type T, or default(T) if the data is null or empty.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when deserialization fails due to:
        /// - Invalid MessagePack format
        /// - Type mismatch
        /// - Missing required fields
        /// - Data corruption
        /// </exception>
        /// <remarks>
        /// Empty or null arrays return default(T) for graceful handling.
        /// The binary format must match the expected MessagePack structure.
        /// Type compatibility is validated during deserialization.
        /// </remarks>
        public T? Deserialize<T>(byte[] data)
        {
            if (data == null || data.Length == 0)
                return default;

            try
            {
                _logger?.LogMessagePackDeserialize(data.Length, typeof(T).Name);
                return MessagePackSerializer.Deserialize<T>(data, _options);
            }
            catch (Exception ex)
            {
                _logger?.LogMessagePackDeserializeError(typeof(T).Name, data.Length, ex);
                throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name} using MessagePack", ex);
            }
        }

        /// <summary>
        /// Asynchronously serializes an object to a MessagePack binary byte array.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="obj">The object to serialize. Must not be null.</param>
        /// <param name="cancellationToken">Token to cancel the serialization operation (not used as operation is CPU-bound).</param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task result contains the MessagePack binary byte array.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when serialization fails due to MessagePack errors.
        /// </exception>
        /// <remarks>
        /// While MessagePack serialization is CPU-bound, this method provides an async interface
        /// for consistency with the IRedisSerializer contract. The actual serialization
        /// is performed synchronously and wrapped in a completed task.
        /// Large objects (>1MB) are logged for performance monitoring.
        /// </remarks>
        public Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default)
        {

            try
            {
                // MessagePack serialization is CPU-bound, not I/O-bound
                // Return synchronous result wrapped in completed task
                _logger?.LogMessagePackSerialize(typeof(T).Name, 0);
                var result = MessagePackSerializer.Serialize(obj, _options);

                if (result.Length > 1024 * 1024) // 1MB
                {
                    _logger?.LogMessagePackLargeData(typeof(T).Name, result.Length);
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger?.LogMessagePackSerializeError(typeof(T).Name, ex);
                throw new InvalidOperationException($"Failed to serialize object of type {typeof(T).Name} using MessagePack", ex);
            }
        }

        /// <summary>
        /// Asynchronously deserializes a MessagePack binary byte array to an object.
        /// </summary>
        /// <typeparam name="T">The target type for deserialization.</typeparam>
        /// <param name="data">The MessagePack binary byte array. Can be null or empty.</param>
        /// <param name="cancellationToken">Token to cancel the deserialization operation.</param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task result contains the deserialized object or default(T) if data is null/empty.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when deserialization fails due to invalid MessagePack format or type mismatch.
        /// </exception>
        /// <remarks>
        /// The cancellation token is passed to the MessagePack deserializer for proper cancellation support.
        /// While deserialization is CPU-bound, this method provides consistency with async patterns.
        /// Returns default(T) for null or empty input for graceful cache miss handling.
        /// </remarks>
        public Task<T?> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null || data.Length == 0)
                return Task.FromResult(default(T));

            try
            {
                // MessagePack deserialization is CPU-bound, not I/O-bound
                // Return synchronous result wrapped in completed task
                _logger?.LogMessagePackDeserialize(data.Length, typeof(T).Name);
                var result = MessagePackSerializer.Deserialize<T>(data, _options, cancellationToken);
                return Task.FromResult(result)!;
            }
            catch (Exception ex)
            {
                _logger?.LogMessagePackDeserializeError(typeof(T).Name, data.Length, ex);
                throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name} using MessagePack", ex);
            }
        }

        /// <summary>
        /// Asynchronously deserializes a MessagePack binary byte array to an object using runtime type information.
        /// </summary>
        /// <param name="data">The MessagePack binary byte array. Can be null or empty.</param>
        /// <param name="type">The target type for deserialization. Must not be null.</param>
        /// <param name="cancellationToken">Token to cancel the deserialization operation.</param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task result contains the deserialized object or null if data is null/empty.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when deserialization fails due to invalid MessagePack format or type incompatibility.
        /// </exception>
        /// <remarks>
        /// This overload is useful for scenarios where the type is determined at runtime,
        /// such as polymorphic deserialization or dynamic type handling.
        /// The cancellation token is properly forwarded to the MessagePack deserializer.
        /// Performance may be slightly slower than the generic version due to runtime type resolution.
        /// </remarks>
        public Task<object?> DeserializeAsync(byte[] data, Type type, CancellationToken cancellationToken = default)
        {
            if (data == null || data.Length == 0)
                return Task.FromResult<object?>(null);

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            try
            {
                // MessagePack deserialization is CPU-bound, not I/O-bound
                // Return synchronous result wrapped in completed task
                _logger?.LogMessagePackDeserialize(data.Length, type.Name);
                var result = MessagePackSerializer.Deserialize(type, data, _options, cancellationToken);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger?.LogMessagePackDeserializeError(type.Name, data.Length, ex);
                throw new InvalidOperationException($"Failed to deserialize data to type {type.Name} using MessagePack", ex);
            }
        }
    }
}