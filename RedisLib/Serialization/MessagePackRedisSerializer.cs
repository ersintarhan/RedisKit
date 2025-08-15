using MessagePack;
using Microsoft.Extensions.Logging;
using RedisLib.Logging;

namespace RedisLib.Serialization
{
    /// <summary>
    /// MessagePack implementation of IRedisSerializer
    /// </summary>
    internal class MessagePackRedisSerializer : IRedisSerializer
    {
        private readonly ILogger<MessagePackRedisSerializer>? _logger;
        private readonly MessagePackSerializerOptions? _options;

        /// <summary>
        /// Gets the name of the serializer
        /// </summary>
        public string Name => "MessagePack";

        /// <summary>
        /// Initializes a new instance of the MessagePackRedisSerializer class
        /// </summary>
        public MessagePackRedisSerializer()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MessagePackRedisSerializer class with custom options
        /// </summary>
        /// <param name="options">MessagePack serializer options</param>
        public MessagePackRedisSerializer(MessagePackSerializerOptions? options)
            : this(null, options)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MessagePackRedisSerializer class with logger and options
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="options">MessagePack serializer options</param>
        public MessagePackRedisSerializer(ILogger<MessagePackRedisSerializer>? logger, MessagePackSerializerOptions? options)
        {
            _logger = logger;
            _options = options ?? MessagePackSerializerOptions.Standard;
        }

        /// <summary>
        /// Serializes an object to byte array (synchronous)
        /// </summary>
        public byte[] Serialize<T>(T obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

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
        /// Deserializes a byte array to an object (synchronous)
        /// </summary>
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
        /// Serializes an object to byte array (asynchronous)
        /// </summary>
        public Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

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
        /// Deserializes a byte array to an object (asynchronous)
        /// </summary>
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
        /// Deserializes a byte array to an object using runtime type (asynchronous)
        /// </summary>
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