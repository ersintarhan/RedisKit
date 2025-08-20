using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RedisKit.Helpers;
using RedisKit.Interfaces;
using RedisKit.Logging;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     Implementation of IRedisStreamService using StackExchange.Redis with advanced stream operations.
///     Provides comprehensive support for Redis Streams including consumer groups, message acknowledgment, and dead letter queues.
/// </summary>
/// <remarks>
///     This class implements Redis Streams operations with enterprise-grade features for event sourcing,
///     message queuing, and real-time data processing scenarios.
///     Thread Safety: This class is fully thread-safe and designed for singleton usage.
///     Core Capabilities:
///     - Append-only log with automatic ID generation
///     - Consumer groups for distributed processing
///     - Message acknowledgment and retry mechanisms
///     - Dead letter queue support for failed messages
///     - Batch operations for high throughput
///     - Stream trimming for memory management
///     - Pending message recovery
///     Stream Features:
///     - Auto-generated IDs based on timestamp-sequence format
///     - MAXLEN trimming (approximate or exact)
///     - Range queries with start/end IDs
///     - Consumer group coordination
///     - Pending entries list (PEL) management
///     - Message claiming for failure recovery
///     Consumer Groups:
///     - Multiple consumers per group with load balancing
///     - At-least-once delivery semantics
///     - Message acknowledgment tracking
///     - Automatic message assignment
///     - Idle message detection and recovery
///     - Consumer health monitoring
///     Reliability Features:
///     - Retry mechanism with exponential backoff
///     - Dead letter queue for persistent failures
///     - Message claiming from failed consumers
///     - Pending message timeout detection
///     - Automatic reconnection on failures
///     Performance Optimizations:
///     - Batch message addition for throughput
///     - Efficient serialization with configurable formats
///     - Approximate trimming for better performance
///     - Parallel message processing support
///     - Memory-efficient streaming operations
///     Common Use Cases:
///     - Event Sourcing: Append-only event log with replay
///     - Message Queue: Reliable message delivery with acknowledgment
///     - Activity Feed: Real-time updates with consumer groups
///     - IoT Data: High-volume sensor data ingestion
///     - Audit Log: Immutable audit trail with timestamps
///     - CQRS: Command/event separation with streams
///     Usage Example:
///     <code>
/// public class OrderEventProcessor
/// {
///     private readonly IRedisStreamService _streamService;
/// 
///     // Publishing events
///     public async Task PublishOrderEvent(OrderEvent orderEvent)
///     {
///         var messageId = await _streamService.AddAsync(
///             "orders:events",
///             orderEvent,
///             maxLength: 100000); // Keep last 100k events
/// 
///         Console.WriteLine($"Published event: {messageId}");
///     }
/// 
///     // Processing events with consumer group
///     public async Task ProcessOrderEvents()
///     {
///         // Create consumer group
///         await _streamService.CreateConsumerGroupAsync(
///             "orders:events",
///             "order-processors");
/// 
///         while (!cancellationToken.IsCancellationRequested)
///         {
///             // Read pending messages
///             var messages = await _streamService.ReadGroupAsync&lt;OrderEvent&gt;(
///                 "orders:events",
///                 "order-processors",
///                 "worker-1",
///                 count: 10);
/// 
///             foreach (var (messageId, orderEvent) in messages)
///             {
///                 try
///                 {
///                     await ProcessOrder(orderEvent);
/// 
///                     // Acknowledge successful processing
///                     await _streamService.AcknowledgeAsync(
///                         "orders:events",
///                         "order-processors",
///                         messageId);
///                 }
///                 catch (Exception ex)
///                 {
///                     // Move to DLQ after retries
///                     await _streamService.MoveToDeadLetterAsync&lt;OrderEvent&gt;(
///                         "orders:events",
///                         "orders:dlq",
///                         messageId,
///                         ex.Message);
///                 }
///             }
/// 
///             await Task.Delay(1000); // Polling interval
///         }
///     }
/// 
///     // Claim abandoned messages
///     public async Task RecoverAbandonedMessages()
///     {
///         var pending = await _streamService.GetPendingAsync(
///             "orders:events",
///             "order-processors");
/// 
///         var stuckMessages = pending
///             .Where(p => p.IdleTime > TimeSpan.FromMinutes(5))
///             .Select(p => p.MessageId)
///             .ToArray();
/// 
///         if (stuckMessages.Any())
///         {
///             var claimed = await _streamService.ClaimAsync&lt;OrderEvent&gt;(
///                 "orders:events",
///                 "order-processors",
///                 "recovery-worker",
///                 300000, // 5 minutes idle time
///                 stuckMessages);
/// 
///             // Process claimed messages...
///         }
///     }
/// }
/// </code>
/// </remarks>
public class RedisStreamService : IRedisStreamService
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisStreamService> _logger;
    private readonly RedisOptions _options;
    private readonly ObjectPool<List<(int index, string id)>>? _resultListPool;
    private readonly IRedisSerializer _serializer;
    private string _keyPrefix = string.Empty;

    public RedisStreamService(
        IRedisConnection connection,
        ILogger<RedisStreamService> logger,
        IOptions<RedisOptions> options,
        ObjectPoolProvider? poolProvider = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _options = options.Value ?? throw new ArgumentNullException(nameof(options), "Options value cannot be null");

        // Create serializer based on configuration
        _serializer = RedisSerializerFactory.Create(_options.Serializer);

        // Initialize object pool for result lists
        var provider = poolProvider ?? new DefaultObjectPoolProvider();
        _resultListPool = provider.Create<List<(int index, string id)>>();
    }

    public async Task<string> AddAsync<T>(string stream, T message, CancellationToken cancellationToken = default) where T : class
    {
        return await AddAsync(stream, message, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AddAsync<T>(string stream, T message, int? maxLength, CancellationToken cancellationToken = default) where T : class
    {
        // Use validation helper
        StreamValidationHelper.ValidateStreamName(stream);

        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var prefixedStream = $"{_keyPrefix}{stream}";

        return await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                _logger.LogAddAsync(prefixedStream);

                // Use serialization helper
                var entry = new[] { StreamSerializationHelper.CreateDataField(message, _serializer) };
                var database = await GetDatabaseAsync();

                // Add to stream and get the message ID
                // If maxLength is specified, the stream will be trimmed to approximately that length
                var messageId = await database.StreamAddAsync(
                    prefixedStream,
                    entry,
                    null, // Auto-generate ID
                    maxLength,
                    true); // Use ~ for approximate trimming (more efficient)

                _logger.LogAddAsyncSuccess(prefixedStream, messageId.ToString());
                return messageId.ToString();
            },
            _logger,
            prefixedStream,
            cancellationToken
        ).ConfigureAwait(false) ?? string.Empty;
    }

    public async Task<Dictionary<string, T?>> ReadAsync<T>(string stream, string? start = null, string? end = null, int count = 10, CancellationToken cancellationToken = default) where T : class
    {
        // Use validation helper
        StreamValidationHelper.ValidateStreamName(stream);
        StreamValidationHelper.ValidateCount(count);

        var prefixedStream = $"{_keyPrefix}{stream}";

        try
        {
            _logger.LogReadAsync(prefixedStream, start ?? "0", end ?? "+", count);

            var database = await GetDatabaseAsync();

            // Read from stream
            var entries = await database.StreamRangeAsync(prefixedStream, start ?? "0", end ?? "+", count).ConfigureAwait(false);

            var result = new Dictionary<string, T?>();

            // Use serialization helper for deserialization
            foreach (var entry in entries)
            {
                var data = StreamSerializationHelper.DeserializeStreamEntry<T>(entry, _serializer);
                if (data != null) result[entry.Id!] = data;
            }

            _logger.LogReadAsyncSuccess(prefixedStream, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogReadAsyncError(prefixedStream, ex);
            throw;
        }
    }

    public async Task CreateConsumerGroupAsync(string stream, string groupName, CancellationToken cancellationToken = default)
    {
        // Use validation helpers
        StreamValidationHelper.ValidateStreamName(stream);
        StreamValidationHelper.ValidateGroupName(groupName);

        var prefixedStream = $"{_keyPrefix}{stream}";

        await RedisOperationExecutor.ExecuteWithSilentErrorHandlingAsync(
            async () =>
            {
                _logger.LogCreateConsumerGroupAsync(prefixedStream, groupName);

                var database = await GetDatabaseAsync();

                // Create consumer group starting from the beginning of the stream
                // The 'false' parameter means don't create the stream if it doesn't exist
                await database.StreamCreateConsumerGroupAsync(
                    prefixedStream,
                    groupName,
                    StreamPosition.Beginning,
                    false);

                _logger.LogCreateConsumerGroupAsyncSuccess(prefixedStream, groupName);
                return (object?)true;
            },
            _logger,
            RedisErrorPatterns.BusyGroup,
            defaultValue: (object?)true, // Return true even if group exists
            key: prefixedStream
        ).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, T?>> ReadGroupAsync<T>(string stream, string groupName, string consumerName, int count = 10, CancellationToken cancellationToken = default) where T : class
    {
        // Use validation helpers
        StreamValidationHelper.ValidateStreamName(stream);
        StreamValidationHelper.ValidateGroupName(groupName);
        StreamValidationHelper.ValidateConsumerName(consumerName);
        StreamValidationHelper.ValidateCount(count);

        var prefixedStream = $"{_keyPrefix}{stream}";

        try
        {
            _logger.LogReadGroupAsync(prefixedStream, groupName, consumerName, count);
            var database = await GetDatabaseAsync();

            // Read from stream using consumer group
            // ">" means read only new messages (not yet delivered to this consumer)
            var entries = await database.StreamReadGroupAsync(
                prefixedStream,
                groupName,
                consumerName,
                ">", // Read new messages
                count);

            var result = new Dictionary<string, T?>();

            foreach (var entry in entries)
            {
                // Extract the data field and deserialize
                var value = StreamSerializationHelper.GetDataFieldValue(entry);

                if (value.IsNullOrEmpty) continue;
                try
                {
                    var deserialized = await _serializer.DeserializeAsync<T>(value!, cancellationToken);
                    result[entry.Id!] = deserialized;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deserializing message from stream {Stream} with ID {Id}", prefixedStream, entry.Id);
                    result[entry.Id!] = null;
                }
            }

            _logger.LogReadGroupAsyncSuccess(prefixedStream, groupName, consumerName, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogReadGroupAsyncError(prefixedStream, groupName, consumerName, ex);
            throw;
        }
    }

    public async Task AcknowledgeAsync(string stream, string groupName, string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        if (string.IsNullOrEmpty(groupName))
            throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));

        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));

        var prefixedStream = $"{_keyPrefix}{stream}";

        try
        {
            _logger.LogAcknowledgeAsync(prefixedStream, groupName, messageId);
            var database = await GetDatabaseAsync();

            // Acknowledge the message
            await database.StreamAcknowledgeAsync(prefixedStream, groupName, messageId).ConfigureAwait(false);

            _logger.LogAcknowledgeAsyncSuccess(prefixedStream, groupName, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogAcknowledgeAsyncError(prefixedStream, groupName, messageId, ex);
            throw;
        }
    }

    public async Task<long> DeleteAsync(string stream, string[] messageIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        if (messageIds == null || messageIds.Length == 0)
            throw new ArgumentException("Message IDs cannot be null or empty", nameof(messageIds));

        var prefixedStream = $"{_keyPrefix}{stream}";

        // Validate stream name
        if (stream.Length > 512) _logger.LogWarning("Stream name exceeds Redis key limit of 512 characters: {Stream}", stream);

        try
        {
            _logger.LogDebug("Deleting {Count} messages from stream {Stream}", messageIds.Length, prefixedStream);

            // Convert string array to RedisValue array
            var redisMessageIds = Array.ConvertAll(messageIds, id => (RedisValue)id);
            var database = await GetDatabaseAsync();

            // Delete messages from stream
            var deletedCount = await database.StreamDeleteAsync(prefixedStream, redisMessageIds).ConfigureAwait(false);

            _logger.LogInformation("Deleted {Count} messages from stream {Stream}", deletedCount, prefixedStream);
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting messages from stream {Stream}", prefixedStream);
            throw;
        }
    }

    public async Task<long> TrimByLengthAsync(string stream, int maxLength, bool useApproximateMaxLength = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        if (maxLength <= 0)
            throw new ArgumentException("Max length must be greater than 0", nameof(maxLength));

        var prefixedStream = $"{_keyPrefix}{stream}";

        try
        {
            _logger.LogDebug("Trimming stream {Stream} to maximum {MaxLength} entries", prefixedStream, maxLength);
            var database = await GetDatabaseAsync();

            // Trim the stream
            var trimmedCount = await database.StreamTrimAsync(
                prefixedStream,
                maxLength,
                useApproximateMaxLength);

            _logger.LogInformation("Trimmed {Count} messages from stream {Stream}", trimmedCount, prefixedStream);
            return trimmedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trimming stream {Stream}", prefixedStream);
            throw;
        }
    }

    public async Task<Dictionary<string, T?>> ClaimAsync<T>(string stream, string groupName, string consumerName, long minIdleTime, string[] messageIds, CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        if (string.IsNullOrEmpty(groupName))
            throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));

        if (string.IsNullOrEmpty(consumerName))
            throw new ArgumentException("Consumer name cannot be null or empty", nameof(consumerName));

        if (messageIds == null || messageIds.Length == 0)
            throw new ArgumentException("Message IDs cannot be null or empty", nameof(messageIds));

        var prefixedStream = $"{_keyPrefix}{stream}";

        _logger.LogDebug("Claiming {Count} messages from stream {Stream} for consumer {Consumer}",
            messageIds.Length, prefixedStream, consumerName);

        // Convert string array to RedisValue array
        var redisMessageIds = Array.ConvertAll(messageIds, id => (RedisValue)id);
        var database = await GetDatabaseAsync();

        // Claim the messages
        var entries = await database.StreamClaimAsync(
            prefixedStream,
            groupName,
            consumerName,
            minIdleTime,
            redisMessageIds);

        var result = new Dictionary<string, T?>();

        foreach (var entry in entries)
        {
            // Extract the data field and deserialize
            var value = entry.Values.FirstOrDefault(fv => fv.Name == "data").Value;

            if (!value.IsNullOrEmpty)
                try
                {
                    var deserialized = await _serializer.DeserializeAsync<T>(value!, cancellationToken);
                    result[entry.Id!] = deserialized;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deserializing claimed message from stream {Stream} with ID {Id}",
                        prefixedStream, entry.Id);
                    result[entry.Id!] = null;
                }
        }

        _logger.LogInformation("Claimed {Count} messages from stream {Stream} for consumer {Consumer}",
            result.Count, prefixedStream, consumerName);
        return result;
    }

    public async Task<StreamInfo> GetInfoAsync(string stream, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        var prefixedStream = $"{_keyPrefix}{stream}";

        _logger.LogDebug("Getting info for stream {Stream}", prefixedStream);
        var database = await GetDatabaseAsync();

        // Get stream info
        var info = await database.StreamInfoAsync(prefixedStream).ConfigureAwait(false);

        _logger.LogDebug("Retrieved info for stream {Stream}: Length={Length}, FirstEntry={FirstEntry}, LastEntry={LastEntry}",
            prefixedStream, info.Length, info.FirstEntry.Id, info.LastEntry.Id);

        return info;
    }

    public async Task<StreamPendingMessageInfo[]> GetPendingAsync(string stream, string groupName, int count = 10, string? consumerName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        if (string.IsNullOrEmpty(groupName))
            throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));

        var prefixedStream = $"{_keyPrefix}{stream}";

        try
        {
            _logger.LogDebug("Getting pending messages for stream {Stream}, group {Group}",
                prefixedStream, groupName);
            var database = await GetDatabaseAsync();

            // Get pending messages
            StreamPendingMessageInfo[] pendingMessages;

            if (string.IsNullOrEmpty(consumerName))
                // Get all pending messages for the group
                pendingMessages = await database.StreamPendingMessagesAsync(
                    prefixedStream,
                    groupName,
                    count,
                    RedisValue.Null);
            else
                // Get pending messages for specific consumer
                pendingMessages = await database.StreamPendingMessagesAsync(
                    prefixedStream,
                    groupName,
                    count,
                    consumerName);

            _logger.LogDebug("Retrieved {Count} pending messages for stream {Stream}, group {Group}",
                pendingMessages.Length, prefixedStream, groupName);

            return pendingMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending messages for stream {Stream}, group {Group}",
                prefixedStream, groupName);
            throw;
        }
    }

    // ============= Critical Features Implementation =============

    public async Task<string> MoveToDeadLetterAsync<T>(
        string sourceStream,
        string deadLetterStream,
        string messageId,
        string reason,
        int retryCount = 0,
        string? groupName = null,
        string? consumerName = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrEmpty(sourceStream))
            throw new ArgumentException("Source stream cannot be null or empty", nameof(sourceStream));

        if (string.IsNullOrEmpty(deadLetterStream))
            throw new ArgumentException("Dead letter stream cannot be null or empty", nameof(deadLetterStream));

        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));

        // Validate stream names
        if (sourceStream.Length > 512) _logger.LogWarning("Source stream name exceeds Redis key limit of 512 characters: {Stream}", sourceStream);

        if (deadLetterStream.Length > 512) _logger.LogWarning("Dead letter stream name exceeds Redis key limit of 512 characters: {Stream}", deadLetterStream);

        var prefixedSourceStream = $"{_keyPrefix}{sourceStream}";
        var prefixedDeadLetterStream = $"{_keyPrefix}{deadLetterStream}";

        _logger.LogInformation("Moving message {MessageId} from {Source} to dead letter queue {DLQ}",
            messageId, prefixedSourceStream, prefixedDeadLetterStream);
        var database = await GetDatabaseAsync();

        // Read the original message
        var entries = await database.StreamRangeAsync(prefixedSourceStream, messageId, messageId, 1).ConfigureAwait(false);

        if (entries.Length == 0)
        {
            _logger.LogWarning("Message {MessageId} not found in stream {Stream}", messageId, prefixedSourceStream);
            return string.Empty;
        }

        var entry = entries[0];
        var value = entry.Values.FirstOrDefault(fv => fv.Name == "data").Value;

        T? originalMessage = null;
        if (!value.IsNullOrEmpty)
            try
            {
                originalMessage = await _serializer.DeserializeAsync<T>(value!, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize message {MessageId} for DLQ", messageId);
            }

        // Create dead letter message
        var deadLetterMessage = new DeadLetterMessage<T>
        {
            OriginalMessage = originalMessage,
            FailureReason = reason,
            FailedAt = DateTime.UtcNow,
            OriginalStream = sourceStream,
            OriginalMessageId = messageId,
            RetryCount = retryCount,
            ConsumerName = consumerName,
            GroupName = groupName
        };

        // Add to dead letter queue
        var dlqId = await AddAsync(deadLetterStream, deadLetterMessage, cancellationToken).ConfigureAwait(false);

        // Acknowledge the original message if group name is provided
        if (!string.IsNullOrEmpty(groupName))
            try
            {
                await AcknowledgeAsync(sourceStream, groupName, messageId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to acknowledge message {MessageId} after moving to DLQ", messageId);
            }

        _logger.LogInformation("Successfully moved message {MessageId} to DLQ with new ID {DLQId}", messageId, dlqId);
        return dlqId;
    }

    public async Task<RetryResult<T>> RetryPendingAsync<T>(
        string stream,
        string groupName,
        string consumerName,
        Func<T, Task<bool>> processor,
        RetryConfiguration? retryConfig = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ValidateRetryParameters(stream, groupName, consumerName, processor);

        retryConfig ??= new RetryConfiguration();
        var result = new RetryResult<T>();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting retry for pending messages in stream {Stream}, group {Group}", stream, groupName);

            var timedOutMessages = await GetTimedOutMessagesAsync(stream, groupName, consumerName, retryConfig, cancellationToken);
            _logger.LogDebug("Found {Count} timed out messages to retry", timedOutMessages.Count);

            foreach (var pendingMessage in timedOutMessages.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
                await ProcessPendingMessageAsync(
                    stream, groupName, consumerName,
                    pendingMessage.MessageId.ToString(),
                    processor, retryConfig, result, cancellationToken);

            result.ElapsedTime = DateTime.UtcNow - startTime;
            LogRetryCompletion(result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during retry operation for stream {Stream}", stream);
            throw;
        }
    }

    // ============= Important Features Implementation =============

    public async Task<string[]> AddBatchAsync<T>(
        string stream,
        T[] messages,
        int? maxLength = null,
        CancellationToken cancellationToken = default) where T : class
    {
        // Use validation helper
        StreamValidationHelper.ValidateBatchParameters(stream, messages);

        try
        {
            var startTime = DateTime.UtcNow;
            _logger.LogDebug("Adding {Count} messages to stream {Stream} in batch", messages.Length, stream);

            var messageIds = await ProcessBatchMessagesAsync(stream, messages, maxLength, cancellationToken);

            LogBatchCompletion(stream, messageIds.Length, startTime);
            return messageIds;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Batch add operation was cancelled for stream {Stream}", stream);
            throw;
        }
    }

    public async Task<StreamHealthInfo> GetHealthAsync(
        string stream,
        bool includeGroups = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        // Validate stream name
        if (stream.Length > 512) _logger.LogWarning("Stream name exceeds Redis key limit of 512 characters: {Stream}", stream);

        var prefixedStream = $"{_keyPrefix}{stream}";
        var health = new StreamHealthInfo();

        try
        {
            // Validate stream name
            if (stream.Length > 512) _logger.LogWarning("Stream name exceeds Redis key limit of 512 characters: {Stream}", stream);

            _logger.LogDebug("Checking health for stream {Stream}", prefixedStream);
            var database = await GetDatabaseAsync();

            // Get stream info
            var streamInfo = await database.StreamInfoAsync(prefixedStream).ConfigureAwait(false);
            health.Length = streamInfo.Length;

            if (includeGroups)
                try
                {
                    // Get consumer groups info
                    var groups = await database.StreamGroupInfoAsync(prefixedStream).ConfigureAwait(false);
                    health.ConsumerGroupCount = groups.Length;

                    // Calculate total pending messages and oldest pending age
                    long totalPending = 0;
                    long oldestPendingTime = 0;

                    foreach (var group in groups)
                    {
                        totalPending += group.PendingMessageCount;

                        // Get detailed pending info for the group
                        var pendingInfo = await database.StreamPendingAsync(prefixedStream, group.Name).ConfigureAwait(false);
                        if (pendingInfo.LowestPendingMessageId != RedisValue.Null && pendingInfo.HighestPendingMessageId != RedisValue.Null)
                        {
                            // Parse the timestamp from the message ID (format: timestamp-sequence)
                            var lowestId = pendingInfo.LowestPendingMessageId.ToString();
                            if (lowestId.Contains('-'))
                            {
                                var timestampStr = lowestId.Split('-')[0];
                                if (long.TryParse(timestampStr, out var timestamp))
                                {
                                    var messageTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
                                    var age = (long)(DateTime.UtcNow - messageTime.UtcDateTime).TotalMilliseconds;
                                    if (age > oldestPendingTime) oldestPendingTime = age;
                                }
                            }
                        }
                    }

                    health.TotalPendingMessages = totalPending;
                    health.OldestPendingAge = TimeSpan.FromMilliseconds(oldestPendingTime);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get consumer group info for stream {Stream}", prefixedStream);
                }

            // Determine health status
            health.IsHealthy = health.Length < 1_000_000 && // Less than 1M messages
                               health.OldestPendingAge < TimeSpan.FromHours(1) && // No message older than 1 hour
                               health.TotalPendingMessages < 10_000; // Less than 10K pending

            health.HealthMessage = health.IsHealthy
                ? "Stream is healthy"
                : $"Stream health issues: Length={health.Length}, OldestPending={health.OldestPendingAge}, TotalPending={health.TotalPendingMessages}";

            health.CheckedAt = DateTime.UtcNow;

            _logger.LogInformation("Stream {Stream} health: {IsHealthy} - {Message}",
                prefixedStream, health.IsHealthy, health.HealthMessage);

            return health;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health for stream {Stream}", prefixedStream);
            throw;
        }
    }

    public async Task<StreamMetrics> GetMetricsAsync(
        string stream,
        string? groupName = null,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        // Validate stream name
        if (stream.Length > 512) _logger.LogWarning("Stream name exceeds Redis key limit of 512 characters: {Stream}", stream);

        var prefixedStream = $"{_keyPrefix}{stream}";
        var metrics = new StreamMetrics();
        var measurementWindow = window ?? TimeSpan.FromMinutes(5);

        try
        {
            _logger.LogDebug("Collecting metrics for stream {Stream}", prefixedStream);
            var database = await GetDatabaseAsync();

            // Get stream info
            var streamInfo = await database.StreamInfoAsync(prefixedStream).ConfigureAwait(false);
            metrics.TotalMessages = streamInfo.Length;
            metrics.MeasurementWindow = measurementWindow;

            // Calculate messages per second based on recent messages
            var endTime = DateTime.UtcNow;
            var startTime = endTime.Subtract(measurementWindow);
            var startId = $"{new DateTimeOffset(startTime).ToUnixTimeMilliseconds()}-0";
            var endId = $"{new DateTimeOffset(endTime).ToUnixTimeMilliseconds()}-0";

            var recentMessages = await database.StreamRangeAsync(prefixedStream, startId, endId, -1).ConfigureAwait(false);
            metrics.MessagesPerSecond = recentMessages.Length / measurementWindow.TotalSeconds;

            // Get consumer group metrics if specified
            if (!string.IsNullOrEmpty(groupName))
                try
                {
                    var groupInfo = await database.StreamGroupInfoAsync(prefixedStream).ConfigureAwait(false);
                    var group = groupInfo.FirstOrDefault(g => g.Name == groupName);

                    if (!string.IsNullOrEmpty(group.Name))
                    {
                        metrics.PendingMessages = group.PendingMessageCount;
                        metrics.TotalConsumers = group.ConsumerCount;

                        // Calculate average processing time for pending messages
                        var pendingDetails = await database.StreamPendingMessagesAsync(
                            prefixedStream, groupName, 100, RedisValue.Null);

                        if (pendingDetails.Length > 0)
                        {
                            var totalIdleTime = pendingDetails.Sum(p => p.IdleTimeInMilliseconds);
                            metrics.AverageProcessingTime = TimeSpan.FromMilliseconds(
                                totalIdleTime / pendingDetails.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get group metrics for {Group}", groupName);
                }
            else
                // Get metrics for all groups
                try
                {
                    var groups = await database.StreamGroupInfoAsync(prefixedStream).ConfigureAwait(false);
                    metrics.PendingMessages = groups.Sum(g => g.PendingMessageCount);
                    metrics.TotalConsumers = groups.Sum(g => g.ConsumerCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get group metrics for stream {Stream}", prefixedStream);
                }

            // Check for dead letter queue
            var dlqStream = $"{stream}:dlq";
            try
            {
                var dlqInfo = await database.StreamInfoAsync($"{_keyPrefix}{dlqStream}").ConfigureAwait(false);
                metrics.DeadLetterCount = dlqInfo.Length;
            }
            catch
            {
                // DLQ might not exist
                metrics.DeadLetterCount = 0;
            }

            metrics.CollectedAt = DateTime.UtcNow;

            _logger.LogInformation("Collected metrics for stream {Stream}: Messages={Total}, Pending={Pending}, Rate={Rate}/s",
                prefixedStream, metrics.TotalMessages, metrics.PendingMessages, metrics.MessagesPerSecond.ToString("F2"));

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting metrics for stream {Stream}", prefixedStream);
            throw;
        }
    }

    public async Task<int> ReadGroupWithAutoAckAsync<T>(
        string stream,
        string groupName,
        string consumerName,
        Func<T, Task<bool>> processor,
        int count = 10,
        CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        if (string.IsNullOrEmpty(groupName))
            throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));

        if (string.IsNullOrEmpty(consumerName))
            throw new ArgumentException("Consumer name cannot be null or empty", nameof(consumerName));

        if (processor == null)
            throw new ArgumentNullException(nameof(processor));

        try
        {
            _logger.LogDebug("Reading from stream {Stream} with auto-ack for group {Group}, consumer {Consumer}",
                stream, groupName, consumerName);

            var messages = await ReadGroupAsync<T>(stream, groupName, consumerName, count, cancellationToken);
            var successCount = 0;

            foreach (var kvp in messages)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (kvp.Value != null)
                    try
                    {
                        var success = await processor(kvp.Value);

                        if (success)
                        {
                            await AcknowledgeAsync(stream, groupName, kvp.Key, cancellationToken).ConfigureAwait(false);
                            successCount++;

                            _logger.LogDebug("Successfully processed and acknowledged message {MessageId}", kvp.Key);
                        }
                        else
                        {
                            _logger.LogWarning("Message {MessageId} processing returned false, not acknowledging", kvp.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message {MessageId}", kvp.Key);
                    }
            }

            _logger.LogInformation("Processed {Success}/{Total} messages with auto-ack from stream {Stream}",
                successCount, messages.Count, stream);

            return successCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ReadGroupWithAutoAck for stream {Stream}", stream);
            throw;
        }
    }

    public void SetKeyPrefix(string prefix)
    {
        _keyPrefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
    }

    private static void ValidateRetryParameters<T>(string stream, string groupName, string consumerName, Func<T, Task<bool>> processor) where T : class
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));
        if (string.IsNullOrEmpty(groupName))
            throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));
        if (string.IsNullOrEmpty(consumerName))
            throw new ArgumentException("Consumer name cannot be null or empty", nameof(consumerName));
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));
    }

    private static void ValidateStreamName(string stream, string paramName = "stream")
    {
        if (string.IsNullOrWhiteSpace(stream))
            throw new ArgumentException("Stream name cannot be null, empty or whitespace", paramName);

        // Redis key length limit check
        if (stream.Length > 512)
            throw new ArgumentException($"Stream name cannot exceed 512 characters (was {stream.Length})", paramName);
    }

    private async Task<List<StreamPendingMessageInfo>> GetTimedOutMessagesAsync(
        string stream, string groupName, string consumerName,
        RetryConfiguration retryConfig, CancellationToken cancellationToken)
    {
        var pendingMessages = await GetPendingAsync(stream, groupName, 100, consumerName, cancellationToken).ConfigureAwait(false);
        return pendingMessages.Where(p => p.IdleTimeInMilliseconds > retryConfig.IdleTimeout.TotalMilliseconds).ToList();
    }

    private async Task ProcessPendingMessageAsync<T>(
        string stream, string groupName, string consumerName,
        string messageId, Func<T, Task<bool>> processor,
        RetryConfiguration retryConfig, RetryResult<T> result,
        CancellationToken cancellationToken) where T : class
    {
        var processed = await RetryMessageProcessingAsync(
            stream, groupName, consumerName, messageId,
            processor, retryConfig, result, cancellationToken);

        if (!processed)
        {
            result.FailureCount++;
            await HandleFailedMessageAsync(stream, messageId, retryConfig, result, groupName, consumerName, cancellationToken);
        }
    }

    private async Task<bool> RetryMessageProcessingAsync<T>(
        string stream, string groupName, string consumerName,
        string messageId, Func<T, Task<bool>> processor,
        RetryConfiguration retryConfig, RetryResult<T> result,
        CancellationToken cancellationToken) where T : class
    {
        // Use retry helper for simplified retry logic
        var success = await StreamRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                var claimed = await ClaimAsync<T>(
                    stream, groupName, consumerName,
                    (long)retryConfig.IdleTimeout.TotalMilliseconds,
                    new[] { messageId }, cancellationToken);

                if (claimed.Count == 0 || !claimed.TryGetValue(messageId, out var message))
                {
                    _logger.LogWarning("Failed to claim message {MessageId}", messageId);
                    return false;
                }

                try
                {
                    var processed = await processor(message!);
                    if (processed)
                    {
                        await AcknowledgeAsync(stream, groupName, messageId, cancellationToken);
                        result.SuccessCount++;
                        result.ProcessedMessages[messageId] = message!;
                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message {MessageId}", messageId);
                    result.FailedMessages[messageId] = ex.Message;
                    throw;
                }
            },
            retryConfig,
            _logger,
            $"ProcessMessage-{messageId}",
            cancellationToken);

        return success;
    }

    // TryProcessMessageAsync and DelayBeforeRetryAsync removed - functionality merged into RetryMessageProcessingAsync using StreamRetryHelper

    private async Task HandleFailedMessageAsync<T>(
        string stream, string messageId, RetryConfiguration retryConfig,
        RetryResult<T> result, string groupName, string consumerName,
        CancellationToken cancellationToken) where T : class
    {
        if (!retryConfig.MoveToDeadLetterQueue)
            return;

        try
        {
            var dlqStream = $"{stream}{retryConfig.DeadLetterSuffix}";
            await MoveToDeadLetterAsync<T>(
                stream, dlqStream, messageId,
                $"Failed after {retryConfig.MaxRetries} retries",
                retryConfig.MaxRetries, groupName, consumerName, cancellationToken);

            result.DeadLetterCount++;
            _logger.LogWarning("Moved message {MessageId} to dead letter queue after {MaxRetries} retries",
                messageId, retryConfig.MaxRetries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move message {MessageId} to dead letter queue", messageId);
        }
    }

    private void LogRetryCompletion<T>(RetryResult<T> result) where T : class
    {
        _logger.LogInformation("Retry operation completed. Success: {Success}, Failed: {Failed}, DLQ: {DLQ}, Time: {Time}ms",
            result.SuccessCount, result.FailureCount, result.DeadLetterCount, result.ElapsedTime.TotalMilliseconds);
    }

    // ValidateBatchParameters removed - now using StreamValidationHelper.ValidateBatchParameters

    private async Task<string[]> ProcessBatchMessagesAsync<T>(
        string stream, T[] messages, int? maxLength,
        CancellationToken cancellationToken) where T : class
    {
        const int optimalBatchSize = 100;
        var prefixedStream = $"{_keyPrefix}{stream}";

        // For small batches, use batch operations
        if (messages.Length <= optimalBatchSize)
        {
            var database = (await _connection.GetMultiplexerAsync()).GetDatabase();
            var batch = database.CreateBatch();
            var tasks = new List<Task<RedisValue>>(messages.Length);

            // Use serialization helper for batch serialization
            var serializedEntries = await StreamSerializationHelper.BatchSerializeAsync(messages, _serializer);

            for (var i = 0; i < serializedEntries.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var task = batch.StreamAddAsync(prefixedStream, serializedEntries[i], null, maxLength, true);
                tasks.Add(task);
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            return tasks.Select(t => t.Result.ToString()).ToArray();
        }

        // For large batches, use parallel processing
        var results = new ConcurrentBag<(int index, string id)>();

        await Parallel.ForEachAsync(
            messages.Select((msg, idx) => (message: msg, index: idx)),
            CreateParallelOptions(cancellationToken),
            async (item, ct) =>
            {
                var messageId = await AddAsync(stream, item.message, maxLength, ct).ConfigureAwait(false);
                results.Add((item.index, messageId));
                LogBatchProgress(stream, item.index, messages.Length);
            });

        return results
            .OrderBy(r => r.index)
            .Select(r => r.id)
            .ToArray();
    }

    private static ParallelOptions CreateParallelOptions(CancellationToken cancellationToken)
    {
        // Dynamic parallelism: Adjust based on CPU count, max 8
        var maxDegree = Math.Min(Environment.ProcessorCount * 2, 8);

        return new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegree,
            CancellationToken = cancellationToken
        };
    }

    // Removed AddSingleMessageAsync - no longer needed after consolidation

    private void LogBatchProgress(string stream, int currentIndex, int totalMessages)
    {
        if (totalMessages > 100 && (currentIndex + 1) % 100 == 0)
            _logger.LogDebug("Batch progress: {Processed}/{Total} messages added to stream {Stream}",
                currentIndex + 1, totalMessages, stream);
    }

    private void LogBatchCompletion(string stream, int messageCount, DateTime startTime)
    {
        var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
        var rate = elapsedMs > 0 ? messageCount / (elapsedMs / 1000) : 0;

        _logger.LogInformation("Successfully added {Count} messages to stream {Stream} in {ElapsedMs}ms ({Rate} msgs/sec)",
            messageCount, stream, elapsedMs, rate);
    }


    /// <summary>
    ///     Memory-efficient streaming reader for large datasets
    /// </summary>
    public async IAsyncEnumerable<(string Id, T? Data)> ReadStreamingAsync<T>(
        string stream,
        string? start = null,
        int batchSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

        var prefixedStream = $"{_keyPrefix}{stream}";
        var currentStart = start ?? "0";

        while (!cancellationToken.IsCancellationRequested)
        {
            var database = await GetDatabaseAsync();
            var entries = await database.StreamRangeAsync(
                prefixedStream,
                currentStart,
                "+",
                batchSize).ConfigureAwait(false);

            if (entries.Length == 0)
                yield break;

            foreach (var entry in entries)
            {
                var value = StreamSerializationHelper.GetDataFieldValue(entry);
                if (!value.IsNullOrEmpty)
                {
                    T? data = null;
                    try
                    {
                        data = await _serializer.DeserializeAsync<T>(value!, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deserializing message from stream {Stream} with ID {Id}",
                            prefixedStream, entry.Id);
                    }

                    yield return (entry.Id!, data);
                }

                currentStart = entry.Id!;
            }

            // If we got less than batch size, we've reached the end
            if (entries.Length < batchSize)
                yield break;
        }
    }

    /// <summary>
    ///     Streaming consumer group reader with automatic acknowledgment
    /// </summary>
    public async IAsyncEnumerable<(string Id, T? Data, Func<Task> AckFunc)> ReadGroupStreamingAsync<T>(
        string stream,
        string groupName,
        string consumerName,
        int batchSize = 10,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrEmpty(stream))
            throw new ArgumentException("Stream cannot be null or empty", nameof(stream));
        if (string.IsNullOrEmpty(groupName))
            throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));
        if (string.IsNullOrEmpty(consumerName))
            throw new ArgumentException("Consumer name cannot be null or empty", nameof(consumerName));

        var prefixedStream = $"{_keyPrefix}{stream}";

        while (!cancellationToken.IsCancellationRequested)
        {
            var database = await GetDatabaseAsync();
            var entries = await database.StreamReadGroupAsync(
                prefixedStream,
                groupName,
                consumerName,
                ">",
                batchSize).ConfigureAwait(false);

            if (entries.Length == 0)
            {
                // No new messages, wait a bit before checking again
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var entry in entries)
            {
                var value = StreamSerializationHelper.GetDataFieldValue(entry);
                T? data = null;

                if (!value.IsNullOrEmpty)
                    try
                    {
                        data = await _serializer.DeserializeAsync<T>(value!, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deserializing message from stream {Stream} with ID {Id}",
                            prefixedStream, entry.Id);
                    }

                // Create acknowledgment function closure
                var messageId = entry.Id.ToString();
                if (!string.IsNullOrEmpty(messageId))
                {
                    var ackFunc = async () => { await AcknowledgeAsync(stream, groupName, messageId, cancellationToken); };

                    yield return (messageId, data, ackFunc);
                }
            }
        }
    }

    private Task<IDatabaseAsync> GetDatabaseAsync()
    {
        return _connection.GetDatabaseAsync();
    }
}