using Microsoft.Extensions.Logging;
using RedisKit.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedisKit.Helpers;

/// <summary>
/// Helper class for Redis Stream retry operations
/// </summary>
internal static class StreamRetryHelper
{
    /// <summary>
    /// Executes an operation with retry logic
    /// </summary>
    public static async Task<TResult?> ExecuteWithRetryAsync<TResult>(
        Func<Task<TResult>> operation,
        RetryConfiguration retryConfig,
        ILogger? logger = null,
        string? operationName = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        var maxRetries = retryConfig.MaxRetries;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var result = await operation();
                
                if (attempt > 0 && logger != null)
                {
                    LogRetrySuccess(logger, operationName, attempt);
                }
                
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                
                if (attempt < maxRetries)
                {
                    var delay = CalculateDelay(attempt, retryConfig);
                    
                    if (logger != null)
                    {
                        LogRetryAttempt(logger, operationName, attempt + 1, maxRetries, delay, ex);
                    }
                    
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        if (logger != null && lastException != null)
        {
            LogRetryFailure(logger, operationName, maxRetries, lastException);
        }

        return default;
    }

    /// <summary>
    /// Executes an operation with retry logic (non-generic version)
    /// </summary>
    public static async Task<bool> ExecuteWithRetryAsync(
        Func<Task<bool>> operation,
        RetryConfiguration retryConfig,
        ILogger? logger = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        var maxRetries = retryConfig.MaxRetries;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var result = await operation();
                
                if (result)
                {
                    if (attempt > 0 && logger != null)
                    {
                        LogRetrySuccess(logger, operationName, attempt);
                    }
                    return true;
                }
                
                // If result is false and we have more retries, continue
                if (attempt < maxRetries)
                {
                    var delay = CalculateDelay(attempt, retryConfig);
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                
                if (attempt < maxRetries)
                {
                    var delay = CalculateDelay(attempt, retryConfig);
                    
                    if (logger != null)
                    {
                        LogRetryAttempt(logger, operationName, attempt + 1, maxRetries, delay, ex);
                    }
                    
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        if (logger != null && lastException != null)
        {
            LogRetryFailure(logger, operationName, maxRetries, lastException);
        }

        return false;
    }

    /// <summary>
    /// Calculates delay for retry attempt
    /// </summary>
    public static TimeSpan CalculateDelay(int attemptNumber, RetryConfiguration retryConfig)
    {
        var baseDelay = retryConfig.InitialDelay.TotalMilliseconds;
        double delayMs;
        
        switch (retryConfig.Strategy)
        {
            case BackoffStrategy.Fixed:
                delayMs = baseDelay;
                break;
                
            case BackoffStrategy.Linear:
                delayMs = baseDelay * (attemptNumber + 1);
                break;
                
            case BackoffStrategy.Exponential:
                delayMs = baseDelay * Math.Pow(retryConfig.BackoffMultiplier, attemptNumber);
                break;
                
            case BackoffStrategy.ExponentialWithJitter:
                var exponentialDelay = baseDelay * Math.Pow(retryConfig.BackoffMultiplier, attemptNumber);
                var jitter = exponentialDelay * retryConfig.JitterFactor * Random.Shared.NextDouble();
                delayMs = exponentialDelay + jitter;
                break;
                
            case BackoffStrategy.DecorrelatedJitter:
                // AWS recommended decorrelated jitter
                var minDelay = baseDelay;
                var maxDelay = Math.Min(retryConfig.MaxDelay.TotalMilliseconds, baseDelay * Math.Pow(3, attemptNumber));
                delayMs = Random.Shared.NextDouble() * (maxDelay - minDelay) + minDelay;
                break;
                
            default:
                delayMs = baseDelay;
                break;
        }
        
        // Cap at max delay
        delayMs = Math.Min(delayMs, retryConfig.MaxDelay.TotalMilliseconds);
        
        return TimeSpan.FromMilliseconds(delayMs);
    }

    /// <summary>
    /// Determines if an exception is retryable
    /// </summary>
    public static bool IsRetryableException(Exception ex)
    {
        return ex switch
        {
            TimeoutException => true,
            OperationCanceledException => false,
            InvalidOperationException => false,
            ArgumentException => false,
            _ => true
        };
    }

    // CreateRetryResult method removed - RetryResult<T> structure is different for stream processing

    private static void LogRetryAttempt(
        ILogger logger,
        string? operationName,
        int attempt,
        int maxRetries,
        TimeSpan delay,
        Exception ex)
    {
        logger.LogWarning(
            ex,
            "Retry attempt {Attempt}/{MaxRetries} for operation '{Operation}'. Waiting {Delay}ms before next attempt.",
            attempt,
            maxRetries,
            operationName ?? "Unknown",
            delay.TotalMilliseconds);
    }

    private static void LogRetrySuccess(
        ILogger logger,
        string? operationName,
        int attemptNumber)
    {
        logger.LogInformation(
            "Operation '{Operation}' succeeded after {Attempts} attempt(s).",
            operationName ?? "Unknown",
            attemptNumber + 1);
    }

    private static void LogRetryFailure(
        ILogger logger,
        string? operationName,
        int maxRetries,
        Exception lastException)
    {
        logger.LogError(
            lastException,
            "Operation '{Operation}' failed after {MaxRetries} retry attempts.",
            operationName ?? "Unknown",
            maxRetries);
    }

    /// <summary>
    /// Wraps an async function to add retry capability
    /// </summary>
    public static Func<Task<T?>> WrapWithRetry<T>(
        Func<Task<T?>> operation,
        RetryConfiguration retryConfig,
        ILogger? logger = null) where T : class
    {
        return async () =>
        {
            // Wrap the nullable operation to match ExecuteWithRetryAsync's signature
            async Task<T> wrappedOperation()
            {
                var result = await operation();
                return result!; // We handle null by returning from ExecuteWithRetryAsync
            }
            
            var result = await ExecuteWithRetryAsync<T>(
                wrappedOperation,
                retryConfig,
                logger,
                operation.Method?.Name);
            return result;
        };
    }
}