namespace RedisKit.Models;

/// <summary>
///     Internal subscription handler wrapper.
/// </summary>
/// <remarks>
///     This class encapsulates the details of a subscription, including its unique identifier,
///     the handler function to be invoked when a message is received, the type of message it handles,
///     and the time at which it was subscribed.  It's used internally to manage pub/sub subscriptions
///     within the RedisKit library.
/// </remarks>
internal class SubscriptionHandler
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SubscriptionHandler" /> class.
    /// </summary>
    /// <param name="id">The unique identifier for the subscription.</param>
    /// <param name="handler">The function to be invoked when a message is received.</param>
    /// <param name="messageType">The type of message that this handler handles.</param>
    /// <exception cref="ArgumentNullException">Thrown if any of the input arguments are null.</exception>
    public SubscriptionHandler(string id, Func<object, CancellationToken, Task> handler, Type messageType)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        SubscribedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the unique identifier for the subscription.
    /// </summary>
    public string Id { get; }

    /// <summary>
    ///     Gets the function to be invoked when a message is received.
    /// </summary>
    public Func<object, CancellationToken, Task> Handler { get; }

    /// <summary>
    ///     Gets the type of message that this handler handles.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Gets the time at which the subscription was created.
    /// </summary>
    public DateTime SubscribedAt { get; }
}