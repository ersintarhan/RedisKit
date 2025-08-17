using RedisKit.Models;
using StackExchange.Redis;

namespace RedisKit.Interfaces
{
    /// <summary>
    /// Interface for Redis Streams operations with generic support.
    /// Provides high-level abstractions for stream-based message processing and event sourcing.
    /// </summary>
    /// <remarks>
    /// Redis Streams provide an append-only log data structure with consumer groups support,
    /// making them ideal for event sourcing, message queuing, and real-time data processing.
    /// 
    /// Thread Safety: Implementations should be thread-safe and support concurrent operations.
    /// 
    /// Key Features:
    /// - Append-only log with automatic ID generation
    /// - Consumer groups for reliable message processing
    /// - Message acknowledgment and retry mechanisms
    /// - Dead letter queue support
    /// - Batch operations for high throughput
    /// - Stream trimming for memory management
    /// - Pending message handling
    /// 
    /// Common Use Cases:
    /// - Event sourcing and CQRS patterns
    /// - Message queue with at-least-once delivery
    /// - Activity feeds and timelines
    /// - IoT data ingestion
    /// - Audit logging
    /// - Real-time analytics pipelines
    /// 
    /// Usage Example:
    /// <code>
    /// public class OrderEventProcessor
    /// {
    ///     private readonly IRedisStreamService _streamService;
    ///     
    ///     public async Task PublishOrderEvent(OrderEvent orderEvent)
    ///     {
    ///         // Add event to stream with automatic trimming
    ///         var messageId = await _streamService.AddAsync(
    ///             "orders:events", 
    ///             orderEvent, 
    ///             maxLength: 10000);
    ///     }
    ///     
    ///     public async Task ProcessOrderEvents()
    ///     {
    ///         // Process events using consumer group
    ///         await _streamService.ReadGroupWithAutoAckAsync(
    ///             "orders:events",
    ///             "order-processors",
    ///             "processor-1",
    ///             async (OrderEvent evt) => 
    ///             {
    ///                 await HandleOrderEvent(evt);
    ///                 return true; // Auto-acknowledge on success
    ///             });
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public interface IRedisStreamService
    {
        /// <summary>
        /// Adds a message to a stream with automatic ID generation.
        /// </summary>
        /// <typeparam name="T">The type of message to add.</typeparam>
        /// <param name="stream">The stream name to add the message to.</param>
        /// <param name="message">The message object to add.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The generated message ID in format "timestamp-sequence".</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream or message is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when serialization fails.</exception>
        /// <remarks>
        /// The message ID is automatically generated based on the current timestamp.
        /// Messages are guaranteed to be ordered by their IDs.
        /// Use this overload when you don't need stream trimming.
        /// </remarks>
        /// <example>
        /// <code>
        /// var payment = new PaymentEvent { Amount = 100.50m, Currency = "USD" };
        /// var messageId = await streamService.AddAsync("payments:stream", payment).ConfigureAwait(false);
        /// Console.WriteLine($"Added message with ID: {messageId}");
        /// </code>
        /// </example>
        Task<string> AddAsync<T>(string stream, T message, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Adds a message to a stream with optional automatic trimming.
        /// </summary>
        /// <typeparam name="T">The type of message to add.</typeparam>
        /// <param name="stream">The stream name to add the message to.</param>
        /// <param name="message">The message object to add.</param>
        /// <param name="maxLength">Optional maximum stream length for automatic trimming.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The generated message ID.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream or message is null.</exception>
        /// <remarks>
        /// When maxLength is specified, the stream is automatically trimmed to approximately
        /// this length after adding the message, preventing unbounded memory growth.
        /// The trimming is approximate for better performance.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Keep only last 1000 messages in stream
        /// await streamService.AddAsync("events:stream", eventData, maxLength: 1000).ConfigureAwait(false);
        /// </code>
        /// </example>
        Task<string> AddAsync<T>(string stream, T message, int? maxLength, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Reads messages from a stream within a range.
        /// </summary>
        /// <typeparam name="T">The type of messages to read.</typeparam>
        /// <param name="stream">The stream name to read from.</param>
        /// <param name="start">Start ID or "-" for beginning. Null defaults to beginning.</param>
        /// <param name="end">End ID or "+" for end. Null defaults to end.</param>
        /// <param name="count">Maximum number of messages to return.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Dictionary mapping message IDs to deserialized messages.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <remarks>
        /// Use "-" as start to read from the beginning, "+" as end to read until the end.
        /// Specific IDs can be used for range queries (e.g., "1234567890-0").
        /// This method is for simple reading without consumer groups.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Read last 10 messages
        /// var messages = await streamService.ReadAsync&lt;LogEntry&gt;("logs:stream", count: 10);
        /// 
        /// // Read all messages after specific ID
        /// var newMessages = await streamService.ReadAsync&lt;LogEntry&gt;(
        ///     "logs:stream", 
        ///     start: "1234567890-0",
        ///     end: "+");
        /// </code>
        /// </example>
        Task<Dictionary<string, T?>> ReadAsync<T>(string stream, string? start = null, string? end = null, int count = 10, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Creates a consumer group for coordinated message processing.
        /// </summary>
        /// <param name="stream">The stream name to create the group for.</param>
        /// <param name="groupName">The name of the consumer group to create.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream or groupName is null.</exception>
        /// <remarks>
        /// Consumer groups enable multiple consumers to process messages from the same stream
        /// with automatic load balancing and message acknowledgment.
        /// If the group already exists, this operation is idempotent.
        /// The group starts reading from the end of the stream ("$").
        /// </remarks>
        /// <example>
        /// <code>
        /// // Create consumer group for order processing
        /// await streamService.CreateConsumerGroupAsync("orders:stream", "order-processors").ConfigureAwait(false);
        /// </code>
        /// </example>
        Task CreateConsumerGroupAsync(string stream, string groupName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads messages from a stream using a consumer group.
        /// </summary>
        /// <typeparam name="T">The type of messages to read.</typeparam>
        /// <param name="stream">The stream name to read from.</param>
        /// <param name="groupName">The consumer group name.</param>
        /// <param name="consumerName">The name of this consumer within the group.</param>
        /// <param name="count">Maximum number of messages to read.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Dictionary mapping message IDs to deserialized messages.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
        /// <remarks>
        /// Messages are automatically assigned to this consumer and must be acknowledged
        /// after successful processing. Unacknowledged messages become "pending" and can
        /// be claimed by other consumers after a timeout.
        /// </remarks>
        /// <example>
        /// <code>
        /// var messages = await streamService.ReadGroupAsync&lt;Order&gt;(
        ///     "orders:stream",
        ///     "order-processors",
        ///     "worker-1",
        ///     count: 5);
        /// 
        /// foreach (var (messageId, order) in messages)
        /// {
        ///     if (await ProcessOrder(order))
        ///     {
        ///         await streamService.AcknowledgeAsync("orders:stream", "order-processors", messageId).ConfigureAwait(false);
        ///     }
        /// }
        /// </code>
        /// </example>
        Task<Dictionary<string, T?>> ReadGroupAsync<T>(string stream, string groupName, string consumerName, int count = 10, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Acknowledges a message as successfully processed.
        /// </summary>
        /// <param name="stream">The stream name.</param>
        /// <param name="groupName">The consumer group name.</param>
        /// <param name="messageId">The message ID to acknowledge.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
        /// <remarks>
        /// Acknowledgment removes the message from the pending entries list (PEL).
        /// Only acknowledge messages after they have been successfully processed
        /// to ensure at-least-once delivery semantics.
        /// </remarks>
        Task AcknowledgeAsync(string stream, string groupName, string messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes specific messages from a stream.
        /// </summary>
        /// <param name="stream">The stream name.</param>
        /// <param name="messageIds">Array of message IDs to delete.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The number of messages actually deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream or messageIds is null.</exception>
        /// <remarks>
        /// Deleting messages creates gaps in the stream but doesn't affect ordering.
        /// Use trimming instead of deletion for memory management.
        /// Deleted messages cannot be recovered.
        /// </remarks>
        Task<long> DeleteAsync(string stream, string[] messageIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Trims the stream to a specified maximum length.
        /// </summary>
        /// <param name="stream">The stream name to trim.</param>
        /// <param name="maxLength">Maximum number of entries to keep.</param>
        /// <param name="useApproximateMaxLength">Use approximate trimming for better performance.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The number of messages trimmed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <exception cref="ArgumentException">Thrown when maxLength is not positive.</exception>
        /// <remarks>
        /// Approximate trimming (~) is more efficient as it trims only when it can remove
        /// a whole macro node. Use exact trimming only when precise length is critical.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Keep approximately last 1000 messages
        /// var trimmed = await streamService.TrimByLengthAsync("events:stream", 1000).ConfigureAwait(false);
        /// Console.WriteLine($"Trimmed {trimmed} old messages");
        /// </code>
        /// </example>
        Task<long> TrimByLengthAsync(string stream, int maxLength, bool useApproximateMaxLength = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Claims ownership of pending messages from other consumers.
        /// </summary>
        /// <typeparam name="T">The type of messages to claim.</typeparam>
        /// <param name="stream">The stream name.</param>
        /// <param name="groupName">The consumer group name.</param>
        /// <param name="consumerName">The consumer name claiming the messages.</param>
        /// <param name="minIdleTime">Minimum idle time in milliseconds before messages can be claimed.</param>
        /// <param name="messageIds">Specific message IDs to attempt claiming.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Dictionary of successfully claimed messages.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
        /// <remarks>
        /// Use this to recover messages from failed consumers. Messages can only be claimed
        /// if they have been idle (not acknowledged) for at least minIdleTime milliseconds.
        /// This is essential for implementing reliable message processing.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Claim messages idle for more than 5 minutes
        /// var claimedMessages = await streamService.ClaimAsync&lt;Order&gt;(
        ///     "orders:stream",
        ///     "order-processors",
        ///     "recovery-worker",
        ///     minIdleTime: 300000, // 5 minutes
        ///     messageIds: pendingMessageIds);
        /// </code>
        /// </example>
        Task<Dictionary<string, T?>> ClaimAsync<T>(string stream, string groupName, string consumerName, long minIdleTime, string[] messageIds, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Gets detailed information about a stream.
        /// </summary>
        /// <param name="stream">The stream name.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Stream information including length, first/last entry IDs, and consumer groups.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <remarks>
        /// Use this for monitoring stream health, checking message counts,
        /// and understanding consumer group state.
        /// </remarks>
        Task<StreamInfo> GetInfoAsync(string stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about pending messages for a consumer group.
        /// </summary>
        /// <param name="stream">The stream name.</param>
        /// <param name="groupName">The consumer group name.</param>
        /// <param name="count">Maximum number of pending messages to return.</param>
        /// <param name="consumerName">Optional consumer name to filter by.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Array of pending message information.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream or groupName is null.</exception>
        /// <remarks>
        /// Pending messages are those that have been delivered to consumers but not yet acknowledged.
        /// Monitor pending messages to detect stuck or failed message processing.
        /// </remarks>
        /// <example>
        /// <code>
        /// var pending = await streamService.GetPendingAsync(
        ///     "orders:stream",
        ///     "order-processors",
        ///     count: 100);
        /// 
        /// foreach (var msg in pending.Where(p => p.IdleTime > TimeSpan.FromMinutes(10)))
        /// {
        ///     Console.WriteLine($"Message {msg.MessageId} stuck for {msg.IdleTime}");
        /// }
        /// </code>
        /// </example>
        Task<StreamPendingMessageInfo[]> GetPendingAsync(string stream, string groupName, int count = 10, string? consumerName = null, CancellationToken cancellationToken = default);

        // ============= Critical Features =============

        /// <summary>
        /// Moves a failed message to a dead letter queue for manual inspection.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="sourceStream">The source stream name.</param>
        /// <param name="deadLetterStream">The dead letter queue stream name.</param>
        /// <param name="messageId">The message ID to move.</param>
        /// <param name="reason">The reason for moving to DLQ.</param>
        /// <param name="retryCount">Number of retries attempted before moving to DLQ.</param>
        /// <param name="groupName">Optional consumer group name for metadata.</param>
        /// <param name="consumerName">Optional consumer name for metadata.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The message ID in the dead letter queue.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
        /// <remarks>
        /// Dead letter queues are essential for handling messages that repeatedly fail processing.
        /// The original message is preserved with metadata about the failure for debugging.
        /// Remember to acknowledge the original message after moving to DLQ.
        /// </remarks>
        /// <example>
        /// <code>
        /// try
        /// {
        ///     await ProcessMessage(message);
        /// }
        /// catch (Exception ex)
        /// {
        ///     if (retryCount >= maxRetries)
        ///     {
        ///         await streamService.MoveToDeadLetterAsync&lt;Order&gt;(
        ///             "orders:stream",
        ///             "orders:dlq",
        ///             messageId,
        ///             reason: ex.Message,
        ///             retryCount: retryCount);
        ///     }
        /// }
        /// </code>
        /// </example>
        Task<string> MoveToDeadLetterAsync<T>(string sourceStream, string deadLetterStream, string messageId, string reason,
            int retryCount = 0, string? groupName = null, string? consumerName = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Retries pending messages with exponential backoff and configurable retry logic.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="stream">The stream name.</param>
        /// <param name="groupName">The consumer group name.</param>
        /// <param name="consumerName">The consumer name.</param>
        /// <param name="processor">Message processor function returning success/failure.</param>
        /// <param name="retryConfig">Optional retry configuration.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Result containing successful and failed message counts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
        /// <remarks>
        /// This method implements a robust retry mechanism with exponential backoff,
        /// automatic dead letter queue handling, and comprehensive error tracking.
        /// It's essential for building resilient message processing systems.
        /// </remarks>
        /// <example>
        /// <code>
        /// var retryConfig = new RetryConfiguration
        /// {
        ///     MaxAttempts = 3,
        ///     InitialDelay = TimeSpan.FromSeconds(1),
        ///     Strategy = BackoffStrategy.Exponential
        /// };
        /// 
        /// var result = await streamService.RetryPendingAsync&lt;Order&gt;(
        ///     "orders:stream",
        ///     "order-processors",
        ///     "retry-worker",
        ///     async order => await ProcessOrderWithRetry(order),
        ///     retryConfig);
        /// 
        /// Console.WriteLine($"Retried: {result.SuccessCount} succeeded, {result.FailureCount} failed");
        /// </code>
        /// </example>
        Task<RetryResult<T>> RetryPendingAsync<T>(string stream, string groupName, string consumerName,
            Func<T, Task<bool>> processor, RetryConfiguration? retryConfig = null, CancellationToken cancellationToken = default) where T : class;

        // ============= Important Features =============

        /// <summary>
        /// Adds multiple messages to a stream in a single batch operation.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="stream">The stream name.</param>
        /// <param name="messages">Array of messages to add.</param>
        /// <param name="maxLength">Optional maximum stream length for trimming.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Array of generated message IDs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream or messages is null.</exception>
        /// <remarks>
        /// Batch operations are significantly more efficient than individual adds.
        /// All messages are added atomically - either all succeed or all fail.
        /// Use this for high-throughput scenarios like bulk imports or event replay.
        /// </remarks>
        /// <example>
        /// <code>
        /// var events = GenerateBulkEvents();
        /// var messageIds = await streamService.AddBatchAsync(
        ///     "events:stream",
        ///     events,
        ///     maxLength: 100000);
        /// 
        /// Console.WriteLine($"Added {messageIds.Length} events in batch");
        /// </code>
        /// </example>
        Task<string[]> AddBatchAsync<T>(string stream, T[] messages, int? maxLength = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Gets comprehensive health information about a stream and its consumer groups.
        /// </summary>
        /// <param name="stream">The stream name.</param>
        /// <param name="includeGroups">Include consumer group health information.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Stream health information including lag, pending counts, and consumer status.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <remarks>
        /// Use this for monitoring dashboards, alerting systems, and health checks.
        /// High lag or pending counts may indicate processing issues.
        /// </remarks>
        Task<StreamHealthInfo> GetHealthAsync(string stream, bool includeGroups = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Collects detailed metrics about stream operations and performance.
        /// </summary>
        /// <param name="stream">The stream name.</param>
        /// <param name="groupName">Optional consumer group name for group-specific metrics.</param>
        /// <param name="window">Time window for rate calculations (default: 1 minute).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Comprehensive stream metrics including rates, latencies, and error counts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <remarks>
        /// Metrics are essential for capacity planning, performance tuning, and SLA monitoring.
        /// Track these metrics over time to identify trends and anomalies.
        /// </remarks>
        Task<StreamMetrics> GetMetricsAsync(string stream, string? groupName = null, TimeSpan? window = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads messages from a consumer group with automatic acknowledgment on successful processing.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="stream">The stream name.</param>
        /// <param name="groupName">The consumer group name.</param>
        /// <param name="consumerName">The consumer name.</param>
        /// <param name="processor">Async message processor returning true for success.</param>
        /// <param name="count">Number of messages to read and process.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The number of successfully processed and acknowledged messages.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
        /// <remarks>
        /// This method simplifies the read-process-acknowledge pattern.
        /// Messages are only acknowledged if the processor returns true.
        /// Failed messages remain pending for retry by this or other consumers.
        /// This ensures at-least-once delivery semantics.
        /// </remarks>
        /// <example>
        /// <code>
        /// var processed = await streamService.ReadGroupWithAutoAckAsync&lt;Order&gt;(
        ///     "orders:stream",
        ///     "order-processors",
        ///     "worker-1",
        ///     async order =>
        ///     {
        ///         try
        ///         {
        ///             await ProcessOrder(order);
        ///             return true; // Success - will auto-acknowledge
        ///         }
        ///         catch (Exception ex)
        ///         {
        ///             LogError(ex);
        ///             return false; // Failure - message remains pending
        ///         }
        ///     },
        ///     count: 10);
        /// 
        /// Console.WriteLine($"Successfully processed {processed} orders");
        /// </code>
        /// </example>
        Task<int> ReadGroupWithAutoAckAsync<T>(string stream, string groupName, string consumerName,
            Func<T, Task<bool>> processor, int count = 10, CancellationToken cancellationToken = default) where T : class;
    }
}