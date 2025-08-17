namespace RedisKit.Models;

/// <summary>
/// Retry configuration with advanced backoff strategies
/// </summary>
public class RetryConfiguration
{
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between retries
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Backoff strategy to use
    /// </summary>
    public BackoffStrategy Strategy { get; set; } = BackoffStrategy.ExponentialWithJitter;

    /// <summary>
    /// Multiplier for exponential backoff
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum jitter percentage (0.0 to 1.0)
    /// </summary>
    public double JitterFactor { get; set; } = 0.2;

    // Stream-specific retry settings (compatibility)

    /// <summary>
    /// Alias for MaxAttempts (for backward compatibility)
    /// </summary>
    public int MaxRetries
    {
        get => MaxAttempts;
        set => MaxAttempts = value;
    }

    /// <summary>
    /// Alias for InitialDelay (for backward compatibility)
    /// </summary>
    public TimeSpan RetryDelay
    {
        get => InitialDelay;
        set => InitialDelay = value;
    }

    /// <summary>
    /// Idle timeout for pending messages (Stream-specific)
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Use exponential backoff for retries (Stream-specific)
    /// </summary>
    public bool UseExponentialBackoff
    {
        get => Strategy == BackoffStrategy.Exponential || Strategy == BackoffStrategy.ExponentialWithJitter;
        set => Strategy = value ? BackoffStrategy.ExponentialWithJitter : BackoffStrategy.Fixed;
    }

    /// <summary>
    /// Move to dead letter queue after max retries (Stream-specific)
    /// </summary>
    public bool MoveToDeadLetterQueue { get; set; } = true;

    /// <summary>
    /// Dead letter queue suffix (Stream-specific)
    /// </summary>
    public string DeadLetterSuffix { get; set; } = ":dlq";
}