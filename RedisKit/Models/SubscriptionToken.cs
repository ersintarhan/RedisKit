namespace RedisKit.Models
{
    /// <summary>
    /// Represents a subscription to a Redis channel or pattern
    /// </summary>
    public class SubscriptionToken : IDisposable
    {
        private readonly Func<Task>? _disposeAction;
        private bool _disposed;

        public string Id { get; }
        public string ChannelOrPattern { get; }
        public SubscriptionType Type { get; }

        public SubscriptionToken(string id, string channelOrPattern, SubscriptionType type, Func<Task>? disposeAction = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ChannelOrPattern = channelOrPattern ?? throw new ArgumentNullException(nameof(channelOrPattern));
            Type = type;
            _disposeAction = disposeAction;
        }

        public async Task UnsubscribeAsync()
        {
            if (!_disposed && _disposeAction != null)
            {
                await _disposeAction();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposeAction?.Invoke().GetAwaiter().GetResult();
                _disposed = true;
            }
        }
    }
}