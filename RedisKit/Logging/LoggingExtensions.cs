using Microsoft.Extensions.Logging;

namespace RedisKit.Logging
{
    internal static partial class LoggingExtensions
    {
        // Cache Service Log Methods
        [LoggerMessage(1, LogLevel.Debug, "Getting item from cache with key: {Key}")]
        public static partial void LogGetAsync(this ILogger logger, string key);

        [LoggerMessage(2, LogLevel.Debug, "Successfully retrieved item from cache with key: {Key}")]
        public static partial void LogGetAsyncSuccess(this ILogger logger, string key);

        [LoggerMessage(3, LogLevel.Error, "Error getting item from cache with key: {Key}")]
        public static partial void LogGetAsyncError(this ILogger logger, string key, Exception ex);

        [LoggerMessage(4, LogLevel.Debug, "Setting item in cache with key: {Key}")]
        public static partial void LogSetAsync(this ILogger logger, string key);

        [LoggerMessage(5, LogLevel.Debug, "Successfully set item in cache with key: {Key}")]
        public static partial void LogSetAsyncSuccess(this ILogger logger, string key);

        [LoggerMessage(6, LogLevel.Error, "Error setting item in cache with key: {Key}")]
        public static partial void LogSetAsyncError(this ILogger logger, string key, Exception ex);

        [LoggerMessage(7, LogLevel.Debug, "Deleting item from cache with key: {Key}")]
        public static partial void LogDeleteAsync(this ILogger logger, string key);

        [LoggerMessage(8, LogLevel.Debug, "Successfully deleted item from cache with key: {Key}")]
        public static partial void LogDeleteAsyncSuccess(this ILogger logger, string key);

        [LoggerMessage(9, LogLevel.Error, "Error deleting item from cache with key: {Key}")]
        public static partial void LogDeleteAsyncError(this ILogger logger, string key, Exception ex);

        [LoggerMessage(10, LogLevel.Debug, "Getting multiple items from cache with keys: {Keys}")]
        public static partial void LogGetManyAsync(this ILogger logger, string keys);

        [LoggerMessage(11, LogLevel.Debug, "Successfully retrieved {Count} items from cache")]
        public static partial void LogGetManyAsyncSuccess(this ILogger logger, int count);

        [LoggerMessage(12, LogLevel.Error, "Error getting multiple items from cache")]
        public static partial void LogGetManyAsyncError(this ILogger logger, Exception ex);

        [LoggerMessage(13, LogLevel.Debug, "Setting multiple items in cache with {Count} keys")]
        public static partial void LogSetManyAsync(this ILogger logger, int count);

        [LoggerMessage(14, LogLevel.Debug, "Successfully set {Count} items in cache")]
        public static partial void LogSetManyAsyncSuccess(this ILogger logger, int count);

        [LoggerMessage(15, LogLevel.Error, "Error setting multiple items in cache")]
        public static partial void LogSetManyAsyncError(this ILogger logger, Exception ex);

        [LoggerMessage(16, LogLevel.Debug, "Checking if key exists in cache: {Key}")]
        public static partial void LogExistsAsync(this ILogger logger, string key);

        [LoggerMessage(17, LogLevel.Debug, "Key {Key} exists: {Exists}")]
        public static partial void LogExistsAsyncSuccess(this ILogger logger, string key, bool exists);

        [LoggerMessage(18, LogLevel.Error, "Error checking if key exists in cache: {Key}")]
        public static partial void LogExistsAsyncError(this ILogger logger, string key, Exception ex);

        // Lua Script Support and Performance Log Methods
        [LoggerMessage(19, LogLevel.Information, "Lua scripts are supported, using optimized path")]
        public static partial void LogLuaScriptSupported(this ILogger logger);

        [LoggerMessage(20, LogLevel.Warning, "Lua scripts test failed, using fallback mode")]
        public static partial void LogLuaScriptTestFailed(this ILogger logger);

        [LoggerMessage(21, LogLevel.Warning, "Lua scripts not supported, falling back to standard commands")]
        public static partial void LogLuaScriptNotSupported(this ILogger logger, Exception ex);

        [LoggerMessage(22, LogLevel.Debug, "SetManyAsync progress: {Processed}/{Total} items")]
        public static partial void LogSetManyAsyncProgress(this ILogger logger, int processed, int total);

        [LoggerMessage(23, LogLevel.Warning, "Slow SetManyAsync detected: Count={Count}, Duration={Duration}ms, Strategy={Strategy}")]
        public static partial void LogSlowSetManyAsync(this ILogger logger, int count, long duration, string strategy);

        [LoggerMessage(24, LogLevel.Warning, "Some keys were not set successfully. Expected: {Expected}, Actual: {Actual}")]
        public static partial void LogSetManyPartialSuccess(this ILogger logger, int expected, int actual);

        [LoggerMessage(25, LogLevel.Warning, "Lua script not in cache, switching to fallback mode")]
        public static partial void LogLuaScriptNotInCache(this ILogger logger);

        [LoggerMessage(26, LogLevel.Error, "Lua script execution failed, switching to fallback mode")]
        public static partial void LogLuaScriptExecutionFailed(this ILogger logger, Exception ex);

        // PubSub Service Log Methods
        [LoggerMessage(101, LogLevel.Debug, "Publishing message to channel: {Channel}")]
        public static partial void LogPublishAsync(this ILogger logger, string channel);

        [LoggerMessage(102, LogLevel.Debug, "Successfully published message to channel: {Channel}")]
        public static partial void LogPublishAsyncSuccess(this ILogger logger, string channel);

        [LoggerMessage(103, LogLevel.Error, "Error publishing message to channel: {Channel}")]
        public static partial void LogPublishAsyncError(this ILogger logger, string channel, Exception ex);

        [LoggerMessage(104, LogLevel.Debug, "Subscribing to channel: {Channel}")]
        public static partial void LogSubscribeAsync(this ILogger logger, string channel);

        [LoggerMessage(105, LogLevel.Debug, "Successfully subscribed to channel: {Channel}")]
        public static partial void LogSubscribeAsyncSuccess(this ILogger logger, string channel);

        [LoggerMessage(106, LogLevel.Error, "Error subscribing to channel: {Channel}")]
        public static partial void LogSubscribeAsyncError(this ILogger logger, string channel, Exception ex);

        [LoggerMessage(107, LogLevel.Debug, "Unsubscribing from channel: {Channel}")]
        public static partial void LogUnsubscribeAsync(this ILogger logger, string channel);

        [LoggerMessage(108, LogLevel.Debug, "Successfully unsubscribed from channel: {Channel}")]
        public static partial void LogUnsubscribeAsyncSuccess(this ILogger logger, string channel);

        [LoggerMessage(109, LogLevel.Error, "Error unsubscribing from channel: {Channel}")]
        public static partial void LogUnsubscribeAsyncError(this ILogger logger, string channel, Exception ex);

        [LoggerMessage(110, LogLevel.Debug, "Subscribing to channel pattern: {Pattern}")]
        public static partial void LogSubscribePatternAsync(this ILogger logger, string pattern);

        [LoggerMessage(111, LogLevel.Debug, "Successfully subscribed to channel pattern: {Pattern}")]
        public static partial void LogSubscribePatternAsyncSuccess(this ILogger logger, string pattern);

        [LoggerMessage(112, LogLevel.Error, "Error subscribing to channel pattern: {Pattern}")]
        public static partial void LogSubscribePatternAsyncError(this ILogger logger, string pattern, Exception ex);

        [LoggerMessage(113, LogLevel.Debug, "Unsubscribing from channel pattern: {Pattern}")]
        public static partial void LogUnsubscribePatternAsync(this ILogger logger, string pattern);

        [LoggerMessage(114, LogLevel.Debug, "Successfully unsubscribed from channel pattern: {Pattern}")]
        public static partial void LogUnsubscribePatternAsyncSuccess(this ILogger logger, string pattern);

        [LoggerMessage(115, LogLevel.Error, "Error unsubscribing from channel pattern: {Pattern}")]
        public static partial void LogUnsubscribePatternAsyncError(this ILogger logger, string pattern, Exception ex);

        // Stream Service Log Methods
        [LoggerMessage(201, LogLevel.Debug, "Adding message to stream: {Stream}")]
        public static partial void LogAddAsync(this ILogger logger, string stream);

        [LoggerMessage(202, LogLevel.Debug, "Successfully added message to stream: {Stream} with ID: {MessageId}")]
        public static partial void LogAddAsyncSuccess(this ILogger logger, string stream, string messageId);

        [LoggerMessage(203, LogLevel.Error, "Error adding message to stream: {Stream}")]
        public static partial void LogAddAsyncError(this ILogger logger, string stream, Exception ex);

        [LoggerMessage(204, LogLevel.Debug, "Reading messages from stream: {Stream} with start: {Start}, end: {End}, count: {Count}")]
        public static partial void LogReadAsync(this ILogger logger, string stream, string start, string end, int count);

        [LoggerMessage(205, LogLevel.Debug, "Successfully read {Count} messages from stream: {Stream}")]
        public static partial void LogReadAsyncSuccess(this ILogger logger, string stream, int count);

        [LoggerMessage(206, LogLevel.Error, "Error reading messages from stream: {Stream}")]
        public static partial void LogReadAsyncError(this ILogger logger, string stream, Exception ex);

        [LoggerMessage(207, LogLevel.Debug, "Creating consumer group {GroupName} for stream: {Stream}")]
        public static partial void LogCreateConsumerGroupAsync(this ILogger logger, string stream, string groupName);

        [LoggerMessage(208, LogLevel.Debug, "Successfully created consumer group {GroupName} for stream: {Stream}")]
        public static partial void LogCreateConsumerGroupAsyncSuccess(this ILogger logger, string stream, string groupName);

        [LoggerMessage(209, LogLevel.Error, "Error creating consumer group {GroupName} for stream: {Stream}")]
        public static partial void LogCreateConsumerGroupAsyncError(this ILogger logger, string stream, string groupName, Exception ex);

        [LoggerMessage(210, LogLevel.Debug, "Reading messages from stream: {Stream} using group: {GroupName}, consumer: {ConsumerName}, count: {Count}")]
        public static partial void LogReadGroupAsync(this ILogger logger, string stream, string groupName, string consumerName, int count);

        [LoggerMessage(211, LogLevel.Debug, "Successfully read {Count} messages from stream: {Stream} using group: {GroupName}, consumer: {ConsumerName}")]
        public static partial void LogReadGroupAsyncSuccess(this ILogger logger, string stream, string groupName, string consumerName, int count);

        [LoggerMessage(212, LogLevel.Error, "Error reading messages from stream: {Stream} using group: {GroupName}, consumer: {ConsumerName}")]
        public static partial void LogReadGroupAsyncError(this ILogger logger, string stream, string groupName, string consumerName, Exception ex);

        [LoggerMessage(213, LogLevel.Debug, "Acknowledging message {MessageId} in stream: {Stream} for group: {GroupName}")]
        public static partial void LogAcknowledgeAsync(this ILogger logger, string stream, string groupName, string messageId);

        [LoggerMessage(214, LogLevel.Debug, "Successfully acknowledged message {MessageId} in stream: {Stream} for group: {GroupName}")]
        public static partial void LogAcknowledgeAsyncSuccess(this ILogger logger, string stream, string groupName, string messageId);

        [LoggerMessage(215, LogLevel.Error, "Error acknowledging message {MessageId} in stream: {Stream} for group: {GroupName}")]
        public static partial void LogAcknowledgeAsyncError(this ILogger logger, string stream, string groupName, string messageId, Exception ex);

        // Serializer Log Methods - High Performance Source Generated Logging
        // MessagePack Serializer
        [LoggerMessage(301, LogLevel.Debug, "Serializing {Type} using MessagePack, estimated size: {Size} bytes")]
        public static partial void LogMessagePackSerialize(this ILogger logger, string type, int size);

        [LoggerMessage(302, LogLevel.Debug, "Deserializing {ByteCount} bytes to {Type} using MessagePack")]
        public static partial void LogMessagePackDeserialize(this ILogger logger, int byteCount, string type);

        [LoggerMessage(303, LogLevel.Warning, "Large MessagePack serialization detected: {Type} resulted in {ByteCount} bytes")]
        public static partial void LogMessagePackLargeData(this ILogger logger, string type, int byteCount);

        [LoggerMessage(304, LogLevel.Error, "MessagePack serialization failed for {Type}")]
        public static partial void LogMessagePackSerializeError(this ILogger logger, string type, Exception ex);

        [LoggerMessage(305, LogLevel.Error, "MessagePack deserialization failed for {Type} from {ByteCount} bytes")]
        public static partial void LogMessagePackDeserializeError(this ILogger logger, string type, int byteCount, Exception ex);

        // System.Text.Json Serializer
        [LoggerMessage(311, LogLevel.Debug, "Serializing {Type} using System.Text.Json")]
        public static partial void LogJsonSerialize(this ILogger logger, string type);

        [LoggerMessage(312, LogLevel.Debug, "Deserializing {ByteCount} bytes to {Type} using System.Text.Json")]
        public static partial void LogJsonDeserialize(this ILogger logger, int byteCount, string type);

        [LoggerMessage(313, LogLevel.Warning, "Large JSON serialization detected: {Type} resulted in {ByteCount} bytes")]
        public static partial void LogJsonLargeData(this ILogger logger, string type, int byteCount);

        [LoggerMessage(314, LogLevel.Error, "JSON serialization failed for {Type}: {ErrorMessage}")]
        public static partial void LogJsonSerializeError(this ILogger logger, string type, string errorMessage, Exception ex);

        [LoggerMessage(315, LogLevel.Error, "JSON deserialization failed for {Type} from {ByteCount} bytes: {ErrorMessage}")]
        public static partial void LogJsonDeserializeError(this ILogger logger, string type, int byteCount, string errorMessage, Exception ex);

        [LoggerMessage(316, LogLevel.Warning, "Invalid UTF-8 encoding detected when deserializing {Type}")]
        public static partial void LogJsonInvalidUtf8(this ILogger logger, string type);

        [LoggerMessage(317, LogLevel.Debug, "JSON stream deserialization started for {Type}")]
        public static partial void LogJsonStreamDeserialize(this ILogger logger, string type);

        // Generic Serializer Methods
        [LoggerMessage(320, LogLevel.Information, "Serializer {SerializerName} initialized with options")]
        public static partial void LogSerializerInitialized(this ILogger logger, string serializerName);

        [LoggerMessage(321, LogLevel.Warning, "Large object detected for serialization: {Type} with {ElementCount} elements")]
        public static partial void LogLargeObjectWarning(this ILogger logger, string type, int elementCount);

        [LoggerMessage(322, LogLevel.Error, "Out of memory during serialization of {Type}")]
        public static partial void LogOutOfMemoryError(this ILogger logger, string type, Exception ex);

        [LoggerMessage(323, LogLevel.Error, "Type {Type} is not supported for serialization")]
        public static partial void LogUnsupportedTypeError(this ILogger logger, string type, Exception ex);

        [LoggerMessage(324, LogLevel.Debug, "Serialization completed in {ElapsedMs}ms for {Type} ({ByteCount} bytes)")]
        public static partial void LogSerializationPerformance(this ILogger logger, double elapsedMs, string type, int byteCount);

        [LoggerMessage(325, LogLevel.Debug, "Deserialization completed in {ElapsedMs}ms for {Type}")]
        public static partial void LogDeserializationPerformance(this ILogger logger, double elapsedMs, string type);

        // Factory and Configuration
        [LoggerMessage(330, LogLevel.Information, "Creating serializer of type {SerializerType}")]
        public static partial void LogSerializerCreation(this ILogger logger, string serializerType);

        [LoggerMessage(331, LogLevel.Debug, "Serializer cache hit for {SerializerType}, returning cached instance")]
        public static partial void LogSerializerCacheHit(this ILogger logger, string serializerType);

        [LoggerMessage(332, LogLevel.Debug, "Serializer cache miss for {SerializerType}, creating new instance")]
        public static partial void LogSerializerCacheMiss(this ILogger logger, string serializerType);

        [LoggerMessage(333, LogLevel.Warning, "Custom serializer registration failed for type {TypeName}")]
        public static partial void LogCustomSerializerRegistrationFailed(this ILogger logger, string typeName, Exception ex);

        [LoggerMessage(334, LogLevel.Information, "Serializer cache cleared, {Count} instances removed")]
        public static partial void LogSerializerCacheCleared(this ILogger logger, int count);

        [LoggerMessage(335, LogLevel.Warning, "Attempting to deserialize large data of {ByteCount} bytes for type {Type}")]
        public static partial void LogLargeDataDeserializationWarning(this ILogger logger, int byteCount, string type);
    }
}
