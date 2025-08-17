using RedisKit.Interfaces;

namespace RedisKit.Services;

internal sealed class MultiLockHandle : IMultiLockHandle
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
        await DisposeAsyncCore();
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Synchronous dispose of managed resources
                // Since we only have async operations, we need to block here
                // This is not ideal but required for IDisposable pattern
                Task.Run(async () => await ReleaseAllAsync()).Wait(TimeSpan.FromSeconds(5));
            }

            _disposed = true;
        }
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (!_disposed)
        {
            await ReleaseAllAsync().ConfigureAwait(false);
            _disposed = true;
        }
    }
}