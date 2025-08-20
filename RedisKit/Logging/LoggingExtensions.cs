using Microsoft.Extensions.Logging;
using RedisKit.Models;

namespace RedisKit.Logging;

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


    [LoggerMessage(7, LogLevel.Debug, "Deleting item from cache with key: {Key}")]
    public static partial void LogDeleteAsync(this ILogger logger, string key);

    [LoggerMessage(8, LogLevel.Debug, "Successfully deleted item from cache with key: {Key}")]
    public static partial void LogDeleteAsyncSuccess(this ILogger logger, string key);


    [LoggerMessage(10, LogLevel.Debug, "Getting multiple items from cache with keys: {Keys}")]
    public static partial void LogGetManyAsync(this ILogger logger, string keys);

    [LoggerMessage(11, LogLevel.Debug, "Successfully retrieved {Count} items from cache")]
    public static partial void LogGetManyAsyncSuccess(this ILogger logger, int count);


    [LoggerMessage(13, LogLevel.Debug, "Setting multiple items in cache with {Count} keys")]
    public static partial void LogSetManyAsync(this ILogger logger, int count);

    [LoggerMessage(14, LogLevel.Debug, "Successfully set {Count} items in cache")]
    public static partial void LogSetManyAsyncSuccess(this ILogger logger, int count);


    [LoggerMessage(16, LogLevel.Debug, "Checking if key exists in cache: {Key}")]
    public static partial void LogExistsAsync(this ILogger logger, string key);

    [LoggerMessage(17, LogLevel.Debug, "Key {Key} exists: {Exists}")]
    public static partial void LogExistsAsyncSuccess(this ILogger logger, string key, bool exists);


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

    [LoggerMessage(27, LogLevel.Warning, "SetManyAsync called with {Count} values, which exceeds recommended limit of {Threshold}")]
    public static partial void LogSetManyBatchSizeWarning(this ILogger logger, int count, int threshold);

    // PubSub Service Log Methods
    [LoggerMessage(101, LogLevel.Debug, "Publishing message to channel: {Channel}")]
    public static partial void LogPublishAsync(this ILogger logger, string channel);

    [LoggerMessage(102, LogLevel.Debug, "Successfully published message to channel: {Channel}")]
    public static partial void LogPublishAsyncSuccess(this ILogger logger, string channel);

    // Stream Service Log Methods
    [LoggerMessage(201, LogLevel.Debug, "Adding message to stream: {Stream}")]
    public static partial void LogAddAsync(this ILogger logger, string stream);

    [LoggerMessage(202, LogLevel.Debug, "Successfully added message to stream: {Stream} with ID: {MessageId}")]
    public static partial void LogAddAsyncSuccess(this ILogger logger, string stream, string messageId);

    [LoggerMessage(204, LogLevel.Debug, "Reading messages from stream: {Stream} with start: {Start}, end: {End}, count: {Count}")]
    public static partial void LogReadAsync(this ILogger logger, string stream, string start, string end, int count);

    [LoggerMessage(205, LogLevel.Debug, "Successfully read {Count} messages from stream: {Stream}")]
    public static partial void LogReadAsyncSuccess(this ILogger logger, string stream, int count);

    [LoggerMessage(207, LogLevel.Debug, "Creating consumer group {GroupName} for stream: {Stream}")]
    public static partial void LogCreateConsumerGroupAsync(this ILogger logger, string stream, string groupName);

    [LoggerMessage(208, LogLevel.Debug, "Successfully created consumer group {GroupName} for stream: {Stream}")]
    public static partial void LogCreateConsumerGroupAsyncSuccess(this ILogger logger, string stream, string groupName);


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


    [LoggerMessage(317, LogLevel.Debug, "JSON stream deserialization started for {Type}")]
    public static partial void LogJsonStreamDeserialize(this ILogger logger, string type);


    [LoggerMessage(321, LogLevel.Warning, "Large object detected for serialization: {Type} with {ElementCount} elements")]
    public static partial void LogLargeObjectWarning(this ILogger logger, string type, int elementCount);

    [LoggerMessage(322, LogLevel.Error, "Out of memory during serialization of {Type}")]
    public static partial void LogOutOfMemoryError(this ILogger logger, string type, Exception ex);

    [LoggerMessage(323, LogLevel.Error, "Type {Type} is not supported for serialization")]
    public static partial void LogUnsupportedTypeError(this ILogger logger, string type, Exception ex);

    [LoggerMessage(324, LogLevel.Debug, "Serialization completed in {ElapsedMs}ms for {Type} ({ByteCount} bytes)")]
    public static partial void LogSerializationPerformance(this ILogger logger, double elapsedMs, string type, int byteCount);


    [LoggerMessage(331, LogLevel.Debug, "Serializer cache hit for {SerializerType}, returning cached instance")]
    public static partial void LogSerializerCacheHit(this ILogger logger, string serializerType);

    [LoggerMessage(332, LogLevel.Debug, "Serializer cache miss for {SerializerType}, creating new instance")]
    public static partial void LogSerializerCacheMiss(this ILogger logger, string serializerType);

    [LoggerMessage(333, LogLevel.Warning, "Custom serializer registration failed for type {TypeName}")]
    public static partial void LogCustomSerializerRegistrationFailed(this ILogger logger, string typeName, Exception ex);


    [LoggerMessage(335, LogLevel.Warning, "Attempting to deserialize large data of {ByteCount} bytes for type {Type}")]
    public static partial void LogLargeDataDeserializationWarning(this ILogger logger, int byteCount, string type);

    // Connection Service Log Methods
    [LoggerMessage(401, LogLevel.Information, "Creating Redis connection to: {ConnectionString}")]
    public static partial void LogConnectionCreating(this ILogger logger, string connectionString);

    [LoggerMessage(402, LogLevel.Information, "Successfully connected to Redis at: {ConnectionString}")]
    public static partial void LogConnectionSuccess(this ILogger logger, string connectionString);

    [LoggerMessage(403, LogLevel.Debug, "Connection attempt {Attempt}/{MaxAttempts}")]
    public static partial void LogConnectionAttempt(this ILogger logger, int attempt, int maxAttempts);

    [LoggerMessage(404, LogLevel.Information, "Connected to Redis on attempt {Attempt} after {Duration}ms")]
    public static partial void LogConnectionSuccessWithRetry(this ILogger logger, int attempt, long duration);

    [LoggerMessage(405, LogLevel.Warning, "Failed to connect to Redis (attempt {Attempt}/{MaxAttempts})")]
    public static partial void LogConnectionAttemptFailed(this ILogger logger, int attempt, int maxAttempts, Exception ex);

    [LoggerMessage(406, LogLevel.Debug, "Waiting {DelayMs}ms before retry (strategy: {Strategy})")]
    public static partial void LogConnectionRetryDelay(this ILogger logger, int delayMs, string strategy);

    [LoggerMessage(407, LogLevel.Error, "Failed to connect to Redis after {Attempts} attempts in {Duration}ms")]
    public static partial void LogConnectionFailed(this ILogger logger, int attempts, long duration, Exception ex);

    [LoggerMessage(408, LogLevel.Debug, "Redis connection timeout settings applied. Connect: {ConnectMs}ms, Sync: {SyncMs}ms, Async: {AsyncMs}ms")]
    public static partial void LogConnectionTimeoutSettings(this ILogger logger, int connectMs, int syncMs, int asyncMs);

    [LoggerMessage(409, LogLevel.Error, "Redis connection failed: {FailureType} - {Exception}")]
    public static partial void LogConnectionFailure(this ILogger logger, string failureType, string exception);

    [LoggerMessage(410, LogLevel.Information, "Redis connection restored: {EndPoint}")]
    public static partial void LogConnectionRestored(this ILogger logger, string endPoint);

    [LoggerMessage(411, LogLevel.Error, "Redis error: {Message} from {EndPoint}")]
    public static partial void LogRedisError(this ILogger logger, string message, string endPoint);

    [LoggerMessage(412, LogLevel.Error, "Redis internal error: {Origin}")]
    public static partial void LogRedisInternalError(this ILogger logger, string origin, Exception exception);

    [LoggerMessage(413, LogLevel.Debug, "Health check succeeded in {Duration}ms")]
    public static partial void LogHealthCheckSuccess(this ILogger logger, long duration);

    [LoggerMessage(414, LogLevel.Warning, "Health check failed {Failures} times, attempting reconnection")]
    public static partial void LogHealthCheckFailureWithReconnect(this ILogger logger, int failures);

    [LoggerMessage(415, LogLevel.Error, "Auto-reconnection failed")]
    public static partial void LogAutoReconnectionFailed(this ILogger logger, Exception ex);

    [LoggerMessage(416, LogLevel.Warning, "Health check failed")]
    public static partial void LogHealthCheckFailed(this ILogger logger, Exception ex);

    [LoggerMessage(417, LogLevel.Information, "Circuit breaker has been reset")]
    public static partial void LogCircuitBreakerReset(this ILogger logger);

    [LoggerMessage(418, LogLevel.Error, "Error disposing Redis connection")]
    public static partial void LogConnectionDisposeError(this ILogger logger, Exception ex);

    // Circuit Breaker Log Methods
    [LoggerMessage(501, LogLevel.Warning, "Circuit breaker recorded failure in {State} state")]
    public static partial void LogCircuitBreakerFailure(this ILogger logger, CircuitState state, Exception? exception);

    [LoggerMessage(502, LogLevel.Information, "Circuit breaker transitioned from {OldState} to {NewState}")]
    public static partial void LogCircuitBreakerTransition(this ILogger logger, CircuitState oldState, CircuitState newState);
}