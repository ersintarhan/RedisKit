using System.Collections.Concurrent;
using RedisKit.Interfaces;

namespace RedisKit.Tests.InMemory;

/// <summary>
///     In-memory implementation of IDistributedLock for testing
/// </summary>
public class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, LockInfo> _locks = new();

    public Task<ILockHandle?> AcquireLockAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var lockId = Guid.NewGuid().ToString();
        var lockInfo = new LockInfo
        {
            LockId = lockId,
            ExpiryTime = DateTime.UtcNow.Add(expiry),
            Owner = lockId
        };

        // Try to acquire lock
        if (_locks.TryAdd(resource, lockInfo)) return Task.FromResult<ILockHandle?>(new InMemoryLockHandle(this, resource, lockId, lockInfo.ExpiryTime));

        // Check if existing lock is expired
        if (_locks.TryGetValue(resource, out var existingLock))
            if (existingLock.ExpiryTime <= DateTime.UtcNow)
                // Lock expired, try to replace it
                if (_locks.TryUpdate(resource, lockInfo, existingLock))
                    return Task.FromResult<ILockHandle?>(new InMemoryLockHandle(this, resource, lockId, lockInfo.ExpiryTime));

        return Task.FromResult<ILockHandle?>(null);
    }

    public async Task<ILockHandle?> AcquireLockAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken cancellationToken = default)
    {
        var endTime = DateTime.UtcNow.Add(wait);

        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            var handle = await AcquireLockAsync(resource, expiry, cancellationToken);
            if (handle != null) return handle;

            try
            {
                await Task.Delay(retry, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // Return null if cancelled during wait
                return null;
            }
        }

        return null;
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        if (_locks.TryGetValue(resource, out var lockInfo))
        {
            if (lockInfo.ExpiryTime > DateTime.UtcNow) return Task.FromResult(true);
            // Lock expired, remove it
            _locks.TryRemove(resource, out _);
        }

        return Task.FromResult(false);
    }

    public Task<bool> ExtendLockAsync(ILockHandle handle, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        if (handle is InMemoryLockHandle memHandle)
            if (_locks.TryGetValue(memHandle.Resource, out var lockInfo))
                if (lockInfo.LockId == memHandle.LockId && lockInfo.ExpiryTime > DateTime.UtcNow)
                {
                    lockInfo.ExpiryTime = DateTime.UtcNow.Add(expiry);
                    return Task.FromResult(true);
                }

        return Task.FromResult(false);
    }

    internal void ReleaseLock(string resource, string lockId)
    {
        if (_locks.TryGetValue(resource, out var lockInfo))
            if (lockInfo.LockId == lockId)
                _locks.TryRemove(resource, out _);
    }

    public void Clear()
    {
        _locks.Clear();
    }

    private class LockInfo
    {
        public string LockId { get; set; } = Guid.NewGuid().ToString();
        public DateTime ExpiryTime { get; set; }
        public string Owner { get; set; } = "";
    }

    private class InMemoryLockHandle : ILockHandle
    {
        private readonly InMemoryDistributedLock _lock;
        private bool _disposed;

        public InMemoryLockHandle(InMemoryDistributedLock lockService, string resource, string lockId, DateTime expiresAt)
        {
            _lock = lockService;
            Resource = resource;
            LockId = lockId;
            AcquiredAt = DateTime.UtcNow;
            ExpiresAt = expiresAt;
        }

        public string Resource { get; }
        public string LockId { get; }
        public bool IsAcquired => !_disposed;
        public DateTime AcquiredAt { get; }
        public DateTime ExpiresAt { get; private set; }

        public Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            if (!_disposed)
            {
                _lock.ReleaseLock(Resource, LockId);
                _disposed = true;
            }

            return Task.CompletedTask;
        }

        public Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            if (!_disposed && _lock._locks.TryGetValue(Resource, out var lockInfo))
                if (lockInfo.LockId == LockId && lockInfo.ExpiryTime > DateTime.UtcNow)
                {
                    lockInfo.ExpiryTime = DateTime.UtcNow.Add(expiry);
                    ExpiresAt = lockInfo.ExpiryTime;
                    return Task.FromResult(true);
                }

            return Task.FromResult(false);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _lock.ReleaseLock(Resource, LockId);
                _disposed = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            GC.SuppressFinalize(this);
        }
    }
}