using System;
using System.Threading;
using System.Threading.Tasks;
using RedisKit.Interfaces;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace RedisKit.Services
{
    /// <summary>
    /// Represents a handle to an acquired Redis distributed lock
    /// </summary>
    internal sealed class RedisLockHandle : ILockHandle
    {
        private readonly IDatabase _database;
        private readonly ILogger<RedisLockHandle>? _logger;
        private readonly Timer? _renewalTimer;
        private readonly TimeSpan _defaultExpiry;
        private bool _disposed;
        private bool _isAcquired;

        public string Resource { get; }
        public string LockId { get; }
        public DateTime AcquiredAt { get; }
        public DateTime ExpiresAt { get; private set; }
        public bool IsAcquired => _isAcquired && DateTime.UtcNow < ExpiresAt;

        public RedisLockHandle(
            IDatabase database,
            string resource,
            string lockId,
            TimeSpan expiry,
            bool enableAutoRenewal = false,
            ILogger<RedisLockHandle>? logger = null)
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
                var renewalInterval = TimeSpan.FromMilliseconds(expiry.TotalMilliseconds / 3);
                _renewalTimer = new Timer(
                    async _ => await TryRenewAsync(),
                    null,
                    renewalInterval,
                    renewalInterval);
            }
        }

        private async Task TryRenewAsync()
        {
            if (_disposed || !_isAcquired)
                return;

            try
            {
                var success = await ExtendAsync(_defaultExpiry);
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during lock auto-renewal for resource: {Resource}", Resource);
                _isAcquired = false;
            }
        }

        public async Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            if (_disposed || !_isAcquired)
                return false;

            try
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

                if ((int)result == 1)
                {
                    ExpiresAt = DateTime.UtcNow.Add(expiry);
                    return true;
                }

                _isAcquired = false;
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to extend lock for resource: {Resource}", Resource);
                return false;
            }
        }

        public async Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || !_isAcquired)
                return;

            try
            {
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
                {
                    _logger?.LogDebug("Released lock for resource: {Resource}", Resource);
                }
                else
                {
                    _logger?.LogWarning("Lock was already released or expired for resource: {Resource}", Resource);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to release lock for resource: {Resource}", Resource);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Stop renewal timer
            if (_renewalTimer != null)
            {
                await _renewalTimer.DisposeAsync();
            }

            // Release the lock
            if (_isAcquired)
            {
                await ReleaseAsync();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Stop renewal timer
            _renewalTimer?.Dispose();

            // Release the lock synchronously
            if (_isAcquired)
            {
                Task.Run(async () => await ReleaseAsync()).Wait(TimeSpan.FromSeconds(5));
            }
        }

        private static string GetLockKey(string resource) => $"lock:{resource}";
    }
}