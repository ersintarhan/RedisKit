namespace RedisKit.Models;

/// <summary>
///     Represents a subscription to a Redis channel or pattern, allowing for unsubscribing and resource cleanup.
/// </summary>
public class SubscriptionToken : IDisposable
{
    private readonly Func<Task>? _disposeAction;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SubscriptionToken" /> class.
    /// </summary>
    /// <param name="id">The unique identifier for the subscription.</param>
    /// <param name="channelOrPattern">The name of the channel or pattern being subscribed to.</param>
    /// <param name="type">The type of subscription (e.g., Channel, Pattern).</param>
    /// <param name="disposeAction">An optional action to be executed when the token is disposed, typically for unsubscribing from Redis.</param>
    /// <exception cref="ArgumentNullException">Thrown if any of the required arguments are null.</exception>
    public SubscriptionToken(string id, string channelOrPattern, SubscriptionType type, Func<Task>? disposeAction = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ChannelOrPattern = channelOrPattern ?? throw new ArgumentNullException(nameof(channelOrPattern));
        Type = type;
        _disposeAction = disposeAction;
    }

    /// <summary>
    ///     Gets the unique identifier for the subscription.
    /// </summary>
    public string Id { get; }

    /// <summary>
    ///     Gets the name of the channel or pattern being subscribed to.
    /// </summary>
    public string ChannelOrPattern { get; }

    /// <summary>
    ///     Gets the type of subscription.
    /// </summary>
    public SubscriptionType Type { get; }

    /// <summary>
    ///     Disposes of the token, executing any associated disposal action.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Asynchronously unsubscribes from the Redis channel or pattern.
    /// </summary>
    public async Task UnsubscribeAsync()
    {
        if (!_disposed && _disposeAction != null)
        {
            await _disposeAction();
            _disposed = true;
        }
    }

    /// <summary>
    ///     Protected virtual method invoked during disposal to perform cleanup tasks.
    /// </summary>
    /// <param name="disposing">True if the object is being disposed as opposed to finalized.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing) _disposeAction?.Invoke().GetAwaiter().GetResult();
            _disposed = true;
        }
    }
}