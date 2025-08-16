using System.Text.Json;
using Microsoft.Extensions.Logging;
using RedisKit.Logging;

namespace RedisKit.Serialization
{
    /// <summary>
    /// System.Text.Json implementation of IRedisSerializer
    /// </summary>
    internal class SystemTextJsonRedisSerializer : IRedisSerializer
    {
        private readonly ILogger<SystemTextJsonRedisSerializer>? _logger;
        private readonly JsonSerializerOptions? _options;

        /// <summary>
        /// Gets the name of the serializer
        /// </summary>
        public string Name => "SystemTextJson";

        /// <summary>
        /// Initializes a new instance of the SystemTextJsonRedisSerializer class
        /// </summary>
        public SystemTextJsonRedisSerializer()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SystemTextJsonRedisSerializer class with custom options
        /// </summary>
        /// <param name="options">JSON serializer options</param>
        public SystemTextJsonRedisSerializer(JsonSerializerOptions? options)
            : this(null, options)
        {
        }

        /// <summary>
        /// Initializes a new instance of the SystemTextJsonRedisSerializer class with logger and options
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="options">JSON serializer options</param>
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
        /// Serializes an object to byte array (synchronous)
        /// </summary>
        /// <typeparam name="T">The type of object to serialize</typeparam>
        /// <param name="obj">The object to serialize</param>
        /// <returns>A UTF-8 encoded byte array containing the serialized JSON representation of the object</param>
        /// <exception cref="ArgumentNullException">Thrown when obj is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when serialization fails due to JSON conversion errors or memory constraints</exception>
        public byte[] Serialize<T>(T obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

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
        /// Deserializes a byte array to an object (synchronous)
        /// </summary>
        /// <typeparam name="T">The type to deserialize to</typeparam>
        /// <param name="data">The byte array containing JSON data</param>
        /// <returns>The deserialized object of type T, or default(T) if the data is null/empty</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null</exception>
        /// <exception cref="ArgumentException">Thrown when data array is empty or contains invalid UTF-8</exception>
        /// <exception cref="InvalidOperationException">Thrown when deserialization fails due to invalid JSON or type mismatch</exception>
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
        /// Serializes an object to byte array (asynchronous)
        /// </summary>
        /// <typeparam name="T">The type of object to serialize</typeparam>
        /// <param name="obj">The object to serialize</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>A UTF-8 encoded byte array containing the serialized JSON representation of the object</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when serialization fails due to JSON conversion errors or memory constraints</exception>
        public Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

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
        /// Helper method to validate object size before serialization
        /// </summary>
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
        /// Helper method to create consistent error messages
        /// </summary>
        private string CreateErrorMessage<T>(string operation, Exception ex)
        {
            return $"Failed to {operation} object of type {typeof(T).Name} using System.Text.Json. " +
                   $"Error: {ex.Message}";
        }

        /// <summary>
        /// Deserializes a byte array to an object (asynchronous)
        /// </summary>
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
        /// Deserializes a byte array to an object using runtime type (asynchronous)
        /// </summary>
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
        /// Gets or sets the JSON serializer options
        /// </summary>
        public JsonSerializerOptions? Options => _options;
    }
}