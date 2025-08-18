using Microsoft.Extensions.ObjectPool;
using System;

namespace RedisKit.Utilities;

/// <summary>
/// A disposable struct that wraps a pooled object.
/// </summary>
/// <typeparam name="T">The type of the pooled object.</typeparam>
public readonly struct PooledResource<T> : IDisposable where T : class
{
    private readonly ObjectPool<T> _pool;

    /// <summary>
    /// Gets the wrapped pooled object.
    /// </summary>
    public T Value { get; }

    internal PooledResource(ObjectPool<T> pool, T value)
    {
        _pool = pool;
        Value = value;
    }

    /// <summary>
    /// Returns the wrapped object to the pool.
    /// </summary>
    public void Dispose()
    {
        _pool.Return(Value);
    }
}
