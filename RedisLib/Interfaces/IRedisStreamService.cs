using RedisLib.Models;
using StackExchange.Redis;

namespace RedisLib.Interfaces
{
    /// <summary>
    /// Interface for Redis Streams operations with generic support
    /// </summary>
    public interface IRedisStreamService
    {
        /// <summary>
        /// Adds a message to a stream
        /// </summary>
        Task<string> AddAsync<T>(string stream, T message, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Adds a message to a stream with optional max length for automatic trimming
        /// </summary>
        Task<string> AddAsync<T>(string stream, T message, int? maxLength, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Reads messages from a stream
        /// </summary>
        Task<Dictionary<string, T?>> ReadAsync<T>(string stream, string? start = null, string? end = null, int count = 10, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Creates a consumer group for a stream
        /// </summary>
        Task CreateConsumerGroupAsync(string stream, string groupName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads messages from a stream using consumer group
        /// </summary>
        Task<Dictionary<string, T?>> ReadGroupAsync<T>(string stream, string groupName, string consumerName, int count = 10, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Acknowledges a message as processed
        /// </summary>
        Task AcknowledgeAsync(string stream, string groupName, string messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes messages from a stream
        /// </summary>
        /// <param name="stream">The stream name</param>
        /// <param name="messageIds">Array of message IDs to delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of messages deleted</returns>
        Task<long> DeleteAsync(string stream, string[] messageIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Trims the stream to a specified maximum length
        /// </summary>
        /// <param name="stream">The stream name</param>
        /// <param name="maxLength">Maximum number of entries to keep</param>
        /// <param name="useApproximateMaxLength">Use approximate trimming for better performance</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of messages trimmed</returns>
        Task<long> TrimByLengthAsync(string stream, int maxLength, bool useApproximateMaxLength = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Claims ownership of pending messages
        /// </summary>
        /// <param name="stream">The stream name</param>
        /// <param name="groupName">Consumer group name</param>
        /// <param name="consumerName">Consumer name claiming the messages</param>
        /// <param name="minIdleTime">Minimum idle time in milliseconds</param>
        /// <param name="messageIds">Message IDs to claim</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary of claimed messages</returns>
        Task<Dictionary<string, T?>> ClaimAsync<T>(string stream, string groupName, string consumerName, long minIdleTime, string[] messageIds, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Gets information about a stream
        /// </summary>
        /// <param name="stream">The stream name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream information including length, first and last entry IDs, consumer groups</returns>
        Task<StreamInfo> GetInfoAsync(string stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets pending messages for a consumer group
        /// </summary>
        /// <param name="stream">The stream name</param>
        /// <param name="groupName">Consumer group name</param>
        /// <param name="count">Maximum number of pending messages to return</param>
        /// <param name="consumerName">Optional consumer name to filter by</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of pending message information</returns>
        Task<StreamPendingMessageInfo[]> GetPendingAsync(string stream, string groupName, int count = 10, string? consumerName = null, CancellationToken cancellationToken = default);

        // ============= Critical Features =============

        /// <summary>
        /// Moves a failed message to dead letter queue
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <param name="sourceStream">Source stream name</param>
        /// <param name="deadLetterStream">Dead letter queue stream name</param>
        /// <param name="messageId">Message ID to move</param>
        /// <param name="reason">Reason for moving to DLQ</param>
        /// <param name="retryCount">Number of retries attempted</param>
        /// <param name="groupName">Consumer group name</param>
        /// <param name="consumerName">Consumer name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dead letter queue message ID</returns>
        Task<string> MoveToDeadLetterAsync<T>(string sourceStream, string deadLetterStream, string messageId, string reason, 
            int retryCount = 0, string? groupName = null, string? consumerName = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Retries pending messages that have timed out
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <param name="stream">Stream name</param>
        /// <param name="groupName">Consumer group name</param>
        /// <param name="consumerName">Consumer name</param>
        /// <param name="processor">Message processor function</param>
        /// <param name="retryConfig">Retry configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Retry operation result</returns>
        Task<RetryResult<T>> RetryPendingAsync<T>(string stream, string groupName, string consumerName, 
            Func<T, Task<bool>> processor, RetryConfiguration? retryConfig = null, CancellationToken cancellationToken = default) where T : class;

        // ============= Important Features =============

        /// <summary>
        /// Adds multiple messages to a stream in batch
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <param name="stream">Stream name</param>
        /// <param name="messages">Array of messages to add</param>
        /// <param name="maxLength">Optional max length for stream trimming</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Array of message IDs</returns>
        Task<string[]> AddBatchAsync<T>(string stream, T[] messages, int? maxLength = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Gets health information about a stream
        /// </summary>
        /// <param name="stream">Stream name</param>
        /// <param name="includeGroups">Include consumer group information</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream health information</returns>
        Task<StreamHealthInfo> GetHealthAsync(string stream, bool includeGroups = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Collects metrics about stream operations
        /// </summary>
        /// <param name="stream">Stream name</param>
        /// <param name="groupName">Optional consumer group name for group-specific metrics</param>
        /// <param name="window">Time window for rate calculations</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream metrics</returns>
        Task<StreamMetrics> GetMetricsAsync(string stream, string? groupName = null, TimeSpan? window = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads from consumer group with automatic acknowledgment on successful processing
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <param name="stream">Stream name</param>
        /// <param name="groupName">Consumer group name</param>
        /// <param name="consumerName">Consumer name</param>
        /// <param name="processor">Message processor function that returns success/failure</param>
        /// <param name="count">Number of messages to read</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of successfully processed messages</returns>
        Task<int> ReadGroupWithAutoAckAsync<T>(string stream, string groupName, string consumerName, 
            Func<T, Task<bool>> processor, int count = 10, CancellationToken cancellationToken = default) where T : class;
    }
}