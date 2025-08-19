namespace RedisKit.Helpers;

/// <summary>
///     Provides lazy asynchronous initialization with thread-safety
/// </summary>
internal sealed class AsyncLazy<T>
{
    private readonly Lazy<Task<T>> _lazy;

    public AsyncLazy(Func<Task<T>> taskFactory)
    {
        _lazy = new Lazy<Task<T>>(() => Task.Run(taskFactory));
    }

    public AsyncLazy(Func<Task<T>> taskFactory, LazyThreadSafetyMode mode)
    {
        _lazy = new Lazy<Task<T>>(() => Task.Run(taskFactory), mode);
    }

    /// <summary>
    ///     Gets the lazily initialized value
    /// </summary>
    public Task<T> Value => _lazy.Value;

    /// <summary>
    ///     Gets whether the value has been created
    /// </summary>
    public bool IsValueCreated => _lazy.IsValueCreated;

    /// <summary>
    ///     Gets the value if it's already available, otherwise returns default
    /// </summary>
    public T? GetValueOrDefault()
    {
        if (_lazy.IsValueCreated && _lazy.Value.IsCompletedSuccessfully)
            return _lazy.Value.Result;
        return default;
    }
}