using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Helpers;
using RedisKit.Interfaces;
using RedisKit.Models;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     An implementation of a distributed lock using a single Redis instance.
///     WARNING: This implementation is not the Redlock algorithm and does not provide the same safety guarantees
///     against scenarios like Redis master failover. It is intended for use cases where such guarantees
///     are not strictly required.
/// </summary>
public class RedisDistributedLock : IDistributedLock
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisDistributedLock>? _logger;
    private readonly DistributedLockOptions _options;

    public RedisDistributedLock(
        IRedisConnection connection,
        IOptions<DistributedLockOptions>? options = null,
        ILogger<RedisDistributedLock>? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
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

        // Add bounds validation for lock expiry
        if (expiry < TimeSpan.FromSeconds(1))
            throw new ArgumentException("Lock expiry must be at least 1 second", nameof(expiry));

        if (expiry > TimeSpan.FromHours(1))
            throw new ArgumentException("Lock expiry cannot exceed 1 hour", nameof(expiry));

        // Validate resource length to prevent Redis key size issues
        if (resource.Length > 512)
            throw new ArgumentException("Resource name exceeds Redis key limit of 512 characters", nameof(resource));

        var lockId = GenerateLockId();
        var lockKey = GetLockKey(resource);

        var acquired = await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                var database = await _connection.GetDatabaseAsync();

                // Try to acquire the lock using SET NX PX
                var lockResult = await database.StringSetAsync(
                    lockKey,
                    lockId,
                    expiry,
                    When.NotExists,
                    CommandFlags.DemandMaster);

                return (object?)lockResult;
            },
            _logger,
            lockKey,
            cancellationToken
        ).ConfigureAwait(false);

        var lockAcquired = acquired is bool result && result;

        if (lockAcquired)
        {
            _logger?.LogDebug("Acquired lock for resource: {Resource}, LockId: {LockId}", resource, lockId);

            // RedisLockHandle requires the synchronous IDatabase interface.
            var syncDatabase = (await _connection.GetMultiplexerAsync()).GetDatabase();

            return new RedisLockHandle(
                syncDatabase,
                resource,
                lockId,
                expiry,
                _options.EnableAutoRenewal,
                _logger); // Pass logger to RedisLockHandle for better logging
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

        // Validate resource length to prevent Redis key size issues
        if (resource.Length > 512)
            throw new ArgumentException("Resource name exceeds Redis key limit of 512 characters", nameof(resource));

        var lockKey = GetLockKey(resource);

        var exists = await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                var database = await _connection.GetDatabaseAsync();
                var keyExists = await database.KeyExistsAsync(lockKey, CommandFlags.DemandMaster).ConfigureAwait(false);
                return (object?)keyExists;
            },
            _logger,
            lockKey,
            cancellationToken
        ).ConfigureAwait(false);

        return exists is bool result && result;
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

        // Add bounds validation for lock expiry
        if (expiry < TimeSpan.FromSeconds(1))
            throw new ArgumentException("Lock expiry must be at least 1 second", nameof(expiry));

        if (expiry > TimeSpan.FromHours(1))
            throw new ArgumentException("Lock expiry cannot exceed 1 hour", nameof(expiry));

        return await handle.ExtendAsync(expiry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Attempts to acquire locks on multiple resources.
    ///     WARNING: This operation is NOT atomic. It acquires locks sequentially. If it fails to acquire
    ///     a lock on one resource, it will release all locks it has already acquired. This can lead to
    ///     race conditions in high-concurrency scenarios. For atomic multi-lock, a Lua script is required.
    /// </summary>
    public async Task<IMultiLockHandle?> AcquireMultiLockAsync(
        string[] resources,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (resources == null || resources.Length == 0)
            throw new ArgumentException("Resources cannot be null or empty", nameof(resources));

        var acquiredLocks = new List<ILockHandle>();

        return await RedisOperationExecutor.ExecuteAsync(
            async () =>
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
            },
            _logger,
            string.Join(",", resources),
            cancellationToken,
            ex =>
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

                Task.WhenAll(releaseTasks).GetAwaiter().GetResult();
                return null; // Re-throw the exception
            }
        ).ConfigureAwait(false);
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
        // Use a SHA256 hash of the resource name to create a safe and uniform key.
        // This prevents issues with special characters and key length.
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(resource));
        var hashString = Convert.ToHexString(hashBytes);
        return $"lock:{hashString}";
    }
}