using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RedisKit.Serialization
{
    /// <summary>
    /// Factory for creating Redis serializers with support for custom serializers and thread-safe singleton instances
    /// </summary>
    public static class RedisSerializerFactory
    {
        private static readonly ConcurrentDictionary<Type, object> _customSerializers = new();
        private static readonly ConcurrentDictionary<string, IRedisSerializer> _serializerCache = new();

        /// <summary>
        /// Creates a serializer instance based on the specified type
        /// </summary>
        /// <param name="serializerType">The type of serializer to create</param>
        /// <param name="loggerFactory">Optional logger factory for logging</param>
        /// <returns>An instance of IRedisSerializer</returns>
        public static IRedisSerializer Create(Models.SerializerType serializerType, ILoggerFactory? loggerFactory = null)
        {
            var cacheKey = $"{serializerType}";

            return _serializerCache.GetOrAdd(cacheKey, key =>
            {
                return serializerType switch
                {
                    Models.SerializerType.MessagePack =>
                        new MessagePackRedisSerializer(
                            loggerFactory?.CreateLogger<MessagePackRedisSerializer>(),
                            null),

                    Models.SerializerType.SystemTextJson =>
                        new SystemTextJsonRedisSerializer(
                            loggerFactory?.CreateLogger<SystemTextJsonRedisSerializer>(),
                            null),

                    Models.SerializerType.Custom =>
                        throw new ArgumentException("Custom serializer type requires specifying the custom serializer implementation", nameof(serializerType)),

                    _ => throw new ArgumentOutOfRangeException(nameof(serializerType), $"Unknown serializer type: {serializerType}")
                };
            });
        }

        /// <summary>
        /// Creates a serializer instance with custom options using pattern matching
        /// </summary>
        /// <param name="serializerType">The type of serializer to create</param>
        /// <param name="options">Serializer-specific options</param>
        /// <param name="loggerFactory">Optional logger factory for logging</param>
        /// <returns>An instance of IRedisSerializer</returns>
        public static IRedisSerializer Create<TOptions>(
            Models.SerializerType serializerType,
            TOptions options,
            ILoggerFactory? loggerFactory = null) where TOptions : class
        {
            var cacheKey = $"{serializerType}_{options.GetHashCode()}";

            return _serializerCache.GetOrAdd(cacheKey, key =>
            {
                // Using modern pattern matching with switch expression
                return (serializerType, options) switch
                {
                    (Models.SerializerType.MessagePack, MessagePack.MessagePackSerializerOptions messagePackOptions) =>
                        new MessagePackRedisSerializer(
                            loggerFactory?.CreateLogger<MessagePackRedisSerializer>(),
                            messagePackOptions),

                    (Models.SerializerType.SystemTextJson, System.Text.Json.JsonSerializerOptions jsonOptions) =>
                        new SystemTextJsonRedisSerializer(
                            loggerFactory?.CreateLogger<SystemTextJsonRedisSerializer>(),
                            jsonOptions),

                    (Models.SerializerType.MessagePack, _) =>
                        throw new ArgumentException($"Invalid options type for MessagePack serializer. Expected {nameof(MessagePack.MessagePackSerializerOptions)}, got {options.GetType().Name}", nameof(options)),

                    (Models.SerializerType.SystemTextJson, _) =>
                        throw new ArgumentException($"Invalid options type for System.Text.Json serializer. Expected {nameof(System.Text.Json.JsonSerializerOptions)}, got {options.GetType().Name}", nameof(options)),

                    (Models.SerializerType.Custom, _) =>
                        throw new ArgumentException("Custom serializer type requires using CreateCustom method", nameof(serializerType)),

                    _ => throw new ArgumentOutOfRangeException(nameof(serializerType), $"Unknown serializer type: {serializerType}")
                };
            });
        }

        /// <summary>
        /// Alternative method using if-else pattern matching for better readability in some cases
        /// </summary>
        public static IRedisSerializer CreateWithOptions(
            Models.SerializerType serializerType,
            object? options = null,
            ILoggerFactory? loggerFactory = null)
        {
            if (options == null)
            {
                return Create(serializerType, loggerFactory);
            }

            var cacheKey = $"{serializerType}_{options.GetHashCode()}";

            return _serializerCache.GetOrAdd(cacheKey, key =>
            {
                // Using if-else pattern matching as suggested
                if (serializerType == Models.SerializerType.MessagePack)
                {
                    if (options is MessagePack.MessagePackSerializerOptions messagePackOptions)
                    {
                        return new MessagePackRedisSerializer(
                            loggerFactory?.CreateLogger<MessagePackRedisSerializer>(),
                            messagePackOptions);
                    }
                    throw new ArgumentException($"Invalid options type for MessagePack serializer. Expected {nameof(MessagePack.MessagePackSerializerOptions)}, got {options.GetType().Name}", nameof(options));
                }
                else if (serializerType == Models.SerializerType.SystemTextJson)
                {
                    if (options is System.Text.Json.JsonSerializerOptions jsonOptions)
                    {
                        return new SystemTextJsonRedisSerializer(
                            loggerFactory?.CreateLogger<SystemTextJsonRedisSerializer>(),
                            jsonOptions);
                    }
                    throw new ArgumentException($"Invalid options type for System.Text.Json serializer. Expected {nameof(System.Text.Json.JsonSerializerOptions)}, got {options.GetType().Name}", nameof(options));
                }
                else if (serializerType == Models.SerializerType.Custom)
                {
                    throw new ArgumentException("Custom serializer type requires using CreateCustom method", nameof(serializerType));
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(serializerType), $"Unknown serializer type: {serializerType}");
                }
            });
        }

        /// <summary>
        /// Creates a custom serializer instance
        /// </summary>
        /// <param name="serializerType">The type of the custom serializer</param>
        /// <param name="loggerFactory">Optional logger factory for logging</param>
        /// <returns>An instance of IRedisSerializer</returns>
        public static IRedisSerializer CreateCustom(Type serializerType, ILoggerFactory? loggerFactory = null)
        {
            if (!typeof(IRedisSerializer).IsAssignableFrom(serializerType))
            {
                throw new ArgumentException($"Type {serializerType.Name} must implement IRedisSerializer interface", nameof(serializerType));
            }

            var cacheKey = $"Custom_{serializerType.FullName}";

            return (IRedisSerializer)_customSerializers.GetOrAdd(serializerType, type =>
            {
                try
                {
                    var instance = Activator.CreateInstance(type);

                    // Using pattern matching instead of cast
                    return instance switch
                    {
                        null => throw new InvalidOperationException($"Failed to create instance of type {type.Name}"),
                        IRedisSerializer serializer => serializer,
                        _ => throw new InvalidOperationException($"Type {type.Name} must implement IRedisSerializer interface")
                    };
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    throw new InvalidOperationException($"Failed to create custom serializer of type {serializerType.Name}", ex);
                }
            });
        }

        /// <summary>
        /// Creates a custom serializer instance with specific options
        /// </summary>
        /// <param name="serializerType">The type of the custom serializer</param>
        /// <param name="options">Options for the serializer</param>
        /// <param name="loggerFactory">Optional logger factory for logging</param>
        /// <returns>An instance of IRedisSerializer</returns>
        public static IRedisSerializer CreateCustom(Type serializerType, object options, ILoggerFactory? loggerFactory = null)
        {
            if (!typeof(IRedisSerializer).IsAssignableFrom(serializerType))
            {
                throw new ArgumentException($"Type {serializerType.Name} must implement IRedisSerializer interface", nameof(serializerType));
            }

            var cacheKey = $"Custom_{serializerType.FullName}_{options.GetHashCode()}";

            return (IRedisSerializer)_customSerializers.GetOrAdd(serializerType, type =>
            {
                try
                {
                    var instance = Activator.CreateInstance(type, options);

                    // Using pattern matching instead of cast
                    return instance switch
                    {
                        null => throw new InvalidOperationException($"Failed to create instance of type {type.Name} with options"),
                        IRedisSerializer serializer => serializer,
                        _ => throw new InvalidOperationException($"Type {type.Name} must implement IRedisSerializer interface")
                    };
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    throw new InvalidOperationException($"Failed to create custom serializer of type {serializerType.Name} with options", ex);
                }
            });
        }

        /// <summary>
        /// Registers a custom serializer type with improved pattern matching
        /// </summary>
        /// <param name="serializerType">The type of the custom serializer</param>
        public static void RegisterCustomSerializer(Type serializerType)
        {
            if (!typeof(IRedisSerializer).IsAssignableFrom(serializerType))
            {
                throw new ArgumentException($"Type {serializerType.Name} must implement IRedisSerializer interface", nameof(serializerType));
            }

            var instance = Activator.CreateInstance(serializerType);

            _customSerializers[serializerType] = instance switch
            {
                null => throw new InvalidOperationException($"Failed to create instance of type {serializerType.Name}"),
                IRedisSerializer serializer => serializer,
                _ => throw new InvalidOperationException($"Type {serializerType.Name} must implement IRedisSerializer interface")
            };
        }

        /// <summary>
        /// Registers a custom serializer instance directly
        /// </summary>
        /// <typeparam name="TSerializer">The type of the serializer</typeparam>
        /// <param name="serializer">The serializer instance</param>
        public static void RegisterCustomSerializer<TSerializer>(TSerializer serializer) where TSerializer : IRedisSerializer
        {
            _customSerializers[typeof(TSerializer)] = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// Tries to get a registered custom serializer
        /// </summary>
        /// <typeparam name="TSerializer">The type of the serializer</typeparam>
        /// <param name="serializer">The serializer instance if found</param>
        /// <returns>True if the serializer was found, false otherwise</returns>
        public static bool TryGetCustomSerializer<TSerializer>(out TSerializer? serializer) where TSerializer : IRedisSerializer
        {
            if (_customSerializers.TryGetValue(typeof(TSerializer), out var instance))
            {
                serializer = instance is TSerializer typedSerializer ? typedSerializer : default;
                return serializer != null;
            }

            serializer = default;
            return false;
        }

        /// <summary>
        /// Clears the serializer cache (useful for testing)
        /// </summary>
        public static void ClearCache()
        {
            _serializerCache.Clear();
            _customSerializers.Clear();
        }

        /// <summary>
        /// Gets the number of cached serializers
        /// </summary>
        public static int CacheSize => _serializerCache.Count;

        /// <summary>
        /// Gets the number of registered custom serializers
        /// </summary>
        public static int CustomSerializerCount => _customSerializers.Count;
    }
}