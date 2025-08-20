using Microsoft.Extensions.Logging;
using RedisKit.Helpers;
using RedisKit.Interfaces;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     Represents a handle to an acquired Redis distributed lock
/// </summary>
internal sealed class RedisLockHandle : ILockHandle
{
    private readonly IDatabase _database;
    private readonly TimeSpan _defaultExpiry;
    private readonly ILogger? _logger;
    private readonly Timer? _renewalTimer;
    private bool _disposed;
    private bool _isAcquired;

    public RedisLockHandle(
        IDatabase database,
        string resource,
        string lockId,
        TimeSpan expiry,
        bool enableAutoRenewal = false,
        ILogger? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        LockId = lockId ?? throw new ArgumentNullException(nameof(lockId));
        _logger = logger;
        _defaultExpiry = expiry;
        _isAcquired = true;

        AcquiredAt = DateTime.UtcNow;
        ExpiresAt = AcquiredAt.Add(expiry);

        // Setup auto-renewal if enabled
        if (enableAutoRenewal && expiry.TotalMilliseconds > 1000)
        {
            var renewalInterval = CalculateRenewalInterval(expiry);
            _renewalTimer = new Timer(
                async _ => await TryRenewAsync(),
                null,
                renewalInterval,
                renewalInterval);

            _logger?.LogDebug(
                "Auto-renewal enabled for lock {Resource} with interval: {RenewalInterval}ms (expiry: {Expiry}ms)",
                Resource,
                renewalInterval.TotalMilliseconds,
                expiry.TotalMilliseconds);
        }
    }

    public string Resource { get; }
    public string LockId { get; }
    public DateTime AcquiredAt { get; }
    public DateTime ExpiresAt { get; private set; }
    public bool IsAcquired => _isAcquired && DateTime.UtcNow < ExpiresAt;

    public async Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        if (_disposed || !_isAcquired)
            return false;

        var extendResult = await RedisOperationExecutor.ExecuteWithSilentErrorHandlingAsync(
            async () =>
            {
                var script = @"
                        if redis.call('get', KEYS[1]) == ARGV[1] then
                            return redis.call('pexpire', KEYS[1], ARGV[2])
                        else
                            return 0
                        end";

                var result = await _database.ScriptEvaluateAsync(
                    script,
                    new RedisKey[] { GetLockKey(Resource) },
                    new RedisValue[] { LockId, (long)expiry.TotalMilliseconds },
                    CommandFlags.DemandMaster);

                return (object?)(int)result;
            },
            _logger,
            "extend lock",
            0,
            GetLockKey(Resource)
        ).ConfigureAwait(false);

        var resultInt = extendResult is int result ? result : 0;

        if (resultInt == 1)
        {
            ExpiresAt = DateTime.UtcNow.Add(expiry);
            return true;
        }

        _isAcquired = false;
        return false;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_isAcquired)
            return;

        // Lua script to ensure we only delete our own lock
        var script = @"
                    if redis.call('get', KEYS[1]) == ARGV[1] then
                        return redis.call('del', KEYS[1])
                    else
                        return 0
                    end";

        var result = await _database.ScriptEvaluateAsync(
            script,
            new RedisKey[] { GetLockKey(Resource) },
            new RedisValue[] { LockId },
            CommandFlags.DemandMaster);

        _isAcquired = false;

        if ((int)result == 1)
            _logger?.LogDebug("Released lock for resource: {Resource}", Resource);
        else
            _logger?.LogWarning("Lock was already released or expired for resource: {Resource}", Resource);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop renewal timer
        if (_renewalTimer != null) await _renewalTimer.DisposeAsync().ConfigureAwait(false);

        // Release the lock
        if (_isAcquired) await ReleaseAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop renewal timer
        _renewalTimer?.Dispose();

        // Release the lock synchronously with timeout to prevent hanging
        if (_isAcquired)
            try
            {
                Task.Run(async () => await ReleaseAsync().ConfigureAwait(false))
                    .Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to release lock {Resource} during synchronous disposal", Resource);
            }
    }

    private async Task TryRenewAsync()
    {
        if (_disposed || !_isAcquired)
            return;

        var success = await ExtendAsync(_defaultExpiry).ConfigureAwait(false);
        if (!success)
        {
            _logger?.LogWarning("Failed to auto-renew lock for resource: {Resource}", Resource);
            _isAcquired = false;
        }
        else
        {
            _logger?.LogDebug("Auto-renewed lock for resource: {Resource}", Resource);
        }
    }

    /// <summary>
    ///     Calculates an optimized renewal interval based on lock expiry time.
    ///     Prevents excessive CPU usage for short-lived locks while ensuring timely renewal.
    /// </summary>
    private static TimeSpan CalculateRenewalInterval(TimeSpan expiry)
    {
        // Define bounds for renewal interval
        const int MIN_RENEWAL_MS = 1000; // Minimum 1 second
        const int MAX_RENEWAL_MS = 30000; // Maximum 30 seconds

        // Calculate interval as 1/3 of expiry (standard practice)
        var calculatedInterval = expiry.TotalMilliseconds / 3;

        // Apply minimum bound to prevent excessive renewals
        if (calculatedInterval < MIN_RENEWAL_MS)
            // For very short locks, use minimum interval
            return TimeSpan.FromMilliseconds(MIN_RENEWAL_MS);

        // Apply maximum bound for very long locks
        if (calculatedInterval > MAX_RENEWAL_MS)
            // For very long locks, cap at maximum interval
            // This ensures we still renew frequently enough
            return TimeSpan.FromMilliseconds(MAX_RENEWAL_MS);

        // Use calculated interval if within bounds
        return TimeSpan.FromMilliseconds(calculatedInterval);
    }

    private static string GetLockKey(string resource)
    {
        return $"lock:{resource}";
    }
}