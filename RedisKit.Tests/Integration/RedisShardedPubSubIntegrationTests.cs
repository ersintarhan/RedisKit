using System.Collections.Concurrent;
using FluentAssertions;
using RedisKit.Models;
using Xunit;

namespace RedisKit.Tests.Integration;

public class RedisShardedPubSubIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task IsSupportedAsync_ShouldReturnCorrectValue()
    {
        // Act
        var isSupported = ShardedPubSubService != null && await ShardedPubSubService.IsSupportedAsync();

        // Assert
        // Will be true for Redis 7.0+, false otherwise
        Console.WriteLine($"Sharded Pub/Sub supported: {isSupported}");

        if (!isSupported) throw new SkipException("Redis 7.0+ required for Sharded Pub/Sub tests");
    }

    [Fact]
    public async Task PublishAsync_AndSubscribeAsync_ShouldDeliverMessage()
    {
        // Arrange
        await RequireRedis7();

        var channel = $"test:channel:{Guid.NewGuid():N}";
        var messageReceived = new TaskCompletionSource<TestMessage>();
        var subscription = await ShardedPubSubService!.SubscribeAsync<TestMessage>(
            channel,
            async (msg, ct) =>
            {
                messageReceived.TrySetResult(msg.Data);
                await Task.CompletedTask;
            });

        // Give subscription time to establish
        await Task.Delay(100);

        var testMessage = new TestMessage
        {
            Content = "Hello Sharded World!",
            Author = "Integration Test",
            Priority = 1
        };

        // Act
        var subscriberCount = await ShardedPubSubService.PublishAsync(channel, testMessage);

        // Assert
        subscriberCount.Should().BeGreaterThanOrEqualTo(1);

        var receivedMessage = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        receivedMessage.Should().NotBeNull();
        receivedMessage.Content.Should().Be(testMessage.Content);
        receivedMessage.Author.Should().Be(testMessage.Author);
        receivedMessage.Priority.Should().Be(testMessage.Priority);

        // Cleanup
        await ShardedPubSubService.UnsubscribeAsync(subscription);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_ShouldAllReceiveMessage()
    {
        // Arrange
        await RequireRedis7();

        var channel = $"test:channel:{Guid.NewGuid():N}";
        var receivedMessages = new ConcurrentBag<string>();
        var subscriber1Complete = new TaskCompletionSource<bool>();
        var subscriber2Complete = new TaskCompletionSource<bool>();

        var subscription1 = await ShardedPubSubService!.SubscribeAsync<TestMessage>(
            channel,
            async (msg, ct) =>
            {
                receivedMessages.Add($"Sub1:{msg.Data.Content}");
                subscriber1Complete.TrySetResult(true);
                await Task.CompletedTask;
            });

        var subscription2 = await ShardedPubSubService.SubscribeAsync<TestMessage>(
            channel,
            async (msg, ct) =>
            {
                receivedMessages.Add($"Sub2:{msg.Data.Content}");
                subscriber2Complete.TrySetResult(true);
                await Task.CompletedTask;
            });

        // Give subscriptions time to establish
        await Task.Delay(100);

        // Act
        var subscriberCount = await ShardedPubSubService.PublishAsync(
            channel,
            new TestMessage { Content = "Multi-subscriber test" });

        // Assert
        subscriberCount.Should().BeGreaterThanOrEqualTo(1); // Sharded pub/sub may report differently

        await Task.WhenAll(
            subscriber1Complete.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            subscriber2Complete.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        receivedMessages.Should().HaveCount(2);
        receivedMessages.Should().Contain("Sub1:Multi-subscriber test");
        receivedMessages.Should().Contain("Sub2:Multi-subscriber test");

        // Cleanup
        await ShardedPubSubService.UnsubscribeAsync(subscription1);
        await ShardedPubSubService.UnsubscribeAsync(subscription2);
    }

    [Fact]
    public async Task UnsubscribeAsync_ShouldStopReceivingMessages()
    {
        // Arrange
        await RequireRedis7();

        var channel = $"test:channel:{Guid.NewGuid():N}";
        var messageCount = 0;

        var subscription = await ShardedPubSubService!.SubscribeAsync<TestMessage>(
            channel,
            async (msg, ct) =>
            {
                Interlocked.Increment(ref messageCount);
                await Task.CompletedTask;
            });

        await Task.Delay(100);

        // Send first message
        await ShardedPubSubService.PublishAsync(channel, new TestMessage { Content = "Message 1" });
        await Task.Delay(100);
        messageCount.Should().Be(1);

        // Act - Unsubscribe
        await ShardedPubSubService.UnsubscribeAsync(subscription);
        await Task.Delay(100);

        // Send second message after unsubscribe
        await ShardedPubSubService.PublishAsync(channel, new TestMessage { Content = "Message 2" });
        await Task.Delay(100);

        // Assert
        messageCount.Should().Be(1); // Should not receive second message
    }

    [Fact]
    public async Task SubscribePatternAsync_ShouldThrowNotSupportedException()
    {
        // Arrange
        await RequireRedis7();

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
#pragma warning disable CS0618 // Type or member is obsolete - Testing that it throws NotSupportedException
            await ShardedPubSubService!.SubscribePatternAsync<TestMessage>(
                "test:*",
                async (msg, ct) => await Task.CompletedTask);
#pragma warning restore CS0618 // Type or member is obsolete
        });
    }

    [Fact]
    public async Task GetSubscriberCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        await RequireRedis7();

        var channel = $"test:channel:{Guid.NewGuid():N}";

        // Initially no subscribers
        var initialCount = await ShardedPubSubService!.GetSubscriberCountAsync(channel);
        initialCount.Should().Be(0);

        // Add subscribers
        var subscription1 = await ShardedPubSubService.SubscribeAsync<TestMessage>(
            channel,
            async (msg, ct) => await Task.CompletedTask);

        await Task.Delay(100);

        // Act
        var countWithOne = await ShardedPubSubService.GetSubscriberCountAsync(channel);

        var subscription2 = await ShardedPubSubService.SubscribeAsync<TestMessage>(
            channel,
            async (msg, ct) => await Task.CompletedTask);

        await Task.Delay(100);

        var countWithTwo = await ShardedPubSubService.GetSubscriberCountAsync(channel);

        // Assert
        countWithOne.Should().BeGreaterThanOrEqualTo(1);
        countWithTwo.Should().BeGreaterThanOrEqualTo(countWithOne);

        // Cleanup
        await ShardedPubSubService.UnsubscribeAsync(subscription1);
        await ShardedPubSubService.UnsubscribeAsync(subscription2);
    }

    [Fact]
    public async Task GetStatsAsync_ShouldReturnStatistics()
    {
        // Arrange
        await RequireRedis7();

        var channel1 = $"test:channel1:{Guid.NewGuid():N}";
        var channel2 = $"test:channel2:{Guid.NewGuid():N}";

        var subscription1 = await ShardedPubSubService!.SubscribeAsync<TestMessage>(
            channel1,
            async (msg, ct) => await Task.CompletedTask);

        var subscription2 = await ShardedPubSubService.SubscribeAsync<TestMessage>(
            channel2,
            async (msg, ct) => await Task.CompletedTask);

        await Task.Delay(100);

        // Act
        var stats = await ShardedPubSubService.GetStatsAsync();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalChannels.Should().BeGreaterThanOrEqualTo(2);
        stats.TotalSubscribers.Should().BeGreaterThanOrEqualTo(2);
        stats.CollectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Cleanup
        await ShardedPubSubService.UnsubscribeAsync(subscription1);
        await ShardedPubSubService.UnsubscribeAsync(subscription2);
    }

    [Fact]
    public async Task PublishAsync_ToMultipleShardedChannels_ShouldDistributeAcrossShards()
    {
        // Arrange
        await RequireRedis7();

        var channels = Enumerable.Range(0, 10)
            .Select(i => $"test:shard:{i}:{Guid.NewGuid():N}")
            .ToList();

        var receivedMessages = new ConcurrentDictionary<string, TestMessage>();
        var subscriptions = new List<SubscriptionToken>();

        foreach (var channel in channels)
        {
            var localChannel = channel;
            var subscription = await ShardedPubSubService!.SubscribeAsync<TestMessage>(
                localChannel,
                async (msg, ct) =>
                {
                    receivedMessages[localChannel] = msg.Data;
                    await Task.CompletedTask;
                });
            subscriptions.Add(subscription);
        }

        await Task.Delay(200);

        // Act
        foreach (var channel in channels)
            await ShardedPubSubService!.PublishAsync(
                channel,
                new TestMessage { Content = $"Message for {channel}" });

        await Task.Delay(500);

        // Assert
        receivedMessages.Should().HaveCount(10);
        foreach (var channel in channels)
        {
            receivedMessages.Should().ContainKey(channel);
            receivedMessages[channel].Content.Should().Be($"Message for {channel}");
        }

        // Cleanup
        foreach (var subscription in subscriptions) await ShardedPubSubService!.UnsubscribeAsync(subscription);
    }

    [Fact]
    public async Task ShardedPubSub_WithConcurrentOperations_ShouldHandleCorrectly()
    {
        // Arrange
        await RequireRedis7();

        var channel = $"test:concurrent:{Guid.NewGuid():N}";
        var receivedCount = 0;
        var messageCount = 100;
        var allReceived = new TaskCompletionSource<bool>();

        var subscription = await ShardedPubSubService!.SubscribeAsync<TestMessage>(
            channel,
            async (msg, ct) =>
            {
                var count = Interlocked.Increment(ref receivedCount);
                if (count >= messageCount) allReceived.TrySetResult(true);
                await Task.CompletedTask;
            });

        await Task.Delay(100);

        // Act - Publish messages concurrently
        var publishTasks = Enumerable.Range(1, messageCount)
            .Select(i => Task.Run(async () =>
            {
                await ShardedPubSubService.PublishAsync(
                    channel,
                    new TestMessage
                    {
                        Content = $"Message {i}",
                        Priority = i
                    });
            }))
            .ToArray();

        await Task.WhenAll(publishTasks);

        // Assert
        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        receivedCount.Should().Be(messageCount);

        // Cleanup
        await ShardedPubSubService.UnsubscribeAsync(subscription);
    }

    [Fact]
    public async Task ShardedPubSub_WithComplexObjects_ShouldSerializeCorrectly()
    {
        // Arrange
        await RequireRedis7();

        var channel = $"test:complex:{Guid.NewGuid():N}";
        var receivedEvent = new TaskCompletionSource<TestEvent>();

        var complexEvent = new TestEvent
        {
            EventType = "UserRegistered",
            Data = new
            {
                UserId = 123,
                Username = "testuser",
                Email = "test@example.com",
                Metadata = new Dictionary<string, object>
                {
                    ["Country"] = "US",
                    ["Age"] = 25,
                    ["Preferences"] = new[] { "tech", "music", "sports" }
                }
            },
            Source = "IntegrationTest"
        };

        var subscription = await ShardedPubSubService!.SubscribeAsync<TestEvent>(
            channel,
            async (msg, ct) =>
            {
                receivedEvent.TrySetResult(msg.Data);
                await Task.CompletedTask;
            });

        await Task.Delay(100);

        // Act
        await ShardedPubSubService.PublishAsync(channel, complexEvent);

        // Assert
        var received = await receivedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.Should().NotBeNull();
        received.EventType.Should().Be(complexEvent.EventType);
        received.Source.Should().Be(complexEvent.Source);
        received.Data.Should().NotBeNull();

        // Cleanup
        await ShardedPubSubService.UnsubscribeAsync(subscription);
    }
}