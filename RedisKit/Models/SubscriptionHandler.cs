namespace RedisKit.Models;

/// <summary>
///     Internal subscription handler wrapper
/// </summary>
internal class SubscriptionHandler
{
    public SubscriptionHandler(string id, Func<object, CancellationToken, Task> handler, Type messageType)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        SubscribedAt = DateTime.UtcNow;
    }

    public string Id { get; }
    public Func<object, CancellationToken, Task> Handler { get; }
    public Type MessageType { get; }
    public DateTime SubscribedAt { get; }
}