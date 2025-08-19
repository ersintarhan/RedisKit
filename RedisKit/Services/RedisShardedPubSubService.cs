using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     Service for Redis Sharded Pub/Sub operations (Redis 7.0+)
/// </summary>
public class RedisShardedPubSubService : IRedisShardedPubSub, IDisposable
{
    private readonly IRedisConnection _connection;
    private readonly ConcurrentDictionary<string, List<Func<ShardedChannelMessage<object>, CancellationToken, Task>>> _handlers = new();
    private readonly ILogger<RedisShardedPubSubService> _logger;
    private readonly RedisOptions _options;
    private readonly IRedisSerializer _serializer;
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ShardedSubscription> _subscriptions = new();
    private readonly SemaphoreSlim _supportCheckLock = new(1, 1);
    private bool _disposed;
    private bool? _isSupported;

    public RedisShardedPubSubService(
        IRedisConnection connection,
        ILogger<RedisShardedPubSubService> logger,
        IOptions<RedisOptions> options,
        IRedisSerializer? serializer = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? RedisSerializerFactory.Create(_options.Serializer);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Unsubscribe all
        var tasks = _subscriptions.Values.Select(s =>
            s.IsPattern
                ? UnsubscribePatternAsync(s.Channel, CancellationToken.None)
                : UnsubscribeAsync(s.Channel, CancellationToken.None)
        );

        Task.WhenAll(tasks).GetAwaiter().GetResult();

        _subscriptions.Clear();
        _handlers.Clear();
        _supportCheckLock?.Dispose();
        _subscriptionLock?.Dispose();
    }

    public async Task<long> PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(message);

        if (!await EnsureSupportedAsync(cancellationToken)) throw new NotSupportedException("Sharded Pub/Sub is not supported. Requires Redis 7.0+");

        try
        {
            _logger.LogDebug("Publishing to sharded channel: {Channel}", channel);

            var subscriber = await _connection.GetSubscriberAsync();
            var serialized = await _serializer.SerializeAsync(message, cancellationToken);

            // Use native sharded pub/sub support
            var subscribers = await subscriber.PublishAsync(RedisChannel.Sharded(channel), serialized).ConfigureAwait(false);

            _logger.LogInformation("Published to sharded channel {Channel}, reached {Subscribers} subscribers",
                channel, subscribers);

            return subscribers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing to sharded channel {Channel}", channel);
            throw;
        }
    }

    public async Task<SubscriptionToken> SubscribeAsync<T>(
        string channel,
        Func<ShardedChannelMessage<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(handler);

        if (!await EnsureSupportedAsync(cancellationToken)) throw new NotSupportedException("Sharded Pub/Sub is not supported. Requires Redis 7.0+");

        await _subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Subscribing to sharded channel: {Channel}", channel);

            var subscription = new ShardedSubscription
            {
                Channel = channel,
                IsPattern = false
            };

            _subscriptions[subscription.Id] = subscription;

            // Wrap the typed handler
            var wrappedHandler = CreateTypedHandler(handler, channel, null);

            if (!_handlers.TryGetValue(channel, out var channelHandlers))
            {
                channelHandlers = new List<Func<ShardedChannelMessage<object>, CancellationToken, Task>>();
                _handlers[channel] = channelHandlers;

                // First subscription to this channel, subscribe to Redis
                await SubscribeToRedisAsync(channel, false);
            }

            channelHandlers.Add(wrappedHandler);

            _logger.LogInformation("Subscribed to sharded channel: {Channel}", channel);

            return new SubscriptionToken(
                subscription.Id,
                channel,
                SubscriptionType.Channel,
                async () => await UnsubscribeAsync(channel, CancellationToken.None)
            );
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    public Task<SubscriptionToken> SubscribePatternAsync<T>(
        string pattern,
        Func<ShardedChannelMessage<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        // Sharded pub/sub doesn't support pattern subscriptions
        throw new NotSupportedException("Sharded Pub/Sub does not support pattern subscriptions. Use regular Pub/Sub for pattern matching.");
    }

    public async Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);

        if (!await EnsureSupportedAsync(cancellationToken)) return;

        await _subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Unsubscribing from sharded channel: {Channel}", channel);

            if (_handlers.TryRemove(channel, out _)) await UnsubscribeFromRedisAsync(channel, false);

            // Remove related subscriptions
            var toRemove = _subscriptions.Where(s => s.Value.Channel == channel && !s.Value.IsPattern)
                .Select(s => s.Key)
                .ToList();

            foreach (var id in toRemove) _subscriptions.TryRemove(id, out _);

            _logger.LogInformation("Unsubscribed from sharded channel: {Channel}", channel);
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    public Task UnsubscribePatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // Sharded pub/sub doesn't support pattern subscriptions
        throw new NotSupportedException("Sharded Pub/Sub does not support pattern subscriptions. Use regular Pub/Sub for pattern matching.");
    }

    public async Task UnsubscribeAsync(SubscriptionToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Type == SubscriptionType.Pattern)
            await UnsubscribePatternAsync(token.ChannelOrPattern, cancellationToken);
        else
            await UnsubscribeAsync(token.ChannelOrPattern, cancellationToken);
    }

    public async Task<ShardedPubSubStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureSupportedAsync(cancellationToken)) return new ShardedPubSubStats();

        try
        {
            _logger.LogDebug("Getting sharded pub/sub statistics");

            var database = await _connection.GetDatabaseAsync();

            // Note: PUBSUB SHARDCHANNELS requires raw command execution
            // StackExchange.Redis doesn't have native support for this command yet
            var channelsResult = await database.ExecuteAsync("PUBSUB", "SHARDCHANNELS")
                .ConfigureAwait(false);

            var stats = new ShardedPubSubStats
            {
                TotalChannels = _handlers.Count(h => !h.Key.StartsWith("pattern:")),
                TotalPatterns = _handlers.Count(h => h.Key.StartsWith("pattern:")),
                TotalSubscribers = _subscriptions.Count,
                CollectedAt = DateTime.UtcNow
            };

            // Parse channels from result
            if (channelsResult.Resp3Type == ResultType.Array)
            {
                var channels = (RedisResult[])channelsResult!;
                stats.TotalChannels = channels.Length;
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sharded pub/sub statistics");
            return new ShardedPubSubStats();
        }
    }

    public async Task<long> GetSubscriberCountAsync(string channel, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);

        if (!await EnsureSupportedAsync(cancellationToken)) return 0;

        try
        {
            _logger.LogDebug("Getting subscriber count for sharded channel: {Channel}", channel);

            var database = await _connection.GetDatabaseAsync();

            // Note: PUBSUB SHARDNUMSUB requires raw command execution
            // StackExchange.Redis doesn't have native support for this command yet
            var result = await database.ExecuteAsync(
                "PUBSUB",
                "SHARDNUMSUB", channel
            ).ConfigureAwait(false);

            if (result.Resp3Type == ResultType.Array)
            {
                var items = (RedisResult[])result!;
                if (items.Length >= 2) return (long)items[1];
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriber count for channel {Channel}", channel);
            return 0;
        }
    }

    public async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        return await EnsureSupportedAsync(cancellationToken);
    }

    private async Task<bool> EnsureSupportedAsync(CancellationToken cancellationToken)
    {
        if (_isSupported.HasValue)
            return _isSupported.Value;

        await _supportCheckLock.WaitAsync(cancellationToken);
        try
        {
            if (_isSupported.HasValue)
                return _isSupported.Value;

            var database = await _connection.GetDatabaseAsync();

            try
            {
                // Try to execute SPUBLISH to check support
                // Use native sharded channel support
                var subscriber = await _connection.GetSubscriberAsync();
                await subscriber.PublishAsync(RedisChannel.Sharded("test"), "test")
                    .ConfigureAwait(false);
                _isSupported = true;
                _logger.LogInformation("Sharded Pub/Sub is supported");
            }
            catch (RedisServerException ex) when (ex.Message.Contains("unknown command"))
            {
                _isSupported = false;
                _logger.LogWarning("Sharded Pub/Sub is not supported. Requires Redis 7.0+");
            }

            return _isSupported.Value;
        }
        finally
        {
            _supportCheckLock.Release();
        }
    }

    private Func<ShardedChannelMessage<object>, CancellationToken, Task> CreateTypedHandler<T>(
        Func<ShardedChannelMessage<T>, CancellationToken, Task> handler,
        string? channel,
        string? pattern) where T : class
    {
        return async (message, ct) =>
        {
            try
            {
                T data;
                if (message.Data is T typedData)
                    data = typedData;
                else if (message.Data is byte[] bytes)
                    data = await _serializer.DeserializeAsync<T>(bytes, ct)
                           ?? throw new InvalidOperationException($"Failed to deserialize message data for channel {message.Channel}");
                else
                    throw new InvalidOperationException($"Unexpected message data type: {message.Data?.GetType().Name ?? "null"}");

                var typedMessage = new ShardedChannelMessage<T>
                {
                    Channel = message.Channel,
                    Data = data,
                    ShardId = message.ShardId,
                    ReceivedAt = message.ReceivedAt,
                    Metadata = message.Metadata,
                    Pattern = pattern
                };

                await handler(typedMessage, ct);

                // Update subscription stats
                var subscription = _subscriptions.Values
                    .FirstOrDefault(s => s.Channel == (channel ?? pattern));
                if (subscription != null)
                {
                    subscription.MessageCount++;
                    subscription.LastMessageAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling sharded message on channel {Channel}", channel ?? pattern);
            }
        };
    }

    private async Task SubscribeToRedisAsync(string channelOrPattern, bool isPattern)
    {
        if (isPattern)
            // Sharded pub/sub doesn't support patterns
            throw new NotSupportedException("Sharded Pub/Sub does not support pattern subscriptions");

        var subscriber = await _connection.GetSubscriberAsync();

        // Use native sharded channel support
        await subscriber.SubscribeAsync(
            RedisChannel.Sharded(channelOrPattern),
            async (channel, value) => await HandleMessageAsync(channel.ToString(), value, false)
        ).ConfigureAwait(false);
    }

    private async Task UnsubscribeFromRedisAsync(string channelOrPattern, bool isPattern)
    {
        if (isPattern)
            // Sharded pub/sub doesn't support patterns
            throw new NotSupportedException("Sharded Pub/Sub does not support pattern subscriptions");

        var subscriber = await _connection.GetSubscriberAsync();

        // Use native sharded channel support
        await subscriber.UnsubscribeAsync(RedisChannel.Sharded(channelOrPattern)).ConfigureAwait(false);
    }

    private async Task HandleMessageAsync(RedisChannel channel, RedisValue value, bool isPattern)
    {
        try
        {
            var channelName = channel.ToString();
            var handlerKey = isPattern ? $"pattern:{channelName}" : channelName;

            if (_handlers.TryGetValue(handlerKey, out var handlers))
            {
                var message = new ShardedChannelMessage<object>
                {
                    Channel = channelName,
                    Data = (byte[])value!,
                    ShardId = GetShardId(),
                    ReceivedAt = DateTime.UtcNow
                };

                var tasks = handlers.Select(h => h(message, CancellationToken.None));
                await Task.WhenAll(tasks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling sharded message");
        }
    }

    private static string GetShardId()
    {
        // TODO: Extract actual shard ID from Redis cluster metadata
        // Current implementation returns a placeholder value (machine name)
        // Proper implementation requires:
        // 1. Access to Redis cluster topology
        // 2. Extraction of shard/slot information from message metadata
        // 3. Mapping of channel hash to appropriate shard
        // This is a limitation of StackExchange.Redis which doesn't expose shard info in pub/sub callbacks
        return Environment.MachineName;
    }
}