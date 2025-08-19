using System.Buffers;

namespace RedisKit.Helpers;

/// <summary>
///     Provides a safe wrapper around ArrayPool for serialization operations
/// </summary>
internal sealed class SafeSerializationBuffer : IDisposable
{
    private byte[]? _buffer;
    private bool _disposed;

    public SafeSerializationBuffer(int minimumSize = 1024)
    {
        // Ensure minimum size for efficiency
        var requestedSize = Math.Max(minimumSize, 1024);
        _buffer = ArrayPool<byte>.Shared.Rent(requestedSize);
        Size = _buffer.Length;
    }

    /// <summary>
    ///     Gets the buffer as Memory of byte
    /// </summary>
    public Memory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer.AsMemory();
        }
    }

    /// <summary>
    ///     Gets the buffer as byte array (use carefully)
    /// </summary>
    public byte[] Buffer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer!;
        }
    }

    /// <summary>
    ///     Gets the actual size of the rented buffer
    /// </summary>
    public int Size { get; }

    public void Dispose()
    {
        if (_disposed) return;

        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer != null)
            // Clear the buffer for security (important for sensitive data)
            ArrayPool<byte>.Shared.Return(buffer, true);

        _disposed = true;
    }
}