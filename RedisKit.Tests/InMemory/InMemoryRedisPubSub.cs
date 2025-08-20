using System.Collections.Concurrent;
using RedisKit.Interfaces;
using RedisKit.Models;

namespace RedisKit.Tests.InMemory;

// Simple message wrapper for testing

// Simple stats class for testing

/// <summary>
///     In-memory implementation of IRedisPubSubService for testing
/// </summary>
public class InMemoryRedisPubSub : IRedisPubSubService
{
    private readonly ConcurrentDictionary<string, List<Func<object, Task>>> _patternSubscriptions = new();
    private readonly ConcurrentDictionary<string, List<Func<object, string, Task>>> _patternWithChannelSubscriptions = new();
    private readonly ConcurrentDictionary<string, List<Func<object, Task>>> _subscriptions = new();
    private int _subscriptionIdCounter;

    public Task<long> PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(message);

        long count = 0;

        // Direct channel subscriptions
        if (_subscriptions.TryGetValue(channel, out var handlers))
            foreach (var handler in handlers)
            {
                _ = Task.Run(() => handler(message));
                count++;
            }

        // Pattern subscriptions (without channel)
        foreach (var pattern in _patternSubscriptions.Keys)
            if (MatchesPattern(channel, pattern))
                if (_patternSubscriptions.TryGetValue(pattern, out var patternHandlers))
                    foreach (var handler in patternHandlers)
                    {
                        _ = Task.Run(() => handler(message));
                        count++;
                    }

        // Pattern subscriptions (with channel)
        foreach (var pattern in _patternWithChannelSubscriptions.Keys)
            if (MatchesPattern(channel, pattern))
                if (_patternWithChannelSubscriptions.TryGetValue(pattern, out var patternHandlers))
                    foreach (var handler in patternHandlers)
                    {
                        _ = Task.Run(() => handler(message, channel));
                        count++;
                    }

        return Task.FromResult(count);
    }

    public async Task PublishManyAsync<T>(IEnumerable<(string channel, T message)> messages, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var (channel, message) in messages) await PublishAsync(channel, message, cancellationToken);
    }

    public Task<SubscriptionToken> SubscribeWithMetadataAsync<T>(string channel, Func<T, string, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(handler);

        var wrappedHandler = new Func<object, Task>(async msg =>
        {
            if (msg is T typedMsg) await handler(typedMsg, channel, CancellationToken.None);
        });

        _subscriptions.AddOrUpdate(channel,
            new List<Func<object, Task>> { wrappedHandler },
            (_, list) =>
            {
                list.Add(wrappedHandler);
                return list;
            });

        var token = new SubscriptionToken(
            Interlocked.Increment(ref _subscriptionIdCounter).ToString(),
            channel,
            SubscriptionType.Channel);

        return Task.FromResult(token);
    }

    // Additional overload for simple subscription
    public Task<SubscriptionToken> SubscribeAsync<T>(string channel, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(handler);

        var wrappedHandler = new Func<object, Task>(async msg =>
        {
            if (msg is T typedMsg) await handler(typedMsg, CancellationToken.None);
        });

        _subscriptions.AddOrUpdate(channel,
            new List<Func<object, Task>> { wrappedHandler },
            (_, list) =>
            {
                list.Add(wrappedHandler);
                return list;
            });

        var token = new SubscriptionToken(
            Interlocked.Increment(ref _subscriptionIdCounter).ToString(),
            channel,
            SubscriptionType.Channel);

        return Task.FromResult(token);
    }

    public Task<SubscriptionToken> SubscribePatternAsync<T>(string pattern, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(handler);

        var wrappedHandler = new Func<object, Task>(async msg =>
        {
            if (msg is T typedMsg) await handler(typedMsg, CancellationToken.None);
        });

        _patternSubscriptions.AddOrUpdate(pattern,
            new List<Func<object, Task>> { wrappedHandler },
            (_, list) =>
            {
                list.Add(wrappedHandler);
                return list;
            });

        var token = new SubscriptionToken(
            Interlocked.Increment(ref _subscriptionIdCounter).ToString(),
            pattern,
            SubscriptionType.Pattern);

        return Task.FromResult(token);
    }

    public Task<SubscriptionToken> SubscribePatternWithChannelAsync<T>(string pattern, Func<T, string, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(handler);

        var wrappedHandler = new Func<object, string, Task>(async (msg, ch) =>
        {
            if (msg is T typedMsg) await handler(typedMsg, ch, CancellationToken.None);
        });

        _patternWithChannelSubscriptions.AddOrUpdate(pattern,
            new List<Func<object, string, Task>> { wrappedHandler },
            (_, list) =>
            {
                list.Add(wrappedHandler);
                return list;
            });

        var token = new SubscriptionToken(
            Interlocked.Increment(ref _subscriptionIdCounter).ToString(),
            pattern,
            SubscriptionType.Pattern);

        return Task.FromResult(token);
    }

    public Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default)
    {
        _subscriptions.TryRemove(channel, out _);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(SubscriptionToken token, CancellationToken cancellationToken = default)
    {
        if (token.Type == SubscriptionType.Channel)
            _subscriptions.TryRemove(token.ChannelOrPattern, out _);
        else
            _patternSubscriptions.TryRemove(token.ChannelOrPattern, out _);
        return Task.CompletedTask;
    }

    public Task UnsubscribePatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        _patternSubscriptions.TryRemove(pattern, out _);
        return Task.CompletedTask;
    }

    public Task<int> GetSubscriberCountAsync(string channel, CancellationToken cancellationToken = default)
    {
        var count = 0;
        if (_subscriptions.TryGetValue(channel, out var handlers)) count = handlers.Count;
        return Task.FromResult(count);
    }

    public Task<bool> HasSubscribersAsync(string channel, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_subscriptions.ContainsKey(channel) && _subscriptions[channel].Count > 0);
    }

    public Task<SubscriptionStats[]> GetSubscriptionStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new List<SubscriptionStats>();
        foreach (var sub in _subscriptions)
            stats.Add(new SubscriptionStats
            {
                ChannelOrPattern = sub.Key,
                Type = SubscriptionType.Channel,
                HandlerCount = sub.Value.Count,
                MessagesReceived = 0,
                MessagesProcessed = 0,
                MessagesFailed = 0
            });
        foreach (var sub in _patternSubscriptions)
            stats.Add(new SubscriptionStats
            {
                ChannelOrPattern = sub.Key,
                Type = SubscriptionType.Pattern,
                HandlerCount = sub.Value.Count,
                MessagesReceived = 0,
                MessagesProcessed = 0,
                MessagesFailed = 0
            });
        return Task.FromResult(stats.ToArray());
    }

    public Task<SubscriptionToken> SubscribeAsync<T>(string channel, Func<ChannelMessage<T>, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(handler);

        var wrappedHandler = new Func<object, Task>(async msg =>
        {
            if (msg is T typedMsg) await handler(new ChannelMessage<T> { Channel = channel, Data = typedMsg }, CancellationToken.None);
        });

        _subscriptions.AddOrUpdate(channel,
            new List<Func<object, Task>> { wrappedHandler },
            (_, list) =>
            {
                list.Add(wrappedHandler);
                return list;
            });

        var token = new SubscriptionToken(
            Interlocked.Increment(ref _subscriptionIdCounter).ToString(),
            channel,
            SubscriptionType.Channel);

        return Task.FromResult(token);
    }

    [Obsolete("Use SubscribeAsync with pattern parameter instead")]
    public Task<SubscriptionToken> SubscribePatternAsync<T>(string pattern, Func<ChannelMessage<T>, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(handler);

        var wrappedHandler = new Func<object, Task>(async msg =>
        {
            if (msg is ChannelMessage<T> typedMsg) await handler(typedMsg, CancellationToken.None);
        });

        _patternSubscriptions.AddOrUpdate(pattern,
            new List<Func<object, Task>> { wrappedHandler },
            (_, list) =>
            {
                list.Add(wrappedHandler);
                return list;
            });

        var token = new SubscriptionToken(
            Interlocked.Increment(ref _subscriptionIdCounter).ToString(),
            pattern,
            SubscriptionType.Pattern);

        return Task.FromResult(token);
    }

    [Obsolete("Use UnsubscribeAsync with pattern parameter instead")]
    public Task UnsubscribePatternAsync(string pattern)
    {
        _patternSubscriptions.TryRemove(pattern, out _);
        return Task.CompletedTask;
    }

    public Task<PubSubStats> GetStatsAsync()
    {
        var stats = new PubSubStats
        {
            TotalChannels = _subscriptions.Count + _patternSubscriptions.Count,
            TotalSubscribers = _subscriptions.Values.Sum(v => v.Count) + _patternSubscriptions.Values.Sum(v => v.Count),
            CollectedAt = DateTime.UtcNow
        };
        return Task.FromResult(stats);
    }

    public void Dispose()
    {
        _subscriptions.Clear();
        _patternSubscriptions.Clear();
    }

    private static bool MatchesPattern(string channel, string pattern)
    {
        // Simple glob pattern matching
        if (pattern.Contains('*'))
        {
            var parts = pattern.Split('*');
            if (parts.Length == 2) return channel.StartsWith(parts[0]) && channel.EndsWith(parts[1]);
        }

        return channel == pattern;
    }

    public void Clear()
    {
        _subscriptions.Clear();
        _patternSubscriptions.Clear();
        _patternWithChannelSubscriptions.Clear();
    }
}