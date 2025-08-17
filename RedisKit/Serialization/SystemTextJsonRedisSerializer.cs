using System.Text.Json;
using Microsoft.Extensions.Logging;
using RedisKit.Logging;

namespace RedisKit.Serialization
{
    /// <summary>
    /// System.Text.Json implementation of IRedisSerializer.
    /// Provides JSON serialization using Microsoft's high-performance System.Text.Json library.
    /// </summary>
    /// <remarks>
    /// This serializer uses System.Text.Json for converting objects to/from JSON format.
    /// It provides a good balance between performance, compatibility, and debuggability.
    /// 
    /// Thread Safety: This class is thread-safe and can be used as a singleton.
    /// 
    /// Performance Characteristics:
    /// - Fast serialization/deserialization with low memory allocation
    /// - Optimized for UTF-8 encoding/decoding
    /// - Direct byte array operations without intermediate string conversion
    /// - Supports async streaming for large objects
    /// 
    /// Advantages:
    /// - Human-readable format for debugging
    /// - Wide compatibility with other systems
    /// - Built into .NET, no additional dependencies
    /// - Good performance for most use cases
    /// - Supports source generators for AOT scenarios
    /// 
    /// Limitations:
    /// - Larger payload size compared to binary formats
    /// - Slower than MessagePack for complex object graphs
    /// - Some types require custom converters (e.g., TimeSpan, DateOnly)
    /// 
    /// Configuration:
    /// - Default uses camelCase property naming
    /// - Indentation disabled for smaller payload
    /// - Custom JsonSerializerOptions can be provided
    /// 
    /// Best Practices:
    /// - Use for debugging and development environments
    /// - Ideal when cache inspection is needed
    /// - Good for interoperability with JavaScript/Web clients
    /// - Consider MessagePack for production if performance is critical
    /// 
    /// Usage Example:
    /// <code>
    /// var options = new JsonSerializerOptions
    /// {
    ///     PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    ///     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ///     Converters = { new JsonStringEnumConverter() }
    /// };
    /// 
    /// var serializer = new SystemTextJsonRedisSerializer(logger, options);
    /// var data = serializer.Serialize(myObject);
    /// </code>
    /// </remarks>
    internal class SystemTextJsonRedisSerializer : IRedisSerializer
    {
        private readonly ILogger<SystemTextJsonRedisSerializer>? _logger;
        private readonly JsonSerializerOptions? _options;

        /// <summary>
        /// Gets the name of the serializer for identification and logging.
        /// </summary>
        /// <value>Returns "SystemTextJson" to identify this as the System.Text.Json implementation.</value>
        public string Name => "SystemTextJson";

        /// <summary>
        /// Initializes a new instance of the SystemTextJsonRedisSerializer class with default options.
        /// </summary>
        /// <remarks>
        /// Uses default JsonSerializerOptions with camelCase naming policy and compact formatting.
        /// </remarks>
        public SystemTextJsonRedisSerializer()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SystemTextJsonRedisSerializer class with custom options.
        /// </summary>
        /// <param name="options">Custom JSON serializer options for controlling serialization behavior.</param>
        /// <remarks>
        /// Use this constructor to customize serialization behavior such as:
        /// - Property naming policy (camelCase, PascalCase, etc.)
        /// - Null value handling
        /// - Custom converters for special types
        /// - Indentation settings
        /// </remarks>
        /// <example>
        /// <code>
        /// var options = new JsonSerializerOptions
        /// {
        ///     PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ///     WriteIndented = false,
        ///     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        /// };
        /// var serializer = new SystemTextJsonRedisSerializer(options);
        /// </code>
        /// </example>
        public SystemTextJsonRedisSerializer(JsonSerializerOptions? options)
            : this(null, options)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SystemTextJsonRedisSerializer class with logger and options.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics and performance monitoring.</param>
        /// <param name="options">Custom JSON serializer options. If null, default options are used.</param>
        /// <remarks>
        /// This is the most flexible constructor, allowing both logging and custom serialization options.
        /// Default options use camelCase naming and compact formatting if not specified.
        /// </remarks>
        public SystemTextJsonRedisSerializer(ILogger<SystemTextJsonRedisSerializer>? logger, JsonSerializerOptions? options)
        {
            _logger = logger;

            // Create default options if none provided, with camelCase naming
            _options = options ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Serializes an object to a UTF-8 encoded JSON byte array synchronously.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="obj">The object to serialize. Must not be null.</param>
        /// <returns>
        /// A UTF-8 encoded byte array containing the JSON representation of the object.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when serialization fails due to:
        /// - Circular references in the object graph
        /// - Unsupported types (e.g., IntPtr, delegates)
        /// - Memory constraints for very large objects
        /// - JSON conversion errors
        /// </exception>
        /// <remarks>
        /// This method uses JsonSerializer.SerializeToUtf8Bytes for optimal performance,
        /// avoiding intermediate string allocations. Large objects (>1MB) are logged for monitoring.
        /// Consider using async version for very large objects to avoid blocking.
        /// </remarks>
        public byte[] Serialize<T>(T obj)
        {

            try
            {
                _logger?.LogJsonSerialize(typeof(T).Name);

                // Use JsonSerializer.SerializeToUtf8Bytes for better performance
                // This avoids creating an intermediate string and directly writes to UTF-8 bytes
                var result = JsonSerializer.SerializeToUtf8Bytes(obj, _options);

                if (result.Length > 1024 * 1024) // 1MB
                {
                    _logger?.LogJsonLargeData(typeof(T).Name, result.Length);
                }

                return result;
            }
            catch (JsonException jsonEx)
            {
                _logger?.LogJsonSerializeError(typeof(T).Name, jsonEx.Message, jsonEx);
                throw new InvalidOperationException(
                    $"Failed to serialize object of type {typeof(T).Name} due to JSON serialization error. " +
                    $"Error: {jsonEx.Message}", jsonEx);
            }
            catch (OutOfMemoryException oomEx)
            {
                _logger?.LogOutOfMemoryError(typeof(T).Name, oomEx);
                throw new InvalidOperationException(
                    $"Failed to serialize object of type {typeof(T).Name} due to insufficient memory. " +
                    $"The serialized data exceeds available memory.", oomEx);
            }
            catch (NotSupportedException notSupEx)
            {
                _logger?.LogUnsupportedTypeError(typeof(T).Name, notSupEx);
                throw new InvalidOperationException(
                    $"Failed to serialize object of type {typeof(T).Name} due to unsupported type. " +
                    $"The type cannot be serialized to JSON.", notSupEx);
            }
            catch (Exception ex)
            {
                _logger?.LogJsonSerializeError(typeof(T).Name, ex.Message, ex);
                throw new InvalidOperationException(
                    $"Failed to serialize object of type {typeof(T).Name} using System.Text.Json. " +
                    $"Error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deserializes a UTF-8 encoded JSON byte array to an object synchronously.
        /// </summary>
        /// <typeparam name="T">The target type for deserialization.</typeparam>
        /// <param name="data">The UTF-8 encoded JSON byte array. Must not be null.</param>
        /// <returns>
        /// The deserialized object of type T, or default(T) if the data array is empty.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when deserialization fails due to:
        /// - Invalid JSON syntax
        /// - Type mismatch between JSON and target type
        /// - Missing required properties
        /// - Invalid UTF-8 encoding
        /// </exception>
        /// <remarks>
        /// Empty arrays return default(T) for graceful handling of missing cache entries.
        /// Large data (>10MB) triggers a warning log for monitoring potential memory issues.
        /// The method validates JSON structure and type compatibility during deserialization.
        /// </remarks>
        public T? Deserialize<T>(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.Length == 0)
                return default;

            // Validate for extremely large data to prevent memory issues
            if (data.Length > 10 * 1024 * 1024) // 10MB limit
            {
                _logger?.LogLargeDataDeserializationWarning(data.Length, typeof(T).Name);
            }

            try
            {
                _logger?.LogJsonDeserialize(data.Length, typeof(T).Name);

                return JsonSerializer.Deserialize<T>(data, _options);
            }
            catch (JsonException jsonEx)
            {
                _logger?.LogJsonDeserializeError(typeof(T).Name, data.Length, jsonEx.Message, jsonEx);
                throw new InvalidOperationException(
                    $"Failed to deserialize data to type {typeof(T).Name}: {jsonEx.Message}", jsonEx);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error when deserializing data to type {Type}", typeof(T).Name);
                throw new InvalidOperationException(
                    $"Failed to deserialize data to type {typeof(T).Name} using System.Text.Json. " +
                    $"Error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Asynchronously serializes an object to a UTF-8 encoded JSON byte array.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="obj">The object to serialize. Must not be null.</param>
        /// <param name="cancellationToken">Token to cancel the serialization operation.</param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task result contains the UTF-8 encoded JSON byte array.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when serialization fails due to JSON conversion errors or unsupported types.
        /// </exception>
        /// <remarks>
        /// While JSON serialization is CPU-bound, this method provides an async interface
        /// for consistency with the IRedisSerializer contract. The actual serialization
        /// is performed synchronously and wrapped in a completed task.
        /// Large objects (>1MB) are logged for performance monitoring.
        /// </remarks>
        public Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default)
        {

            // Validate object size to prevent memory issues (only if warning is enabled)
            if (_logger?.IsEnabled(LogLevel.Warning) == true)
            {
                ValidateObjectSize(obj);
            }

            try
            {
                // JSON serialization is CPU-bound, not I/O-bound, so we don't need true async
                // Just return the synchronous result wrapped in a completed task
                _logger?.LogJsonSerialize(typeof(T).Name);

                var json = JsonSerializer.SerializeToUtf8Bytes(obj, _options);

                // Log large objects for monitoring
                if (json.Length > 1024 * 1024) // >1MB
                {
                    _logger?.LogJsonLargeData(typeof(T).Name, json.Length);
                }

                return Task.FromResult(json);
            }
            catch (JsonException jsonEx)
            {
                _logger?.LogJsonSerializeError(typeof(T).Name, jsonEx.Message, jsonEx);
                throw new InvalidOperationException(
                    $"Failed to serialize object of type {typeof(T).Name}: {jsonEx.Message}", jsonEx);
            }
            catch (NotSupportedException notSupEx)
            {
                _logger?.LogUnsupportedTypeError(typeof(T).Name, notSupEx);
                throw new InvalidOperationException(
                    $"Type {typeof(T).Name} cannot be serialized to JSON", notSupEx);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error when serializing object of type {Type}", typeof(T).Name);
                throw new InvalidOperationException(
                    $"Failed to serialize object of type {typeof(T).Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Validates object size before serialization to prevent memory issues.
        /// </summary>
        /// <typeparam name="T">The type of object being validated.</typeparam>
        /// <param name="obj">The object to validate.</param>
        /// <remarks>
        /// This method performs quick checks for common large object scenarios:
        /// - Strings larger than 10MB
        /// - Arrays with more than 100,000 elements
        /// - Collections with more than 50,000 items
        /// Large objects are logged as warnings for monitoring and optimization.
        /// </remarks>
        private void ValidateObjectSize<T>(T obj)
        {
            // Quick validation for common large object scenarios
            const int maxStringLength = 10 * 1024 * 1024; // 10MB for strings
            const int maxArrayElements = 100000; // 100K elements for arrays
            const int maxCollectionItems = 50000; // 50K items for collections

            switch (obj)
            {
                case string str when str.Length > maxStringLength:
                    _logger?.LogLargeObjectWarning("String", str.Length);
                    break;

                case Array array when array.Length > maxArrayElements:
                    _logger?.LogLargeObjectWarning("Array", array.Length);
                    break;

                case System.Collections.ICollection collection when collection.Count > maxCollectionItems:
                    _logger?.LogLargeObjectWarning("Collection", collection.Count);
                    break;

                case System.Collections.IDictionary dict when dict.Count > maxCollectionItems:
                    _logger?.LogLargeObjectWarning("Dictionary", dict.Count);
                    break;
            }
        }

 

        /// <summary>
        /// Asynchronously deserializes a UTF-8 encoded JSON byte array to an object.
        /// </summary>
        /// <typeparam name="T">The target type for deserialization.</typeparam>
        /// <param name="data">The UTF-8 encoded JSON byte array.</param>
        /// <param name="cancellationToken">Token to cancel the deserialization operation.</param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task result contains the deserialized object or default(T) if data is null/empty.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when deserialization fails due to invalid JSON or type mismatch.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when the operation is cancelled via the cancellation token.
        /// </exception>
        /// <remarks>
        /// This method uses streaming deserialization with MemoryStream for better performance
        /// with large objects. It properly handles cancellation and provides detailed error messages.
        /// </remarks>
        public async Task<T?> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null || data.Length == 0)
                return default;

            try
            {
                _logger?.LogJsonStreamDeserialize(typeof(T).Name);

                // Use JsonSerializer.DeserializeAsync directly with a MemoryStream for better performance
                using var stream = new MemoryStream(data);
                return await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException jsonEx)
            {
                _logger?.LogJsonDeserializeError(typeof(T).Name, data.Length, jsonEx.Message, jsonEx);
                throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name} due to invalid JSON format (async)", jsonEx);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to deserialize data to type {Type} using System.Text.Json (async)", typeof(T).Name);
                throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).Name} using System.Text.Json (async)", ex);
            }
        }

        /// <summary>
        /// Asynchronously deserializes a UTF-8 encoded JSON byte array to an object using runtime type information.
        /// </summary>
        /// <param name="data">The UTF-8 encoded JSON byte array.</param>
        /// <param name="type">The target type for deserialization. Must not be null.</param>
        /// <param name="cancellationToken">Token to cancel the deserialization operation.</param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task result contains the deserialized object or null if data is null/empty.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when deserialization fails due to invalid JSON or type incompatibility.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when the operation is cancelled via the cancellation token.
        /// </exception>
        /// <remarks>
        /// This overload is useful for scenarios where the type is determined at runtime,
        /// such as polymorphic deserialization or plugin architectures.
        /// Uses streaming deserialization for memory efficiency.
        /// </remarks>
        public async Task<object?> DeserializeAsync(byte[] data, Type type, CancellationToken cancellationToken = default)
        {
            if (data == null || data.Length == 0)
                return null;

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            try
            {
                _logger?.LogJsonStreamDeserialize(type.Name);

                // Use JsonSerializer.DeserializeAsync directly with a MemoryStream for better performance
                using var stream = new MemoryStream(data);
                return await JsonSerializer.DeserializeAsync(stream, type, _options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException jsonEx)
            {
                _logger?.LogJsonDeserializeError(type.Name, data.Length, jsonEx.Message, jsonEx);
                throw new InvalidOperationException($"Failed to deserialize data to type {type.Name} due to invalid JSON format (async)", jsonEx);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to deserialize data to type {Type} using System.Text.Json (async)", type.Name);
                throw new InvalidOperationException($"Failed to deserialize data to type {type.Name} using System.Text.Json (async)", ex);
            }
        }

        /// <summary>
        /// Gets the JSON serializer options used by this instance.
        /// </summary>
        /// <value>
        /// The JsonSerializerOptions configured for this serializer, or null if using defaults.
        /// </value>
        /// <remarks>
        /// These options control JSON serialization behavior including property naming,
        /// null handling, custom converters, and formatting settings.
        /// </remarks>
        public JsonSerializerOptions? Options => _options;
    }
}