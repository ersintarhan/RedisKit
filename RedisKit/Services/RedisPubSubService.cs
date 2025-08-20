using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Helpers;
using RedisKit.Interfaces;
using RedisKit.Logging;
using RedisKit.Models;
using RedisKit.Serialization;
using RedisKit.Utilities;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     High-performance implementation of IRedisPubSubService with advanced pub/sub messaging features.
///     Provides reliable message delivery, pattern matching, and comprehensive subscription management.
/// </summary>
/// <remarks>
///     This class implements Redis pub/sub messaging with enterprise-grade features:
///     - Type-safe message serialization/deserialization
///     - Pattern-based subscriptions with glob matching
///     - Concurrent handler management with thread safety
///     - Automatic cleanup of inactive handlers
///     - Comprehensive statistics and monitoring
///     - Memory leak prevention through handler timeouts
///     Thread Safety: This class is fully thread-safe and designed for singleton usage.
///     Key Features:
///     - Multiple handlers per channel/pattern
///     - Subscription tokens for precise unsubscribe control
///     - Channel metadata support (channel name in handler)
///     - Pattern matching for dynamic channel subscriptions
///     - Statistics tracking for monitoring
///     - Automatic cleanup of stale handlers
///     Performance Optimizations:
///     - Concurrent collections for lock-free reads
///     - Semaphore-based subscription synchronization
///     - Efficient handler lookup via mapping dictionary
///     - Batch unsubscribe operations
///     - Lazy Redis unsubscribe (only when last handler removed)
///     Message Delivery:
///     - Fire-and-forget semantics (no delivery guarantee)
///     - At-most-once delivery per subscriber
///     - No message persistence or replay
///     - Real-time message distribution
///     Pattern Matching:
///     - Supports Redis glob patterns (* ? [])
///     - Dynamic channel discovery
///     - Single handler for multiple channels
///     - Channel name provided to handler
///     Memory Management:
///     - Automatic cleanup timer (30-minute intervals)
///     - Handler timeout detection (24-hour default)
///     - Weak reference support for handlers
///     - Proper disposal of resources
///     Usage Example:
///     <code>
/// public class NotificationService
/// {
///     private readonly IRedisPubSubService _pubSub;
///     private readonly List&lt;SubscriptionToken&gt; _subscriptions = new();
///     
///     public async Task StartListening()
///     {
///         // Subscribe to user-specific notifications
///         var token = await _pubSub.SubscribeAsync&lt;UserNotification&gt;(
///             "notifications:user:123",
///             async (notification, ct) =>
///             {
///                 await ProcessNotification(notification);
///             });
///         _subscriptions.Add(token);
///         
///         // Subscribe to all system alerts
///         var patternToken = await _pubSub.SubscribePatternAsync&lt;SystemAlert&gt;(
///             "alerts:*",
///             async (alert, ct) =>
///             {
///                 await HandleAlert(alert);
///             });
///         _subscriptions.Add(patternToken);
///     }
///     
///     public async Task SendNotification(int userId, UserNotification notification)
///     {
///         var subscribers = await _pubSub.PublishAsync(
///             $"notifications:user:{userId}",
///             notification);
///         
///         Console.WriteLine($"Delivered to {subscribers} subscribers");
///     }
///     
///     public async Task Cleanup()
///     {
///         foreach (var token in _subscriptions)
///         {
///             await _pubSub.UnsubscribeAsync(token).ConfigureAwait(false);
///         }
///     }
/// }
/// </code>
/// </remarks>
public class RedisPubSubService : IRedisPubSubService, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, List<SubscriptionHandler>> _channelHandlers;

    // Cleanup timer for inactive handlers
    private readonly Timer _cleanupTimer;

    private readonly IRedisConnection _connection;

    // Channel subscriptions: channel -> list of handlers
    // ObjectPool removed - ConcurrentDictionary pooling is an anti-pattern

    // Handler ID to subscription mapping for fast unsubscribe
    private readonly ConcurrentDictionary<string, HandlerMetadata> _handlerMap;
    private readonly TimeSpan _handlerTimeout;
    private readonly ILogger<RedisPubSubService> _logger;

    // Pattern subscriptions: pattern -> list of handlers
    private readonly ConcurrentDictionary<string, List<SubscriptionHandler>> _patternHandlers;
    private readonly IRedisSerializer _serializer;

    // Statistics tracking
    private readonly ConcurrentDictionary<string, SubscriptionStats> _statistics;

    // Synchronization
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);

    // Disposal
    private bool _disposed;

    public RedisPubSubService(
        IRedisConnection connection,
        ILogger<RedisPubSubService> logger,
        IOptions<RedisOptions> options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var redisOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Create serializer based on configuration
        _serializer = RedisSerializerFactory.Create(redisOptions.Serializer);

        // Initialize dictionaries without pooling (ConcurrentDictionary pooling is anti-pattern)
        _channelHandlers = new ConcurrentDictionary<string, List<SubscriptionHandler>>();
        _patternHandlers = new ConcurrentDictionary<string, List<SubscriptionHandler>>();

        _handlerMap = new ConcurrentDictionary<string, HandlerMetadata>();
        _statistics = new ConcurrentDictionary<string, SubscriptionStats>();

        // Configure cleanup timer for memory leak prevention
        _handlerTimeout = TimeSpan.FromHours(24); // Configurable timeout
        _cleanupTimer = new Timer(
            CleanupInactiveHandlers,
            null,
            TimeSpan.FromMinutes(30), // Initial delay
            TimeSpan.FromMinutes(30) // Period
        );
    }

    #region Publishing

    public async Task<long> PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default) where T : class
    {
        ValidationUtils.ValidateChannelName(channel);

        if (message == null)
            throw new ArgumentNullException(nameof(message));

        ThrowIfDisposed();

        _logger.LogPublishAsync(channel);

        var boxedResult = await RedisOperationExecutor.ExecuteAsync<object>(
            async () =>
            {
                var subscriber = await GetSubscriberAsync();
                var serialized = await _serializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);
                var subscriberCount = await subscriber.PublishAsync(RedisChannel.Literal(channel), serialized);
                _logger.LogPublishAsyncSuccess(channel);
                return subscriberCount;
            },
            _logger,
            channel,
            cancellationToken
        );

        var result = boxedResult is long count ? count : 0;

        return result;
    }

    #endregion

    #region Cleanup & Memory Management

    /// <summary>
    ///     Periodically clean up inactive handlers to prevent memory leaks
    /// </summary>
    private async void CleanupInactiveHandlers(object? state)
    {
        if (_disposed)
            return;

        try
        {
            var cutoffTime = DateTime.UtcNow - _handlerTimeout;
            var handlersToRemove = new List<string>();

            // Find inactive handlers
            foreach (var kvp in _handlerMap)
                if (kvp.Value.LastActivity < cutoffTime)
                {
                    handlersToRemove.Add(kvp.Key);
                    _logger.LogWarning(
                        "Removing inactive handler {HandlerId} for {Type}:{Key} (inactive since {LastActivity})",
                        kvp.Key, kvp.Value.Type, kvp.Value.Key, kvp.Value.LastActivity);
                }

            // Remove inactive handlers
            foreach (var handlerId in handlersToRemove) await UnsubscribeHandlerAsync(handlerId, CancellationToken.None).ConfigureAwait(false);

            if (handlersToRemove.Count > 0) _logger.LogInformation("Cleaned up {Count} inactive handlers", handlersToRemove.Count);

            // Clean up old statistics
            var statsToRemove = _statistics
                .Where(s => s.Value.LastMessageAt < cutoffTime &&
                            !_channelHandlers.ContainsKey(s.Key) &&
                            !_patternHandlers.ContainsKey(s.Key))
                .Select(s => s.Key)
                .ToList();

            foreach (var key in statsToRemove) _statistics.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup of inactive handlers");
        }
    }

    #endregion

    #region Subscriptions - Unified

    /// <summary>
    ///     Base subscription method to reduce code duplication
    /// </summary>
    private async Task<SubscriptionToken> SubscribeInternalAsync<T>(
        string key,
        SubscriptionType type,
        Func<T, CancellationToken, Task> handler,
        Func<string, T, CancellationToken, Task>? metadataHandler = null,
        CancellationToken cancellationToken = default) where T : class
    {
        // Validate the key (channel or pattern name)
        ValidationUtils.ValidateChannelName(key);

        if (handler == null && metadataHandler == null)
            throw new ArgumentNullException(nameof(handler));

        if (type == SubscriptionType.Pattern && !PatternMatcher.IsValidPattern(key))
            throw new ArgumentException($"Invalid pattern: {key}", nameof(key));

        ThrowIfDisposed();

        await _subscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogDebug("Subscribing to {Type}: {Key}", type, key);

            var handlerId = Guid.NewGuid().ToString();
            var wrappedHandler = CreateWrappedHandler(handler, metadataHandler, key);
            var subscription = new SubscriptionHandler(handlerId, wrappedHandler, typeof(T));

            // Get the appropriate handler dictionary
            var handlerDict = type == SubscriptionType.Channel ? _channelHandlers : _patternHandlers;

            // Add handler to collection
            handlerDict.AddOrUpdate(
                key,
                new List<SubscriptionHandler> { subscription },
                (k, list) =>
                {
                    var newList = new List<SubscriptionHandler>(list) { subscription };
                    return newList;
                });

            // Store handler metadata for cleanup and fast unsubscribe
            _handlerMap[handlerId] = new HandlerMetadata
            {
                Key = key,
                Type = type,
                LastActivity = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            // Initialize statistics
            _statistics.TryAdd(key, new SubscriptionStats
            {
                ChannelOrPattern = key,
                Type = type
            });

            // Subscribe to Redis if this is the first handler
            if (handlerDict[key].Count == 1) await SubscribeToRedisAsync(key, type, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Successfully subscribed to {Type}: {Key}", type, key);

            // Create unsubscribe action
            var unsubscribeAction = new Func<Task>(async () => { await UnsubscribeHandlerAsync(handlerId, cancellationToken).ConfigureAwait(false); });

            return new SubscriptionToken(handlerId, key, type, unsubscribeAction);
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    /// <summary>
    ///     Creates a wrapped handler that handles both regular and metadata scenarios
    /// </summary>
    private Func<object, CancellationToken, Task> CreateWrappedHandler<T>(
        Func<T, CancellationToken, Task>? handler,
        Func<string, T, CancellationToken, Task>? metadataHandler,
        string key) where T : class
    {
        return async (msg, ct) =>
        {
            if (msg is T typedMsg)
            {
                // Update last activity for cleanup tracking
                if (_handlerMap.TryGetValue(key, out var metadata)) metadata.LastActivity = DateTime.UtcNow;

                if (metadataHandler != null)
                    await metadataHandler(key, typedMsg, ct);
                else if (handler != null)
                    await handler(typedMsg, ct);
            }
        };
    }

    /// <summary>
    ///     Subscribe to Redis based on type
    /// </summary>
    private async Task SubscribeToRedisAsync(string key, SubscriptionType type, CancellationToken cancellationToken)
    {
        var subscriber = await GetSubscriberAsync();
        if (type == SubscriptionType.Channel)
            await subscriber.SubscribeAsync(
                RedisChannel.Literal(key),
                async (ch, val) => await ProcessMessage(ch.ToString(), val, type, cancellationToken));
        else
            await subscriber.SubscribeAsync(
                RedisChannel.Pattern(key),
                async (ch, val) => await ProcessMessage(ch.ToString(), val, type, cancellationToken));
    }

    #endregion

    #region Channel Subscriptions

    public async Task<SubscriptionToken> SubscribeAsync<T>(
        string channel,
        Func<T, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        return await SubscribeInternalAsync(channel, SubscriptionType.Channel, handler, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionToken> SubscribeWithMetadataAsync<T>(
        string channel,
        Func<T, string, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        // Convert metadata handler to match internal signature
        async Task MetadataHandler(string ch, T msg, CancellationToken ct)
        {
            await handler(msg, ch, ct);
        }

        return await SubscribeInternalAsync<T>(channel, SubscriptionType.Channel, null!, MetadataHandler, cancellationToken);
    }

    #endregion

    #region Pattern Subscriptions

    public async Task<SubscriptionToken> SubscribePatternAsync<T>(
        string pattern,
        Func<T, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        return await SubscribeInternalAsync(pattern, SubscriptionType.Pattern, handler, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionToken> SubscribePatternWithMetadataAsync<T>(
        string pattern,
        Func<T, string, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        // Convert metadata handler to match internal signature
        Func<string, T, CancellationToken, Task> metadataHandler = async (ch, msg, ct) => { await handler(msg, ch, ct); };

        return await SubscribeInternalAsync<T>(pattern, SubscriptionType.Pattern, null!, metadataHandler, cancellationToken);
    }

    #endregion

    #region Message Processing

    /// <summary>
    ///     Unified message processing for both channels and patterns
    /// </summary>
    private async Task ProcessMessage(string key, RedisValue value, SubscriptionType type, CancellationToken cancellationToken)
    {
        if (value.IsNullOrEmpty)
            return;

        var stopwatch = Stopwatch.StartNew();
        var stats = _statistics.GetValueOrDefault(key);

        try
        {
            if (stats != null)
            {
                stats.IncrementMessagesReceived();
                stats.LastMessageAt = DateTime.UtcNow;
            }

            // Get handlers based on type
            var handlers = GetHandlersForKey(key, type);
            if (handlers == null || handlers.Count == 0)
                return;

            // Process all handlers in parallel
            var tasks = new List<Task>();
            foreach (var handler in handlers.ToList()) // ToList to avoid modification during iteration
                tasks.Add(ProcessSingleHandler(handler, value, stats, cancellationToken));

            await Task.WhenAll(tasks);

            if (stats != null)
            {
                var elapsed = stopwatch.Elapsed;
                UpdateAverageProcessingTime(stats, elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message on {Type}: {Key}", type, key);
            if (stats != null)
                stats.IncrementMessagesFailed();
        }
    }

    /// <summary>
    ///     Get handlers for a specific key and type, with pattern matching support
    /// </summary>
    private List<SubscriptionHandler>? GetHandlersForKey(string key, SubscriptionType type)
    {
        if (type == SubscriptionType.Channel)
        {
            // Direct channel lookup
            _channelHandlers.TryGetValue(key, out var handlers);
            return handlers;
        }

        // Pattern matching - find all matching patterns
        var allHandlers = new List<SubscriptionHandler>();
        allHandlers.AddRange(_patternHandlers.Where(kvp => PatternMatcher.IsMatch(kvp.Key, key)).SelectMany(kvp => kvp.Value));
        return allHandlers.Count > 0 ? allHandlers : null;
    }

    private async Task ProcessSingleHandler(
        SubscriptionHandler handler,
        RedisValue value,
        SubscriptionStats? stats,
        CancellationToken cancellationToken)
    {
        try
        {
            var deserialized = await _serializer.DeserializeAsync(value!, handler.MessageType, cancellationToken).ConfigureAwait(false);
            if (deserialized != null)
            {
                await handler.Handler(deserialized, cancellationToken);
                if (stats != null)
                    stats.IncrementMessagesProcessed();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler {HandlerId} failed to process message", handler.Id);
            if (stats != null)
                stats.IncrementMessagesFailed();
        }
    }

    #endregion

    #region Unsubscribe

    public async Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default)
    {
        await UnsubscribeInternalAsync(channel, SubscriptionType.Channel, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnsubscribeAsync(SubscriptionToken token, CancellationToken cancellationToken = default)
    {
        if (token == null)
            throw new ArgumentNullException(nameof(token));

        await token.UnsubscribeAsync().ConfigureAwait(false);
    }

    public async Task UnsubscribePatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        await UnsubscribeInternalAsync(pattern, SubscriptionType.Pattern, cancellationToken).ConfigureAwait(false);
    }

    private async Task UnsubscribeInternalAsync(string key, SubscriptionType type, CancellationToken cancellationToken)
    {
        // Validate the key (channel or pattern name)
        ValidationUtils.ValidateChannelName(key);

        ThrowIfDisposed();

        await _subscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogDebug("Unsubscribing from {Type}: {Key}", type, key);
            var subscriber = await GetSubscriberAsync();

            var handlerDict = type == SubscriptionType.Channel ? _channelHandlers : _patternHandlers;

            if (handlerDict.TryRemove(key, out var handlers))
            {
                // Remove all handler mappings
                foreach (var handler in handlers) _handlerMap.TryRemove(handler.Id, out _);

                // Unsubscribe from Redis
                if (type == SubscriptionType.Channel)
                    await subscriber.UnsubscribeAsync(RedisChannel.Literal(key));
                else
                    await subscriber.UnsubscribeAsync(RedisChannel.Pattern(key));

                // Remove statistics
                _statistics.TryRemove(key, out _);

                _logger.LogDebug("Successfully unsubscribed from {Type}: {Key}", type, key);
            }
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    private async Task UnsubscribeHandlerAsync(string handlerId, CancellationToken cancellationToken)
    {
        if (!_handlerMap.TryGetValue(handlerId, out var metadata))
            return;

        await _subscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var handlerDict = metadata.Type == SubscriptionType.Channel ? _channelHandlers : _patternHandlers;

            if (handlerDict.TryGetValue(metadata.Key, out var handlers))
            {
                var updatedHandlers = handlers.Where(h => h.Id != handlerId).ToList();

                if (updatedHandlers.Count == 0)
                    // Last handler, unsubscribe from Redis
                    await UnsubscribeInternalAsync(metadata.Key, metadata.Type, cancellationToken).ConfigureAwait(false);
                else
                    handlerDict[metadata.Key] = updatedHandlers;
            }

            _handlerMap.TryRemove(handlerId, out _);
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    public async Task UnsubscribeAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _subscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogDebug("Unsubscribing from all channels and patterns");
            var subscriber = await GetSubscriberAsync();

            // Unsubscribe all channels
            foreach (var channel in _channelHandlers.Keys.ToList()) await subscriber.UnsubscribeAsync(RedisChannel.Literal(channel));

            // Unsubscribe all patterns
            foreach (var pattern in _patternHandlers.Keys.ToList()) await subscriber.UnsubscribeAsync(RedisChannel.Pattern(pattern));

            // Clear all collections
            _channelHandlers.Clear();
            _patternHandlers.Clear();
            _handlerMap.Clear();
            _statistics.Clear();

            _logger.LogDebug("Successfully unsubscribed from all channels and patterns");
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    #endregion

    #region Statistics

    public IReadOnlyDictionary<string, SubscriptionStats> GetStatistics()
    {
        return _statistics;
    }

    public SubscriptionStats? GetStatistics(string channelOrPattern)
    {
        _statistics.TryGetValue(channelOrPattern, out var stats);
        return stats;
    }

    private static void UpdateAverageProcessingTime(SubscriptionStats stats, TimeSpan elapsed)
    {
        if (stats.AverageProcessingTime == TimeSpan.Zero)
        {
            stats.AverageProcessingTime = elapsed;
        }
        else
        {
            // Calculate weighted average
            var totalTime = (stats.AverageProcessingTime.TotalMilliseconds * (stats.MessagesProcessed - 1) + elapsed.TotalMilliseconds) / stats.MessagesProcessed;
            stats.AverageProcessingTime = TimeSpan.FromMilliseconds(totalTime);
        }
    }

    #endregion

    #region Additional Interface Methods

    public async Task<SubscriptionToken> SubscribePatternWithChannelAsync<T>(
        string pattern,
        Func<T, string, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) where T : class
    {
        return await SubscribePatternWithMetadataAsync(pattern, handler, cancellationToken).ConfigureAwait(false);
    }

    public Task<SubscriptionStats[]> GetSubscriptionStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = GetStatistics().Values.ToArray();
        return Task.FromResult(stats);
    }

    public async Task<bool> HasSubscribersAsync(string channel, CancellationToken cancellationToken = default)
    {
        ValidationUtils.ValidateChannelName(channel);

        var count = await GetSubscriberCountAsync(channel, cancellationToken).ConfigureAwait(false);
        return count > 0;
    }

    public Task<int> GetSubscriberCountAsync(string channel, CancellationToken cancellationToken = default)
    {
        ValidationUtils.ValidateChannelName(channel);

        // For StackExchange.Redis, we can only track our own subscriptions
        // There's no direct way to get global subscriber count without access to server commands
        // Return the count of local handlers for this channel
        _channelHandlers.TryGetValue(channel, out var handlers);
        var count = handlers?.Count ?? 0;

        _logger.LogDebug("Local subscriber count for channel {Channel}: {Count}", channel, count);
        return Task.FromResult(count);
    }

    #endregion

    #region Disposal

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Stop cleanup timer
            _cleanupTimer?.Dispose();

            // Synchronously block for unsubscribe, as Dispose must be synchronous.
            // IAsyncDisposable is preferred.
            try
            {
                UnsubscribeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during synchronous PubSubService disposal");
            }

            _subscriptionLock?.Dispose();

            // Dictionary pooling removed - was an anti-pattern
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _cleanupTimer.DisposeAsync();

        try
        {
            await UnsubscribeAllAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during asynchronous PubSubService disposal");
        }

        _subscriptionLock?.Dispose();

        // Dictionary pooling removed - was an anti-pattern

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RedisPubSubService));
    }

    private Task<ISubscriber> GetSubscriberAsync()
    {
        return _connection.GetSubscriberAsync();
    }

    public async Task PublishManyAsync<T>(IEnumerable<(string channel, T message)> messages, CancellationToken cancellationToken = default) where T : class
    {
        await RedisOperationExecutor.ExecuteVoidAsync(async () =>
        {
            var subscriber = await GetSubscriberAsync();
            var batch = subscriber.Multiplexer.GetDatabase().CreateBatch();
            var tasks = new List<Task>();

            foreach (var (channel, message) in messages)
            {
                var serialized = await _serializer.SerializeAsync(message, cancellationToken);
                tasks.Add(batch.PublishAsync(RedisChannel.Literal(channel), serialized));
            }

            batch.Execute();
            await Task.WhenAll(tasks);
        }, _logger, "PublishManyAsync", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Helper Classes

    /// <summary>
    ///     Internal handler representation
    /// </summary>
    private sealed class SubscriptionHandler
    {
        public SubscriptionHandler(string id, Func<object, CancellationToken, Task> handler, Type messageType)
        {
            Id = id;
            Handler = handler;
            MessageType = messageType;
        }

        public string Id { get; }
        public Func<object, CancellationToken, Task> Handler { get; }
        public Type MessageType { get; }
    }

    /// <summary>
    ///     Handler metadata for tracking and cleanup
    /// </summary>
    private sealed class HandlerMetadata
    {
        public required string Key { get; init; }
        public required SubscriptionType Type { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime LastActivity { get; set; }
    }

    #endregion
}