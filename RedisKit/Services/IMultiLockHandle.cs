using RedisKit.Interfaces;

namespace RedisKit.Services;

/// <summary>
///     Handle for multiple locks acquired atomically
/// </summary>
public interface IMultiLockHandle : IAsyncDisposable, IDisposable
{
    IReadOnlyList<ILockHandle> Locks { get; }
    Task ReleaseAllAsync(CancellationToken cancellationToken = default);
}