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

            var database = await _connection.GetDatabaseAsync();
            var serialized = await _serializer.SerializeAsync(message, cancellationToken);

            // Execute SPUBLISH command
            var result = await database.ExecuteAsync(
                "SPUBLISH",
                new RedisValue[] { channel, serialized }
            ).ConfigureAwait(false);

            var subscribers = (long)result;

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

    public async Task<SubscriptionToken> SubscribePatternAsync<T>(
        string pattern,
        Func<ShardedChannelMessage<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        ArgumentNullException.ThrowIfNull(handler);

        if (!await EnsureSupportedAsync(cancellationToken)) throw new NotSupportedException("Sharded Pub/Sub is not supported. Requires Redis 7.0+");

        await _subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Subscribing to sharded pattern: {Pattern}", pattern);

            var subscription = new ShardedSubscription
            {
                Channel = pattern,
                IsPattern = true
            };

            _subscriptions[subscription.Id] = subscription;

            // Wrap the typed handler
            var wrappedHandler = CreateTypedHandler(handler, null, pattern);

            var patternKey = $"pattern:{pattern}";
            if (!_handlers.TryGetValue(patternKey, out var patternHandlers))
            {
                patternHandlers = new List<Func<ShardedChannelMessage<object>, CancellationToken, Task>>();
                _handlers[patternKey] = patternHandlers;

                // First subscription to this pattern, subscribe to Redis
                await SubscribeToRedisAsync(pattern, true);
            }

            patternHandlers.Add(wrappedHandler);

            _logger.LogInformation("Subscribed to sharded pattern: {Pattern}", pattern);

            return new SubscriptionToken(
                subscription.Id,
                pattern,
                SubscriptionType.Pattern,
                async () => await UnsubscribePatternAsync(pattern, CancellationToken.None)
            );
        }
        finally
        {
            _subscriptionLock.Release();
        }
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

    public async Task UnsubscribePatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        if (!await EnsureSupportedAsync(cancellationToken)) return;

        await _subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Unsubscribing from sharded pattern: {Pattern}", pattern);

            var patternKey = $"pattern:{pattern}";
            if (_handlers.TryRemove(patternKey, out _)) await UnsubscribeFromRedisAsync(pattern, true);

            // Remove related subscriptions
            var toRemove = _subscriptions.Where(s => s.Value.Channel == pattern && s.Value.IsPattern)
                .Select(s => s.Key)
                .ToList();

            foreach (var id in toRemove) _subscriptions.TryRemove(id, out _);

            _logger.LogInformation("Unsubscribed from sharded pattern: {Pattern}", pattern);
        }
        finally
        {
            _subscriptionLock.Release();
        }
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

            // Execute PUBSUB SHARDCHANNELS command
            var channelsResult = await database.ExecuteAsync("PUBSUB", new RedisValue[] { "SHARDCHANNELS" })
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

            // Execute PUBSUB SHARDNUMSUB command
            var result = await database.ExecuteAsync(
                "PUBSUB",
                new RedisValue[] { "SHARDNUMSUB", channel }
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
                await database.ExecuteAsync("SPUBLISH", new RedisValue[] { "test", "test" })
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
        var multiplexer = await _connection.GetMultiplexerAsync();

        var command = isPattern ? "PSUBSCRIBE" : "SSUBSCRIBE";

        // Note: StackExchange.Redis doesn't have direct support for SSUBSCRIBE
        // We need to use Execute on IDatabase for these commands
        var db = multiplexer.GetDatabase();
        await db.ExecuteAsync(command, channelOrPattern)
            .ConfigureAwait(false);

        // Set up message handler using regular pub/sub as fallback
        var subscriber = multiplexer.GetSubscriber();
        if (isPattern)
            await subscriber.SubscribeAsync(
                new RedisChannel(channelOrPattern, RedisChannel.PatternMode.Pattern),
                async (channel, value) => await HandleMessageAsync(channel, value, true)
            ).ConfigureAwait(false);
        else
            await subscriber.SubscribeAsync(
                new RedisChannel(channelOrPattern, RedisChannel.PatternMode.Literal),
                async (channel, value) => await HandleMessageAsync(channel, value, false)
            ).ConfigureAwait(false);
    }

    private async Task UnsubscribeFromRedisAsync(string channelOrPattern, bool isPattern)
    {
        var multiplexer = await _connection.GetMultiplexerAsync();
        var db = multiplexer.GetDatabase();
        var subscriber = multiplexer.GetSubscriber();

        var command = isPattern ? "PUNSUBSCRIBE" : "SUNSUBSCRIBE";

        await db.ExecuteAsync(command, channelOrPattern)
            .ConfigureAwait(false);

        if (isPattern)
            await subscriber.UnsubscribeAsync(
                new RedisChannel(channelOrPattern, RedisChannel.PatternMode.Pattern)
            ).ConfigureAwait(false);
        else
            await subscriber.UnsubscribeAsync(
                new RedisChannel(channelOrPattern, RedisChannel.PatternMode.Literal)
            ).ConfigureAwait(false);
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
        // In a real implementation, this would get the actual shard ID
        // For now, return a placeholder
        return Environment.MachineName;
    }
}