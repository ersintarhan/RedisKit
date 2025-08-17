using Microsoft.Extensions.Logging;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;

namespace RedisKit.Services
{
    /// <summary>
    /// Implementation of IRedisStreamService using StackExchange.Redis with advanced stream operations.
    /// Provides comprehensive support for Redis Streams including consumer groups, message acknowledgment, and dead letter queues.
    /// </summary>
    /// <remarks>
    /// This class implements Redis Streams operations with enterprise-grade features for event sourcing,
    /// message queuing, and real-time data processing scenarios.
    /// 
    /// Thread Safety: This class is fully thread-safe and designed for singleton usage.
    /// 
    /// Core Capabilities:
    /// - Append-only log with automatic ID generation
    /// - Consumer groups for distributed processing
    /// - Message acknowledgment and retry mechanisms
    /// - Dead letter queue support for failed messages
    /// - Batch operations for high throughput
    /// - Stream trimming for memory management
    /// - Pending message recovery
    /// 
    /// Stream Features:
    /// - Auto-generated IDs based on timestamp-sequence format
    /// - MAXLEN trimming (approximate or exact)
    /// - Range queries with start/end IDs
    /// - Consumer group coordination
    /// - Pending entries list (PEL) management
    /// - Message claiming for failure recovery
    /// 
    /// Consumer Groups:
    /// - Multiple consumers per group with load balancing
    /// - At-least-once delivery semantics
    /// - Message acknowledgment tracking
    /// - Automatic message assignment
    /// - Idle message detection and recovery
    /// - Consumer health monitoring
    /// 
    /// Reliability Features:
    /// - Retry mechanism with exponential backoff
    /// - Dead letter queue for persistent failures
    /// - Message claiming from failed consumers
    /// - Pending message timeout detection
    /// - Automatic reconnection on failures
    /// 
    /// Performance Optimizations:
    /// - Batch message addition for throughput
    /// - Efficient serialization with configurable formats
    /// - Approximate trimming for better performance
    /// - Parallel message processing support
    /// - Memory-efficient streaming operations
    /// 
    /// Common Use Cases:
    /// - Event Sourcing: Append-only event log with replay
    /// - Message Queue: Reliable message delivery with acknowledgment
    /// - Activity Feed: Real-time updates with consumer groups
    /// - IoT Data: High-volume sensor data ingestion
    /// - Audit Log: Immutable audit trail with timestamps
    /// - CQRS: Command/event separation with streams
    /// 
    /// Usage Example:
    /// <code>
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
        private readonly IDatabaseAsync _database;
        private readonly ILogger<RedisStreamService> _logger;
        private readonly RedisOptions _options;
        private readonly IRedisSerializer _serializer;
        private string _keyPrefix = string.Empty;

        public RedisStreamService(
            IDatabaseAsync database,
            ILogger<RedisStreamService> logger,
            RedisOptions options)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // Create serializer based on configuration
            _serializer = RedisSerializerFactory.Create(_options.Serializer, null);
        }

        public void SetKeyPrefix(string prefix)
        {
            _keyPrefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        }

        public async Task<string> AddAsync<T>(string stream, T message, CancellationToken cancellationToken = default) where T : class
        {
            return await AddAsync(stream, message, null, cancellationToken);
        }

        public async Task<string> AddAsync<T>(string stream, T message, int? maxLength, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(stream))
                throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var prefixedStream = $"{_keyPrefix}{stream}";

            try
            {
                Logging.LoggingExtensions.LogAddAsync(_logger, prefixedStream);

                var serializedMessage = await _serializer.SerializeAsync(message, cancellationToken: cancellationToken);

                // Create a NameValueEntry array for the stream entry
                var entry = new NameValueEntry[]
                {
                    new("data", serializedMessage)
                };

                // Add to stream and get the message ID
                // If maxLength is specified, the stream will be trimmed to approximately that length
                var messageId = await _database.StreamAddAsync(
                    prefixedStream,
                    entry,
                    messageId: null,  // Auto-generate ID
                    maxLength: maxLength,
                    useApproximateMaxLength: true);  // Use ~ for approximate trimming (more efficient)

                Logging.LoggingExtensions.LogAddAsyncSuccess(_logger, prefixedStream, messageId.ToString());
                return messageId.ToString();
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogAddAsyncError(_logger, prefixedStream, ex);
                throw;
            }
        }

        public async Task<Dictionary<string, T?>> ReadAsync<T>(string stream, string? start = null, string? end = null, int count = 10, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(stream))
                throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

            var prefixedStream = $"{_keyPrefix}{stream}";

            try
            {
                Logging.LoggingExtensions.LogReadAsync(_logger, prefixedStream, start ?? "0", end ?? "+", count);

                // Read from stream
                var entries = await _database.StreamRangeAsync(prefixedStream, start ?? "0", end ?? "+", count);

                var result = new Dictionary<string, T?>();

                foreach (var entry in entries)
                {
                    // Extract the data field and deserialize
                    var value = default(RedisValue);
                    foreach (var fieldValue in entry.Values)
                    {
                        if (fieldValue.Name == "data")
                        {
                            value = fieldValue.Value;
                            break;
                        }
                    }

                    if (!value.IsNullOrEmpty)
                    {
                        try
                        {
                            var deserialized = await _serializer.DeserializeAsync<T>(value!, cancellationToken: cancellationToken);
                            result[entry.Id!] = deserialized;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deserializing message from stream {Stream} with ID {Id}", prefixedStream, entry.Id);
                            result[entry.Id!] = null;
                        }
                    }
                }

                Logging.LoggingExtensions.LogReadAsyncSuccess(_logger, prefixedStream, result.Count);
                return result;
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogReadAsyncError(_logger, prefixedStream, ex);
                throw;
            }
        }

        public async Task CreateConsumerGroupAsync(string stream, string groupName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(stream))
                throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));

            var prefixedStream = $"{_keyPrefix}{stream}";

            try
            {
                Logging.LoggingExtensions.LogCreateConsumerGroupAsync(_logger, prefixedStream, groupName);

                // Create consumer group starting from the beginning of the stream
                // The 'false' parameter means don't create the stream if it doesn't exist
                await _database.StreamCreateConsumerGroupAsync(
                    prefixedStream,
                    groupName,
                    StreamPosition.Beginning,
                    false);

                Logging.LoggingExtensions.LogCreateConsumerGroupAsyncSuccess(_logger, prefixedStream, groupName);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // Consumer group already exists - this is not an error
                _logger.LogInformation("Consumer group {GroupName} already exists for stream {Stream}", groupName, prefixedStream);
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogCreateConsumerGroupAsyncError(_logger, prefixedStream, groupName, ex);
                throw;
            }
        }

        public async Task<Dictionary<string, T?>> ReadGroupAsync<T>(string stream, string groupName, string consumerName, int count = 10, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrEmpty(stream))
                throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));

            if (string.IsNullOrEmpty(consumerName))
                throw new ArgumentException("Consumer name cannot be null or empty", nameof(consumerName));

            var prefixedStream = $"{_keyPrefix}{stream}";

            try
            {
                Logging.LoggingExtensions.LogReadGroupAsync(_logger, prefixedStream, groupName, consumerName, count);

                // Read from stream using consumer group
                // ">" means read only new messages (not yet delivered to this consumer)
                var entries = await _database.StreamReadGroupAsync(
                    prefixedStream,
                    groupName,
                    consumerName,
                    ">",  // Read new messages
                    count);

                var result = new Dictionary<string, T?>();

                foreach (var entry in entries)
                {
                    // Extract the data field and deserialize
                    var value = default(RedisValue);
                    foreach (var fieldValue in entry.Values)
                    {
                        if (fieldValue.Name == "data")
                        {
                            value = fieldValue.Value;
                            break;
                        }
                    }

                    if (!value.IsNullOrEmpty)
                    {
                        try
                        {
                            var deserialized = await _serializer.DeserializeAsync<T>(value, cancellationToken: cancellationToken);
                            result[entry.Id] = deserialized;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deserializing message from stream {Stream} with ID {Id}", prefixedStream, entry.Id);
                            result[entry.Id] = null;
                        }
                    }
                }

                Logging.LoggingExtensions.LogReadGroupAsyncSuccess(_logger, prefixedStream, groupName, consumerName, result.Count);
                return result;
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogReadGroupAsyncError(_logger, prefixedStream, groupName, consumerName, ex);
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
                Logging.LoggingExtensions.LogAcknowledgeAsync(_logger, prefixedStream, groupName, messageId);

                // Acknowledge the message
                await _database.StreamAcknowledgeAsync(prefixedStream, groupName, messageId);

                Logging.LoggingExtensions.LogAcknowledgeAsyncSuccess(_logger, prefixedStream, groupName, messageId);
            }
            catch (Exception ex)
            {
                Logging.LoggingExtensions.LogAcknowledgeAsyncError(_logger, prefixedStream, groupName, messageId, ex);
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

            try
            {
                _logger.LogDebug("Deleting {Count} messages from stream {Stream}", messageIds.Length, prefixedStream);

                // Convert string array to RedisValue array
                var redisMessageIds = Array.ConvertAll(messageIds, id => (RedisValue)id);

                // Delete messages from stream
                var deletedCount = await _database.StreamDeleteAsync(prefixedStream, redisMessageIds);

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

                // Trim the stream
                var trimmedCount = await _database.StreamTrimAsync(
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

            try
            {
                _logger.LogDebug("Claiming {Count} messages from stream {Stream} for consumer {Consumer}",
                    messageIds.Length, prefixedStream, consumerName);

                // Convert string array to RedisValue array
                var redisMessageIds = Array.ConvertAll(messageIds, id => (RedisValue)id);

                // Claim the messages
                var entries = await _database.StreamClaimAsync(
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
                    {
                        try
                        {
                            var deserialized = await _serializer.DeserializeAsync<T>(value, cancellationToken: cancellationToken);
                            result[entry.Id] = deserialized;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deserializing claimed message from stream {Stream} with ID {Id}",
                                prefixedStream, entry.Id);
                            result[entry.Id] = null;
                        }
                    }
                }

                _logger.LogInformation("Claimed {Count} messages from stream {Stream} for consumer {Consumer}",
                    result.Count, prefixedStream, consumerName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error claiming messages from stream {Stream}", prefixedStream);
                throw;
            }
        }

        public async Task<StreamInfo> GetInfoAsync(string stream, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(stream))
                throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

            var prefixedStream = $"{_keyPrefix}{stream}";

            try
            {
                _logger.LogDebug("Getting info for stream {Stream}", prefixedStream);

                // Get stream info
                var info = await _database.StreamInfoAsync(prefixedStream);

                _logger.LogDebug("Retrieved info for stream {Stream}: Length={Length}, FirstEntry={FirstEntry}, LastEntry={LastEntry}",
                    prefixedStream, info.Length, info.FirstEntry.Id, info.LastEntry.Id);

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting info for stream {Stream}", prefixedStream);
                throw;
            }
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

                // Get pending messages
                StreamPendingMessageInfo[] pendingMessages;

                if (string.IsNullOrEmpty(consumerName))
                {
                    // Get all pending messages for the group
                    pendingMessages = await _database.StreamPendingMessagesAsync(
                        prefixedStream,
                        groupName,
                        count,
                        RedisValue.Null);
                }
                else
                {
                    // Get pending messages for specific consumer
                    pendingMessages = await _database.StreamPendingMessagesAsync(
                        prefixedStream,
                        groupName,
                        count,
                        consumerName);
                }

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

            var prefixedSourceStream = $"{_keyPrefix}{sourceStream}";
            var prefixedDeadLetterStream = $"{_keyPrefix}{deadLetterStream}";

            try
            {
                _logger.LogInformation("Moving message {MessageId} from {Source} to dead letter queue {DLQ}",
                    messageId, prefixedSourceStream, prefixedDeadLetterStream);

                // Read the original message
                var entries = await _database.StreamRangeAsync(prefixedSourceStream, messageId, messageId, 1);

                if (entries.Length == 0)
                {
                    _logger.LogWarning("Message {MessageId} not found in stream {Stream}", messageId, prefixedSourceStream);
                    return string.Empty;
                }

                var entry = entries[0];
                var value = entry.Values.FirstOrDefault(fv => fv.Name == "data").Value;

                T? originalMessage = null;
                if (!value.IsNullOrEmpty)
                {
                    try
                    {
                        originalMessage = await _serializer.DeserializeAsync<T>(value, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize message {MessageId} for DLQ", messageId);
                    }
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
                var dlqId = await AddAsync(deadLetterStream, deadLetterMessage, cancellationToken: cancellationToken);

                // Acknowledge the original message if group name is provided
                if (!string.IsNullOrEmpty(groupName))
                {
                    try
                    {
                        await AcknowledgeAsync(sourceStream, groupName, messageId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to acknowledge message {MessageId} after moving to DLQ", messageId);
                    }
                }

                _logger.LogInformation("Successfully moved message {MessageId} to DLQ with new ID {DLQId}", messageId, dlqId);
                return dlqId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moving message {MessageId} to dead letter queue", messageId);
                throw;
            }
        }

        public async Task<RetryResult<T>> RetryPendingAsync<T>(
            string stream,
            string groupName,
            string consumerName,
            Func<T, Task<bool>> processor,
            RetryConfiguration? retryConfig = null,
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

            retryConfig ??= new RetryConfiguration();
            var result = new RetryResult<T>();
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Starting retry for pending messages in stream {Stream}, group {Group}", stream, groupName);

                // Get pending messages
                var pendingMessages = await GetPendingAsync(stream, groupName, 100, consumerName, cancellationToken);

                // Filter messages that have exceeded idle timeout
                var timedOutMessages = pendingMessages.Where(p =>
                    p.IdleTimeInMilliseconds > retryConfig.IdleTimeout.TotalMilliseconds).ToList();

                _logger.LogDebug("Found {Count} timed out messages to retry", timedOutMessages.Count);

                foreach (var pendingMessage in timedOutMessages)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var messageId = pendingMessage.MessageId.ToString();
                    var currentRetry = 0;
                    var processed = false;

                    while (currentRetry < retryConfig.MaxRetries && !processed)
                    {
                        try
                        {
                            // Claim the message
                            var claimed = await ClaimAsync<T>(
                                stream,
                                groupName,
                                consumerName,
                                (long)retryConfig.IdleTimeout.TotalMilliseconds,
                                new[] { messageId },
                                cancellationToken);

                            if (claimed.Count > 0 && claimed.ContainsKey(messageId))
                            {
                                var message = claimed[messageId];
                                if (message != null)
                                {
                                    // Process the message
                                    var success = await processor(message);

                                    if (success)
                                    {
                                        // Acknowledge the message
                                        await AcknowledgeAsync(stream, groupName, messageId, cancellationToken);
                                        result.SuccessCount++;
                                        result.ProcessedMessages[messageId] = message;
                                        processed = true;

                                        _logger.LogDebug("Successfully processed message {MessageId} on retry {Retry}",
                                            messageId, currentRetry + 1);
                                    }
                                    else
                                    {
                                        currentRetry++;

                                        if (currentRetry < retryConfig.MaxRetries)
                                        {
                                            // Wait before retry with exponential backoff if configured
                                            var delay = retryConfig.UseExponentialBackoff
                                                ? TimeSpan.FromMilliseconds(retryConfig.RetryDelay.TotalMilliseconds * Math.Pow(2, currentRetry))
                                                : retryConfig.RetryDelay;

                                            await Task.Delay(delay, cancellationToken);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Failed to claim message {MessageId}", messageId);
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing message {MessageId} on retry {Retry}",
                                messageId, currentRetry + 1);

                            result.FailedMessages[messageId] = ex.Message;
                            currentRetry++;

                            if (currentRetry < retryConfig.MaxRetries)
                            {
                                await Task.Delay(retryConfig.RetryDelay, cancellationToken);
                            }
                        }
                    }

                    // Move to dead letter queue if all retries failed
                    if (!processed)
                    {
                        result.FailureCount++;

                        if (retryConfig.MoveToDeadLetterQueue)
                        {
                            try
                            {
                                var dlqStream = $"{stream}{retryConfig.DeadLetterSuffix}";
                                await MoveToDeadLetterAsync<T>(
                                    stream,
                                    dlqStream,
                                    messageId,
                                    $"Failed after {retryConfig.MaxRetries} retries",
                                    retryConfig.MaxRetries,
                                    groupName,
                                    consumerName,
                                    cancellationToken);

                                result.DeadLetterCount++;
                                _logger.LogWarning("Moved message {MessageId} to dead letter queue after {MaxRetries} retries",
                                    messageId, retryConfig.MaxRetries);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to move message {MessageId} to dead letter queue", messageId);
                            }
                        }
                    }
                }

                result.ElapsedTime = DateTime.UtcNow - startTime;

                _logger.LogInformation("Retry operation completed. Success: {Success}, Failed: {Failed}, DLQ: {DLQ}, Time: {Time}ms",
                    result.SuccessCount, result.FailureCount, result.DeadLetterCount, result.ElapsedTime.TotalMilliseconds);

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
            if (string.IsNullOrEmpty(stream))
                throw new ArgumentException("Stream cannot be null or empty", nameof(stream));

            if (messages == null || messages.Length == 0)
                throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

            try
            {
                var startTime = DateTime.UtcNow;
                _logger.LogDebug("Adding {Count} messages to stream {Stream} in batch", messages.Length, stream);

                // Use ConcurrentBag to collect results in thread-safe manner
                var results = new System.Collections.Concurrent.ConcurrentBag<(int index, string id)>();

                // Process messages in parallel with controlled concurrency using Parallel.ForEachAsync
                await Parallel.ForEachAsync(
                    messages.Select((msg, idx) => (message: msg, index: idx)),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 10, // Max 10 concurrent operations
                        CancellationToken = cancellationToken
                    },
                    async (item, ct) =>
                    {
                        try
                        {
                            var messageId = await AddAsync(stream, item.message, maxLength, ct);
                            results.Add((item.index, messageId));

                            // Log progress every 100 messages for large batches
                            if (messages.Length > 100 && (item.index + 1) % 100 == 0)
                            {
                                _logger.LogDebug("Batch progress: {Processed}/{Total} messages added to stream {Stream}",
                                    item.index + 1, messages.Length, stream);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to add message at index {Index} to stream {Stream}",
                                item.index, stream);
                            throw; // Re-throw to fail the entire batch
                        }
                    });

                // Sort results by original index to maintain order
                var messageIds = results
                    .OrderBy(r => r.index)
                    .Select(r => r.id)
                    .ToArray();

                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("Successfully added {Count} messages to stream {Stream} in {ElapsedMs}ms ({Rate} msgs/sec)",
                    messageIds.Length, stream, elapsedMs,
                    messageIds.Length / (elapsedMs / 1000));

                return messageIds;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Batch add operation was cancelled for stream {Stream}", stream);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding batch messages to stream {Stream}", stream);
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

            var prefixedStream = $"{_keyPrefix}{stream}";
            var health = new StreamHealthInfo();

            try
            {
                _logger.LogDebug("Checking health for stream {Stream}", prefixedStream);

                // Get stream info
                var streamInfo = await _database.StreamInfoAsync(prefixedStream);
                health.Length = streamInfo.Length;

                if (includeGroups)
                {
                    try
                    {
                        // Get consumer groups info
                        var groups = await _database.StreamGroupInfoAsync(prefixedStream);
                        health.ConsumerGroupCount = groups.Length;

                        // Calculate total pending messages and oldest pending age
                        long totalPending = 0;
                        long oldestPendingTime = 0;

                        foreach (var group in groups)
                        {
                            totalPending += group.PendingMessageCount;

                            // Get detailed pending info for the group
                            var pendingInfo = await _database.StreamPendingAsync(prefixedStream, group.Name);
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
                                        if (age > oldestPendingTime)
                                        {
                                            oldestPendingTime = age;
                                        }
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

            var prefixedStream = $"{_keyPrefix}{stream}";
            var metrics = new StreamMetrics();
            var measurementWindow = window ?? TimeSpan.FromMinutes(5);

            try
            {
                _logger.LogDebug("Collecting metrics for stream {Stream}", prefixedStream);

                // Get stream info
                var streamInfo = await _database.StreamInfoAsync(prefixedStream);
                metrics.TotalMessages = streamInfo.Length;
                metrics.MeasurementWindow = measurementWindow;

                // Calculate messages per second based on recent messages
                var endTime = DateTime.UtcNow;
                var startTime = endTime.Subtract(measurementWindow);
                var startId = $"{new DateTimeOffset(startTime).ToUnixTimeMilliseconds()}-0";
                var endId = $"{new DateTimeOffset(endTime).ToUnixTimeMilliseconds()}-0";

                var recentMessages = await _database.StreamRangeAsync(prefixedStream, startId, endId, count: -1);
                metrics.MessagesPerSecond = recentMessages.Length / measurementWindow.TotalSeconds;

                // Get consumer group metrics if specified
                if (!string.IsNullOrEmpty(groupName))
                {
                    try
                    {
                        var groupInfo = await _database.StreamGroupInfoAsync(prefixedStream);
                        var group = groupInfo.FirstOrDefault(g => g.Name == groupName);

                        if (!string.IsNullOrEmpty(group.Name))
                        {
                            metrics.PendingMessages = group.PendingMessageCount;
                            metrics.TotalConsumers = group.ConsumerCount;

                            // Calculate average processing time for pending messages
                            var pendingDetails = await _database.StreamPendingMessagesAsync(
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
                }
                else
                {
                    // Get metrics for all groups
                    try
                    {
                        var groups = await _database.StreamGroupInfoAsync(prefixedStream);
                        metrics.PendingMessages = groups.Sum(g => g.PendingMessageCount);
                        metrics.TotalConsumers = groups.Sum(g => g.ConsumerCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get group metrics for stream {Stream}", prefixedStream);
                    }
                }

                // Check for dead letter queue
                var dlqStream = $"{stream}:dlq";
                try
                {
                    var dlqInfo = await _database.StreamInfoAsync($"{_keyPrefix}{dlqStream}");
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
                    {
                        try
                        {
                            var success = await processor(kvp.Value);

                            if (success)
                            {
                                await AcknowledgeAsync(stream, groupName, kvp.Key, cancellationToken);
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
    }
}