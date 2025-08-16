using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedisKit.Interfaces
{
    /// <summary>
    /// Represents a distributed lock that can be acquired across multiple processes/machines
    /// </summary>
    public interface IDistributedLock
    {
        /// <summary>
        /// Attempts to acquire a distributed lock
        /// </summary>
        /// <param name="resource">The resource identifier to lock</param>
        /// <param name="expiry">Lock expiration time</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A lock handle if successful, null if lock could not be acquired</returns>
        Task<ILockHandle?> AcquireLockAsync(
            string resource,
            TimeSpan expiry,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to acquire a distributed lock with retry logic
        /// </summary>
        /// <param name="resource">The resource identifier to lock</param>
        /// <param name="expiry">Lock expiration time</param>
        /// <param name="wait">Maximum time to wait for lock acquisition</param>
        /// <param name="retry">Retry interval</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A lock handle if successful, null if lock could not be acquired</returns>
        Task<ILockHandle?> AcquireLockAsync(
            string resource,
            TimeSpan expiry,
            TimeSpan wait,
            TimeSpan retry,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a lock is currently held
        /// </summary>
        /// <param name="resource">The resource identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if locked, false otherwise</returns>
        Task<bool> IsLockedAsync(
            string resource,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to extend the expiry of an existing lock
        /// </summary>
        /// <param name="handle">The lock handle to extend</param>
        /// <param name="expiry">New expiration time</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if extended successfully, false otherwise</returns>
        Task<bool> ExtendLockAsync(
            ILockHandle handle,
            TimeSpan expiry,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a handle to an acquired distributed lock
    /// </summary>
    public interface ILockHandle : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Gets the resource identifier this lock is for
        /// </summary>
        string Resource { get; }

        /// <summary>
        /// Gets the unique lock token
        /// </summary>
        string LockId { get; }

        /// <summary>
        /// Gets whether the lock is still valid
        /// </summary>
        bool IsAcquired { get; }

        /// <summary>
        /// Gets the lock acquisition time
        /// </summary>
        DateTime AcquiredAt { get; }

        /// <summary>
        /// Gets the lock expiry time
        /// </summary>
        DateTime ExpiresAt { get; }

        /// <summary>
        /// Releases the lock
        /// </summary>
        Task ReleaseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Extends the lock expiry
        /// </summary>
        Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default);
    }
}