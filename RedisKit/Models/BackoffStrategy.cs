namespace RedisKit.Models;

/// <summary>
///     Backoff strategy for retry logic
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    ///     Fixed delay between retries
    /// </summary>
    Fixed,

    /// <summary>
    ///     Linear increase in delay
    /// </summary>
    Linear,

    /// <summary>
    ///     Exponential increase in delay
    /// </summary>
    Exponential,

    /// <summary>
    ///     Exponential with random jitter to prevent thundering herd
    /// </summary>
    ExponentialWithJitter,

    /// <summary>
    ///     Decorrelated jitter backoff (AWS recommended)
    /// </summary>
    DecorrelatedJitter
}