using RedisKit.Models;

namespace RedisKit.Utilities;

/// <summary>
///     Calculates retry delays based on different backoff strategies
/// </summary>
internal static class BackoffCalculator
{
    private static readonly Random _random = new();
    private static readonly object _randomLock = new();

    /// <summary>
    ///     Calculates the delay for the next retry attempt
    /// </summary>
    /// <param name="attempt">Current attempt number (0-based)</param>
    /// <param name="config">Retry configuration</param>
    /// <param name="previousDelay">Previous delay (for decorrelated jitter)</param>
    /// <returns>Delay duration for the next retry</returns>
    public static TimeSpan CalculateDelay(int attempt, RetryConfiguration config, TimeSpan? previousDelay = null)
    {
        if (attempt < 0)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be non-negative");

        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var baseDelay = config.Strategy switch
        {
            BackoffStrategy.Fixed => CalculateFixed(config),
            BackoffStrategy.Linear => CalculateLinear(attempt, config),
            BackoffStrategy.Exponential => CalculateExponential(attempt, config),
            BackoffStrategy.ExponentialWithJitter => CalculateExponentialWithJitter(attempt, config),
            BackoffStrategy.DecorrelatedJitter => CalculateDecorrelatedJitter(config, previousDelay),
            _ => config.InitialDelay
        };

        // Add jitter to all strategies (except ExponentialWithJitter and DecorrelatedJitter which already have it)
        if (config.EnableJitter &&
            config.Strategy != BackoffStrategy.ExponentialWithJitter &&
            config.Strategy != BackoffStrategy.DecorrelatedJitter)
        {
            var jitterRange = baseDelay.TotalMilliseconds * config.JitterFactor;
            double jitter;
            lock (_randomLock)
            {
                jitter = (_random.NextDouble() * 2 - 1) * jitterRange / 2; // ±jitterFactor/2
            }

            baseDelay = TimeSpan.FromMilliseconds(Math.Max(0, baseDelay.TotalMilliseconds + jitter));
        }

        // Ensure delay doesn't exceed max delay
        if (baseDelay > config.MaxDelay)
            baseDelay = config.MaxDelay;

        return baseDelay;
    }

    private static TimeSpan CalculateFixed(RetryConfiguration config)
    {
        return config.InitialDelay;
    }

    private static TimeSpan CalculateLinear(int attempt, RetryConfiguration config)
    {
        var delayMs = config.InitialDelay.TotalMilliseconds * (attempt + 1);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private static TimeSpan CalculateExponential(int attempt, RetryConfiguration config)
    {
        var delayMs = config.InitialDelay.TotalMilliseconds * Math.Pow(config.BackoffMultiplier, attempt);
        
        // Prevent overflow by capping to reasonable maximum
        if (double.IsInfinity(delayMs) || double.IsNaN(delayMs) || delayMs > TimeSpan.MaxValue.TotalMilliseconds)
        {
            return config.MaxDelay;
        }
        
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private static TimeSpan CalculateExponentialWithJitter(int attempt, RetryConfiguration config)
    {
        // Calculate base exponential delay
        var baseDelay = CalculateExponential(attempt, config);

        // Add jitter to prevent thundering herd problem
        var jitter = GetRandomJitter(baseDelay, config.JitterFactor);

        return baseDelay + jitter;
    }

    private static TimeSpan CalculateDecorrelatedJitter(RetryConfiguration config, TimeSpan? previousDelay)
    {
        // AWS recommended decorrelated jitter
        // sleep = min(cap, random_between(base, sleep * 3))

        var baseMs = config.InitialDelay.TotalMilliseconds;
        var prevMs = previousDelay?.TotalMilliseconds ?? baseMs;

        var minDelay = baseMs;
        var maxDelay = prevMs * 3;

        // Ensure max doesn't exceed configured maximum
        if (maxDelay > config.MaxDelay.TotalMilliseconds)
            maxDelay = config.MaxDelay.TotalMilliseconds;

        double delay;
        lock (_randomLock)
        {
            delay = minDelay + _random.NextDouble() * (maxDelay - minDelay);
        }

        return TimeSpan.FromMilliseconds(delay);
    }

    private static TimeSpan GetRandomJitter(TimeSpan baseDelay, double jitterFactor)
    {
        if (jitterFactor <= 0 || jitterFactor > 1)
            return TimeSpan.Zero;

        double jitterMs;
        lock (_randomLock)
        {
            // Generate random jitter between -jitterFactor and +jitterFactor
            var jitterRange = baseDelay.TotalMilliseconds * jitterFactor;
            jitterMs = (_random.NextDouble() * 2 - 1) * jitterRange;
        }

        return TimeSpan.FromMilliseconds(Math.Abs(jitterMs));
    }

    /// <summary>
    ///     Calculates total maximum possible delay for all retry attempts
    /// </summary>
    public static TimeSpan CalculateTotalMaxDelay(RetryConfiguration config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var totalDelay = TimeSpan.Zero;
        TimeSpan? previousDelay = null;

        for (var i = 0; i < config.MaxAttempts; i++)
        {
            var delay = CalculateDelay(i, config, previousDelay);
            totalDelay += delay;
            previousDelay = delay;
        }

        return totalDelay;
    }
}