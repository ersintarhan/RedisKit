namespace RedisKit.Interfaces;

/// <summary>
///     Represents a handle to an acquired distributed lock
/// </summary>
public interface ILockHandle : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Gets the resource identifier this lock is for
    /// </summary>
    string Resource { get; }

    /// <summary>
    ///     Gets the unique lock token
    /// </summary>
    string LockId { get; }

    /// <summary>
    ///     Gets whether the lock is still valid
    /// </summary>
    bool IsAcquired { get; }

    /// <summary>
    ///     Gets the lock acquisition time
    /// </summary>
    DateTime AcquiredAt { get; }

    /// <summary>
    ///     Gets the lock expiry time
    /// </summary>
    DateTime ExpiresAt { get; }

    /// <summary>
    ///     Releases the lock
    /// </summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Extends the lock expiry
    /// </summary>
    Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default);
}