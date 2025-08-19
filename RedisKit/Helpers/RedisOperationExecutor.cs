using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using RedisKit.Exceptions;
using StackExchange.Redis;

namespace RedisKit.Helpers;

/// <summary>
///     Provides centralized exception handling and retry logic for Redis operations
/// </summary>
internal static class RedisOperationExecutor
{
    /// <summary>
    ///     Executes a Redis operation with standardized exception handling
    /// </summary>
    public static async Task<T?> ExecuteAsync<T>(
        Func<Task<T?>> operation,
        ILogger? logger,
        string? key = null,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string operationName = "",
        Func<Exception, T?>? handleSpecificExceptions = null) where T : class
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            logger?.LogError(ex, "Redis connection error during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitConnectionException($"Failed to connect to Redis during {operationName}", ex);
        }
        catch (RedisTimeoutException ex)
        {
            logger?.LogError(ex, "Redis timeout during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitTimeoutException($"Redis operation timed out during {operationName}", ex);
        }
        catch (RedisServerException ex)
        {
            // Allow custom handling of specific server exceptions
            if (handleSpecificExceptions != null)
            {
                var result = handleSpecificExceptions(ex);
                if (result != null || handleSpecificExceptions(ex) is not null)
                    return result;
            }
            
            logger?.LogError(ex, "Redis server error during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitServerException($"Redis server error during {operationName}", ex);
        }
        catch (Exception ex) when (ex is not RedisKitException)
        {
            // Allow custom handling of specific exceptions
            if (handleSpecificExceptions != null)
            {
                var result = handleSpecificExceptions(ex);
                if (result != null || handleSpecificExceptions(ex) is not null)
                    return result;
            }
            
            logger?.LogError(ex, "Unexpected error during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitException($"Unexpected error during {operationName}", ex);
        }
    }

    /// <summary>
    ///     Executes a Redis operation with standardized exception handling (ValueTask version)
    /// </summary>
    public static async ValueTask ExecuteAsync(
        Func<ValueTask> operation,
        ILogger? logger,
        string? key = null,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string operationName = "")
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            logger?.LogError(ex, "Redis connection error during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitConnectionException($"Failed to connect to Redis during {operationName}", ex);
        }
        catch (RedisTimeoutException ex)
        {
            logger?.LogError(ex, "Redis timeout during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitTimeoutException($"Redis operation timed out during {operationName}", ex);
        }
        catch (RedisServerException ex)
        {
            logger?.LogError(ex, "Redis server error during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitServerException($"Redis server error during {operationName}", ex);
        }
        catch (Exception ex) when (ex is not RedisKitException)
        {
            logger?.LogError(ex, "Unexpected error during {Operation} for key: {Key}", operationName, key);
            throw new RedisKitException($"Unexpected error during {operationName}", ex);
        }
    }

    /// <summary>
    ///     Executes a Redis operation with retry logic
    /// </summary>
    public static async Task<T?> ExecuteWithRetryAsync<T>(
        Func<Task<T?>> operation,
        ILogger? logger,
        int maxRetries = 1,
        string? key = null,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string operationName = "") where T : class
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            try
            {
                if (attempt > 0)
                {
                    var delay = TimeSpan.FromMilliseconds(Math.Min(100 * Math.Pow(2, attempt), 1000));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    logger?.LogDebug("Retrying {Operation} for key {Key}, attempt {Attempt}/{MaxRetries}",
                        operationName, key, attempt, maxRetries);
                }

                return await operation().ConfigureAwait(false);
            }
            catch (RedisConnectionException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                logger?.LogWarning(ex, "Redis connection error on {Operation} for key {Key}, will retry", operationName, key);
            }
            catch (RedisTimeoutException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                logger?.LogWarning(ex, "Redis timeout on {Operation} for key {Key}, will retry", operationName, key);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("LOADING") && attempt < maxRetries)
            {
                // Redis is loading data, retry makes sense
                lastException = ex;
                logger?.LogWarning(ex, "Redis is loading data, will retry {Operation} for key {Key}", operationName, key);
            }
            catch (Exception ex)
            {
                // Don't retry on other exceptions
                logger?.LogError(ex, "Redis error on {Operation} for key {Key}", operationName, key);
                throw new RedisKitException($"Redis operation failed: {operationName}", ex);
            }
        }

        logger?.LogError(lastException, "Redis operation {Operation} failed after {MaxRetries} retries for key {Key}",
            operationName, maxRetries, key);
        throw new RedisKitException($"Redis operation failed after {maxRetries} retries: {operationName}", lastException!);
    }
}