using RedisKit.Interfaces;

namespace RedisKit.Services;

internal class MultiLockHandle : IMultiLockHandle
{
    private readonly List<ILockHandle> _locks;
    private bool _disposed;

    public IReadOnlyList<ILockHandle> Locks => _locks.AsReadOnly();

    public MultiLockHandle(List<ILockHandle> locks)
    {
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
    }

    public async Task ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        var tasks = _locks.Select(l => l.ReleaseAsync(cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await ReleaseAllAsync();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Task.Run(async () => await ReleaseAllAsync()).Wait(TimeSpan.FromSeconds(5));
    }
}