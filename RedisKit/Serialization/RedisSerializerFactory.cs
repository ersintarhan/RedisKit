using System.Collections.Concurrent;
using System.Text.Json;
using MessagePack;
using Microsoft.Extensions.Logging;
using RedisKit.Models;

namespace RedisKit.Serialization;

/// <summary>
///     Factory for creating Redis serializers with support for custom serializers and thread-safe singleton instances
/// </summary>
public static class RedisSerializerFactory
{
    private static readonly ConcurrentDictionary<Type, object> CustomSerializers = new();
    private static readonly ConcurrentDictionary<string, IRedisSerializer> SerializerCache = new();

    /// <summary>
    ///     Gets the number of cached serializers
    /// </summary>
    public static int CacheSize => SerializerCache.Count;

    /// <summary>
    ///     Gets the number of registered custom serializers
    /// </summary>
    public static int CustomSerializerCount => CustomSerializers.Count;

    /// <summary>
    ///     Creates a serializer instance based on the specified type
    /// </summary>
    /// <param name="serializerType">The type of serializer to create</param>
    /// <param name="loggerFactory">Optional logger factory for logging</param>
    /// <returns>An instance of IRedisSerializer</returns>
    public static IRedisSerializer Create(SerializerType serializerType, ILoggerFactory? loggerFactory = null)
    {
        var cacheKey = $"{serializerType}";
        return SerializerCache.GetOrAdd(cacheKey, _ => CreateSerializerInstance(serializerType, null, loggerFactory));
    }

    /// <summary>
    ///     Creates a serializer instance with custom options
    /// </summary>
    /// <param name="serializerType">The type of serializer to create</param>
    /// <param name="options">Serializer-specific options</param>
    /// <param name="loggerFactory">Optional logger factory for logging</param>
    /// <returns>An instance of IRedisSerializer</returns>
    public static IRedisSerializer Create<TOptions>(
        SerializerType serializerType,
        TOptions options,
        ILoggerFactory? loggerFactory = null) where TOptions : class
    {
        var cacheKey = $"{serializerType}_{options.GetHashCode()}";
        return SerializerCache.GetOrAdd(cacheKey, _ => CreateSerializerWithTypedOptions(serializerType, options, loggerFactory));
    }

    /// <summary>
    ///     Creates a serializer instance with options
    /// </summary>
    public static IRedisSerializer CreateWithOptions(
        SerializerType serializerType,
        object? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        if (options == null)
            return Create(serializerType, loggerFactory);

        var cacheKey = $"{serializerType}_{options.GetHashCode()}";
        return SerializerCache.GetOrAdd(cacheKey, _ => CreateSerializerInstance(serializerType, options, loggerFactory));
    }

    /// <summary>
    ///     Creates a custom serializer instance
    /// </summary>
    /// <param name="serializerType">The type of the custom serializer</param>
    /// <param name="loggerFactory">Optional logger factory for logging</param>
    /// <returns>An instance of IRedisSerializer</returns>
    public static IRedisSerializer CreateCustom(Type serializerType, ILoggerFactory? loggerFactory = null)
    {
        ValidateCustomSerializerType(serializerType);
        return (IRedisSerializer)CustomSerializers.GetOrAdd(serializerType, type => CreateCustomSerializerInstance(type, null));
    }

    /// <summary>
    ///     Creates a custom serializer instance with specific options
    /// </summary>
    /// <param name="serializerType">The type of the custom serializer</param>
    /// <param name="options">Options for the serializer</param>
    /// <param name="loggerFactory">Optional logger factory for logging</param>
    /// <returns>An instance of IRedisSerializer</returns>
    public static IRedisSerializer CreateCustom(Type serializerType, object options, ILoggerFactory? loggerFactory = null)
    {
        ValidateCustomSerializerType(serializerType);
        return (IRedisSerializer)CustomSerializers.GetOrAdd(serializerType, type => CreateCustomSerializerInstance(type, options));
    }

    /// <summary>
    ///     Registers a custom serializer type
    /// </summary>
    /// <param name="serializerType">The type of the custom serializer</param>
    public static void RegisterCustomSerializer(Type serializerType)
    {
        ValidateCustomSerializerType(serializerType);
        var instance = CreateCustomSerializerInstance(serializerType, null);
        CustomSerializers[serializerType] = instance;
    }

    /// <summary>
    ///     Registers a custom serializer instance directly
    /// </summary>
    /// <typeparam name="TSerializer">The type of the serializer</typeparam>
    /// <param name="serializer">The serializer instance</param>
    public static void RegisterCustomSerializer<TSerializer>(TSerializer serializer) where TSerializer : IRedisSerializer
    {
        CustomSerializers[typeof(TSerializer)] = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    ///     Tries to get a registered custom serializer
    /// </summary>
    /// <typeparam name="TSerializer">The type of the serializer</typeparam>
    /// <param name="serializer">The serializer instance if found</param>
    /// <returns>True if the serializer was found, false otherwise</returns>
    public static bool TryGetCustomSerializer<TSerializer>(out TSerializer? serializer) where TSerializer : IRedisSerializer
    {
        if (CustomSerializers.TryGetValue(typeof(TSerializer), out var instance))
        {
            serializer = instance is TSerializer typedSerializer ? typedSerializer : default;
            if (serializer is not null) return true;
        }

        serializer = default;
        return false;
    }

    /// <summary>
    ///     Clears the serializer cache (useful for testing)
    /// </summary>
    public static void ClearCache()
    {
        SerializerCache.Clear();
        CustomSerializers.Clear();
    }

    #region Private Helper Methods

    private static IRedisSerializer CreateSerializerInstance(
        SerializerType serializerType,
        object? options,
        ILoggerFactory? loggerFactory)
    {
        return serializerType switch
        {
            SerializerType.MessagePack => CreateMessagePackSerializer(options, loggerFactory),
            SerializerType.SystemTextJson => CreateSystemTextJsonSerializer(options, loggerFactory),
            SerializerType.Custom => throw new ArgumentException(
                "Custom serializer type requires using CreateCustom method", nameof(serializerType)),
            _ => throw new ArgumentOutOfRangeException(nameof(serializerType),
                $"Unknown serializer type: {serializerType}")
        };
    }

    private static IRedisSerializer CreateSerializerWithTypedOptions<TOptions>(
        SerializerType serializerType,
        TOptions options,
        ILoggerFactory? loggerFactory) where TOptions : class
    {
        return (serializerType, options) switch
        {
            (SerializerType.MessagePack, MessagePackSerializerOptions msgPackOpts) =>
                new MessagePackRedisSerializer(
                    loggerFactory?.CreateLogger<MessagePackRedisSerializer>(),
                    msgPackOpts),

            (SerializerType.SystemTextJson, JsonSerializerOptions jsonOpts) =>
                new SystemTextJsonRedisSerializer(
                    loggerFactory?.CreateLogger<SystemTextJsonRedisSerializer>(),
                    jsonOpts),

            (SerializerType.MessagePack, _) =>
                throw CreateInvalidOptionsException(serializerType, typeof(MessagePackSerializerOptions), options.GetType()),

            (SerializerType.SystemTextJson, _) =>
                throw CreateInvalidOptionsException(serializerType, typeof(JsonSerializerOptions), options.GetType()),

            (SerializerType.Custom, _) =>
                throw new ArgumentException("Custom serializer type requires using CreateCustom method", nameof(serializerType)),

            _ => throw new ArgumentOutOfRangeException(nameof(serializerType),
                $"Unknown serializer type: {serializerType}")
        };
    }

    private static IRedisSerializer CreateMessagePackSerializer(object? options, ILoggerFactory? loggerFactory)
    {
        var messagePackOptions = options switch
        {
            null => null,
            MessagePackSerializerOptions msgPackOpts => msgPackOpts,
            _ => throw CreateInvalidOptionsException(SerializerType.MessagePack,
                typeof(MessagePackSerializerOptions), options.GetType())
        };

        return new MessagePackRedisSerializer(
            loggerFactory?.CreateLogger<MessagePackRedisSerializer>(),
            messagePackOptions);
    }

    private static IRedisSerializer CreateSystemTextJsonSerializer(object? options, ILoggerFactory? loggerFactory)
    {
        var jsonOptions = options switch
        {
            null => null,
            JsonSerializerOptions jsonOpts => jsonOpts,
            _ => throw CreateInvalidOptionsException(SerializerType.SystemTextJson,
                typeof(JsonSerializerOptions), options.GetType())
        };

        return new SystemTextJsonRedisSerializer(
            loggerFactory?.CreateLogger<SystemTextJsonRedisSerializer>(),
            jsonOptions);
    }

    private static object CreateCustomSerializerInstance(Type serializerType, object? options)
    {
        try
        {
            var instance = options == null
                ? Activator.CreateInstance(serializerType)
                : Activator.CreateInstance(serializerType, options);

            return instance switch
            {
                null => throw new InvalidOperationException(
                    $"Failed to create instance of type {serializerType.Name}"),
                IRedisSerializer serializer => serializer,
                _ => throw new InvalidOperationException(
                    $"Type {serializerType.Name} must implement IRedisSerializer interface")
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            var withOptionsMsg = options != null ? " with options" : "";
            throw new InvalidOperationException(
                $"Failed to create custom serializer of type {serializerType.Name}{withOptionsMsg}", ex);
        }
    }

    private static void ValidateCustomSerializerType(Type serializerType)
    {
        if (!typeof(IRedisSerializer).IsAssignableFrom(serializerType))
            throw new ArgumentException(
                $"Type {serializerType.Name} must implement IRedisSerializer interface",
                nameof(serializerType));
    }

    private static ArgumentException CreateInvalidOptionsException(
        SerializerType serializerType,
        Type expectedType,
        Type actualType)
    {
        return new ArgumentException(
            $"Invalid options type for {serializerType} serializer. Expected {expectedType.Name}, got {actualType.Name}");
    }

    #endregion
}