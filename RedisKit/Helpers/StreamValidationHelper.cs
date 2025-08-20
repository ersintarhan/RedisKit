using RedisKit.Utilities;

namespace RedisKit.Helpers;

/// <summary>
///     Helper class for Redis Stream validation operations
///     Note: This class now delegates to ValidationUtils for consistency
/// </summary>
internal static class StreamValidationHelper
{
    /// <summary>
    ///     Validates a stream name
    /// </summary>
    public static void ValidateStreamName(string stream, string paramName = "stream")
    {
        ValidationUtils.ValidateStreamName(stream, paramName);
    }

    /// <summary>
    ///     Validates group name for consumer groups
    /// </summary>
    public static void ValidateGroupName(string groupName, string paramName = "groupName")
    {
        ValidationUtils.ValidateGroupName(groupName, paramName);
    }

    /// <summary>
    ///     Validates consumer name
    /// </summary>
    public static void ValidateConsumerName(string consumerName, string paramName = "consumerName")
    {
        ValidationUtils.ValidateConsumerName(consumerName, paramName);
    }

    /// <summary>
    ///     Validates batch parameters
    /// </summary>
    public static void ValidateBatchParameters<T>(string stream, T[] messages) where T : class
    {
        ValidationUtils.ValidateStreamName(stream);
        ValidationUtils.ValidateMessageBatch(messages);
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
        ValidationUtils.ValidateStreamName(stream);
        ValidationUtils.ValidateGroupName(groupName);
        ValidationUtils.ValidateConsumerName(consumerName);
        ValidationUtils.ValidateAsyncFunction(processor, nameof(processor));
    }

    /// <summary>
    ///     Validates message IDs array
    /// </summary>
    public static void ValidateMessageIds(string[] messageIds, string paramName = "messageIds")
    {
        ValidationUtils.ValidateMessageIds(messageIds, paramName);
    }

    /// <summary>
    ///     Validates max length parameter
    /// </summary>
    public static void ValidateMaxLength(int maxLength, string paramName = "maxLength")
    {
        ValidationUtils.ValidatePositiveCount(maxLength, paramName, "Max length must be greater than zero.");
    }

    /// <summary>
    ///     Validates count parameter
    /// </summary>
    public static void ValidateCount(int count, string paramName = "count")
    {
        ValidationUtils.ValidatePositiveCount(count, paramName);
    }

    /// <summary>
    ///     Validates min idle time
    /// </summary>
    public static void ValidateMinIdleTime(long minIdleTime, string paramName = "minIdleTime")
    {
        ValidationUtils.ValidateNonNegativeTime(minIdleTime, paramName, "Min idle time cannot be negative.");
    }
}