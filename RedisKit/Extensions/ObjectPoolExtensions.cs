using Microsoft.Extensions.ObjectPool;
using RedisKit.Utilities;

namespace RedisKit.Extensions;

/// <summary>
/// Extension methods for <see cref="ObjectPool{T}"/>.
/// </summary>
public static class ObjectPoolExtensions
{
    /// <summary>
    /// Gets a pooled object wrapped in a disposable struct.
    /// </summary>
    /// <typeparam name="T">The type of the pooled object.</typeparam>
    /// <param name="pool">The object pool.</param>
    /// <returns>A <see cref="PooledResource{T}"/> that wraps the pooled object.</returns>
    public static PooledResource<T> GetPooled<T>(this ObjectPool<T> pool) where T : class
    {
        return new PooledResource<T>(pool, pool.Get());
    }
}
