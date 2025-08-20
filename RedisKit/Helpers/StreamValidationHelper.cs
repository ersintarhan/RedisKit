namespace RedisKit.Helpers;

/// <summary>
///     Helper class for Redis Stream validation operations
/// </summary>
internal static class StreamValidationHelper
{
    /// <summary>
    ///     Validates a stream name
    /// </summary>
    public static void ValidateStreamName(string stream, string paramName = "stream")
    {
        if (string.IsNullOrWhiteSpace(stream))
            throw new ArgumentException("Stream name cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates group name for consumer groups
    /// </summary>
    public static void ValidateGroupName(string groupName, string paramName = "groupName")
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("Group name cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates consumer name
    /// </summary>
    public static void ValidateConsumerName(string consumerName, string paramName = "consumerName")
    {
        if (string.IsNullOrWhiteSpace(consumerName))
            throw new ArgumentException("Consumer name cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates batch parameters
    /// </summary>
    public static void ValidateBatchParameters<T>(string stream, T[] messages)
    {
        ValidateStreamName(stream);

        if (messages == null)
            throw new ArgumentNullException(nameof(messages), "Messages cannot be null.");

        if (messages.Length == 0)
            throw new ArgumentException("Messages array cannot be empty.", nameof(messages));
    }

    /// <summary>
    ///     Validates retry parameters
    /// </summary>
    public static void ValidateRetryParameters<T>(
        string stream,
        string groupName,
        string consumerName,
        Func<T, Task<bool>>? processor)
    {
        ValidateStreamName(stream);
        ValidateGroupName(groupName);
        ValidateConsumerName(consumerName);

        if (processor == null)
            throw new ArgumentNullException(nameof(processor), "Processor function cannot be null.");
    }

    /// <summary>
    ///     Validates message IDs array
    /// </summary>
    public static void ValidateMessageIds(string[] messageIds, string paramName = "messageIds")
    {
        if (messageIds == null)
            throw new ArgumentNullException(paramName, "Message IDs cannot be null.");

        if (messageIds.Length == 0)
            throw new ArgumentException("Message IDs array cannot be empty.", paramName);

        foreach (var id in messageIds)
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Message ID cannot be null, empty, or whitespace.", paramName);
    }

    /// <summary>
    ///     Validates max length parameter
    /// </summary>
    public static void ValidateMaxLength(int maxLength, string paramName = "maxLength")
    {
        if (maxLength <= 0)
            throw new ArgumentOutOfRangeException(paramName, maxLength, "Max length must be greater than zero.");
    }

    /// <summary>
    ///     Validates count parameter
    /// </summary>
    public static void ValidateCount(int count, string paramName = "count")
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(paramName, count, "Count must be greater than zero.");
    }

    /// <summary>
    ///     Validates min idle time
    /// </summary>
    public static void ValidateMinIdleTime(long minIdleTime, string paramName = "minIdleTime")
    {
        if (minIdleTime < 0)
            throw new ArgumentOutOfRangeException(paramName, minIdleTime, "Min idle time cannot be negative.");
    }
}