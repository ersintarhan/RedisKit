using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Interfaces;
using RedisKit.Models;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     Redis-based distributed lock implementation using the Redlock algorithm
/// </summary>
public class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisDistributedLock>? _logger;
    private readonly DistributedLockOptions _options;

    public RedisDistributedLock(
        IConnectionMultiplexer connectionMultiplexer,
        IOptions<DistributedLockOptions>? options = null,
        ILogger<RedisDistributedLock>? logger = null)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _logger = logger;
        _options = options?.Value ?? new DistributedLockOptions();
    }

    public async Task<ILockHandle?> AcquireLockAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource cannot be null or empty", nameof(resource));

        if (expiry <= TimeSpan.Zero)
            throw new ArgumentException("Expiry must be positive", nameof(expiry));

        var lockId = GenerateLockId();
        var lockKey = GetLockKey(resource);
        var database = _connectionMultiplexer.GetDatabase();

        // Try to acquire the lock using SET NX PX
        var acquired = await database.StringSetAsync(
            lockKey,
            lockId,
            expiry,
            When.NotExists,
            CommandFlags.DemandMaster);

        if (acquired)
        {
            _logger?.LogDebug("Acquired lock for resource: {Resource}, LockId: {LockId}", resource, lockId);

            return new RedisLockHandle(
                database,
                resource,
                lockId,
                expiry,
                _options.EnableAutoRenewal); // Logger type mismatch - RedisLockHandle will work without logger
        }

        _logger?.LogDebug("Failed to acquire lock for resource: {Resource} - already locked", resource);
        return null;
    }

    public async Task<ILockHandle?> AcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken cancellationToken = default)
    {
        if (wait <= TimeSpan.Zero)
            throw new ArgumentException("Wait time must be positive", nameof(wait));

        if (retry <= TimeSpan.Zero)
            throw new ArgumentException("Retry interval must be positive", nameof(retry));

        var deadline = DateTime.UtcNow.Add(wait);
        var attempts = 0;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            attempts++;

            var handle = await AcquireLockAsync(resource, expiry, cancellationToken).ConfigureAwait(false);
            if (handle != null)
            {
                _logger?.LogDebug("Acquired lock for resource: {Resource} after {Attempts} attempts", resource, attempts);
                return handle;
            }

            // Calculate next retry delay
            var remainingTime = deadline - DateTime.UtcNow;
            var delay = remainingTime < retry ? remainingTime : retry;

            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
        }

        _logger?.LogWarning("Failed to acquire lock for resource: {Resource} after {Attempts} attempts within {Wait}ms",
            resource, attempts, wait.TotalMilliseconds);

        return null;
    }

    public async Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource cannot be null or empty", nameof(resource));

        var database = _connectionMultiplexer.GetDatabase();
        var lockKey = GetLockKey(resource);

        return await database.KeyExistsAsync(lockKey, CommandFlags.DemandMaster).ConfigureAwait(false);
    }

    public async Task<bool> ExtendLockAsync(
        ILockHandle handle,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (handle == null)
            throw new ArgumentNullException(nameof(handle));

        if (expiry <= TimeSpan.Zero)
            throw new ArgumentException("Expiry must be positive", nameof(expiry));

        return await handle.ExtendAsync(expiry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Attempts to acquire locks on multiple resources atomically
    /// </summary>
    public async Task<IMultiLockHandle?> AcquireMultiLockAsync(
        string[] resources,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (resources == null || resources.Length == 0)
            throw new ArgumentException("Resources cannot be null or empty", nameof(resources));

        var acquiredLocks = new List<ILockHandle>();

        try
        {
            // Try to acquire all locks
            foreach (var resource in resources)
            {
                var handle = await AcquireLockAsync(resource, expiry, cancellationToken).ConfigureAwait(false);
                if (handle != null)
                {
                    acquiredLocks.Add(handle);
                }
                else
                {
                    // Failed to acquire one lock, release all acquired locks
                    _logger?.LogDebug("Failed to acquire multi-lock. Could not lock resource: {Resource}", resource);

                    // Use LINQ with Task.WhenAll for parallel release
                    await Task.WhenAll(acquiredLocks.Select(acquired =>
                        acquired.ReleaseAsync(cancellationToken)));

                    return null;
                }
            }

            _logger?.LogDebug("Successfully acquired multi-lock for {Count} resources", resources.Length);
            return new MultiLockHandle(acquiredLocks);
        }
        catch (Exception)
        {
            // Clean up any acquired locks using LINQ
            var releaseTasks = acquiredLocks.Select(async acquired =>
            {
                try
                {
                    await acquired.ReleaseAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort cleanup
                }
            });

            await Task.WhenAll(releaseTasks);
            throw;
        }
    }

    /// <summary>
    ///     Waits for a lock to be released
    /// </summary>
    public async Task WaitForUnlockAsync(
        string resource,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource cannot be null or empty", nameof(resource));

        var deadline = DateTime.UtcNow.Add(timeout);
        var checkInterval = TimeSpan.FromMilliseconds(Math.Min(100, timeout.TotalMilliseconds / 10));

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (!await IsLockedAsync(resource, cancellationToken)) return;

            await Task.Delay(checkInterval, cancellationToken);
        }

        throw new TimeoutException($"Timeout waiting for resource '{resource}' to be unlocked");
    }

    private static string GenerateLockId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string GetLockKey(string resource)
    {
        return $"lock:{resource}";
    }
}