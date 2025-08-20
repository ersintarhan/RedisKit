namespace RedisKit.Utilities;

/// <summary>
///     Centralized validation utilities for consistent input validation across RedisKit services
/// </summary>
internal static class ValidationUtils
{
    /// <summary>
    ///     Validates a Redis key for length and basic constraints
    /// </summary>
    /// <param name="key">The Redis key to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    /// <exception cref="ArgumentException">Thrown when key is null, empty, or exceeds maximum length</exception>
    public static void ValidateRedisKey(string key, string paramName = "key")
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Redis key cannot be null, empty, or whitespace.", paramName);

        // Redis keys can't be longer than 512 MB (but practically much less)
        if (key.Length > RedisConstants.MaxRedisKeyLength)
            throw new ArgumentException($"Redis key exceeds maximum allowed length of {RedisConstants.MaxRedisKeyLength}", paramName);
    }

    /// <summary>
    ///     Validates a Redis key prefix (allows empty string for "no prefix")
    /// </summary>
    /// <param name="prefix">The key prefix to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    /// <exception cref="ArgumentNullException">Thrown when prefix is null</exception>
    /// <exception cref="ArgumentException">Thrown when prefix exceeds maximum length</exception>
    public static void ValidateKeyPrefix(string? prefix, string paramName = "prefix")
    {
        if (prefix is null)
            throw new ArgumentNullException(paramName, "Key prefix cannot be null.");

        // Empty string is valid for prefixes (means no prefix)
        if (prefix.Length > RedisConstants.MaxRedisKeyLength)
            throw new ArgumentException($"Key prefix exceeds maximum allowed length of {RedisConstants.MaxRedisKeyLength}", paramName);
    }

    /// <summary>
    ///     Validates a Redis stream name
    /// </summary>
    /// <param name="streamName">The stream name to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateStreamName(string streamName, string paramName = "stream")
    {
        if (string.IsNullOrWhiteSpace(streamName))
            throw new ArgumentException("Stream name cannot be null, empty, or whitespace.", paramName);

        // Apply Redis key length limits to stream names
        if (streamName.Length > RedisConstants.MaxRedisKeyLength)
            throw new ArgumentException($"Stream name exceeds maximum allowed length of {RedisConstants.MaxRedisKeyLength}", paramName);
    }

    /// <summary>
    ///     Validates a Redis pub/sub channel name
    /// </summary>
    /// <param name="channel">The channel name to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateChannelName(string channel, string paramName = "channel")
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel name cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates a consumer group name
    /// </summary>
    /// <param name="groupName">The group name to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateGroupName(string groupName, string paramName = "groupName")
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("Group name cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates a consumer name
    /// </summary>
    /// <param name="consumerName">The consumer name to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateConsumerName(string consumerName, string paramName = "consumerName")
    {
        if (string.IsNullOrWhiteSpace(consumerName))
            throw new ArgumentException("Consumer name cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates a message ID
    /// </summary>
    /// <param name="messageId">The message ID to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateMessageId(string messageId, string paramName = "messageId")
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates an array of message IDs
    /// </summary>
    /// <param name="messageIds">The array of message IDs to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateMessageIds(string[]? messageIds, string paramName = "messageIds")
    {
        if (messageIds is null)
            throw new ArgumentNullException(paramName, "Message IDs array cannot be null.");

        if (messageIds.Length == 0)
            throw new ArgumentException("Message IDs array cannot be empty.", paramName);

        for (var i = 0; i < messageIds.Length; i++)
            if (string.IsNullOrWhiteSpace(messageIds[i]))
                throw new ArgumentException($"Message ID at index {i} cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates a positive integer count parameter
    /// </summary>
    /// <param name="count">The count to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    /// <param name="message">Optional custom error message</param>
    public static void ValidatePositiveCount(int count, string paramName = "count", string? message = null)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(paramName, count, message ?? "Count must be greater than zero.");
    }

    /// <summary>
    ///     Validates a non-negative time value in milliseconds
    /// </summary>
    /// <param name="timeMs">The time in milliseconds to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    /// <param name="message">Optional custom error message</param>
    public static void ValidateNonNegativeTime(long timeMs, string paramName, string? message = null)
    {
        if (timeMs < 0)
            throw new ArgumentOutOfRangeException(paramName, timeMs, message ?? "Time value cannot be negative.");
    }

    /// <summary>
    ///     Validates a timeout value
    /// </summary>
    /// <param name="timeout">The timeout to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateTimeout(TimeSpan timeout, string paramName = "timeout")
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, timeout, "Timeout cannot be negative.");

        if (timeout > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(paramName, timeout, "Timeout cannot exceed 24 hours.");
    }

    /// <summary>
    ///     Validates a batch of messages for stream operations
    /// </summary>
    /// <typeparam name="T">The message type</typeparam>
    /// <param name="messages">The messages array to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateMessageBatch<T>(T[]? messages, string paramName = "messages") where T : class
    {
        if (messages is null)
            throw new ArgumentNullException(paramName, "Messages array cannot be null.");

        if (messages.Length == 0)
            throw new ArgumentException("Messages array cannot be empty.", paramName);

        for (var i = 0; i < messages.Length; i++)
            if (messages[i] is null)
                throw new ArgumentException($"Message at index {i} cannot be null.", $"{paramName}[{i}]");
    }

    /// <summary>
    ///     Validates a function/processor parameter
    /// </summary>
    /// <typeparam name="TInput">The input type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    /// <param name="function">The function to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateFunction<TInput, TOutput>(Func<TInput, TOutput>? function, string paramName = "function")
    {
        if (function is null)
            throw new ArgumentNullException(paramName, "Function cannot be null.");
    }

    /// <summary>
    ///     Validates an async function/processor parameter
    /// </summary>
    /// <typeparam name="TInput">The input type</typeparam>
    /// <typeparam name="TOutput">The output type</typeparam>
    /// <param name="function">The async function to validate</param>
    /// <param name="paramName">The parameter name for exception messages</param>
    public static void ValidateAsyncFunction<TInput, TOutput>(Func<TInput, Task<TOutput>>? function, string paramName = "function")
    {
        if (function is null)
            throw new ArgumentNullException(paramName, "Function cannot be null.");
    }
}