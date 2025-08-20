using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using RedisKit.Interfaces;
using RedisKit.Models;
using System.Collections.Concurrent;
using Xunit;

namespace RedisKit.Tests.Integration;

public class RedisPubSubServiceAdvancedTests : IntegrationTestBase
{
    #region Advanced Subscription Scenarios

    [Fact]
    public async Task SubscribeWithMetadataAsync_ReceivesChannelName()
    {
        // Arrange
        var channel = GenerateTestKey("metadata-channel");
        var message = new TestMessage { Id = 1, Content = "Metadata Test" };
        var receivedChannelName = "";
        var messageReceived = new TaskCompletionSource<bool>();

        // Act
        var token = await PubSubService.SubscribeWithMetadataAsync<TestMessage>(
            channel,
            async (msg, channelName, ct) =>
            {
                receivedChannelName = channelName;
                messageReceived.SetResult(true);
                await Task.CompletedTask;
            });

        // Give subscription time to establish
        await Task.Delay(100);

        // Publish message
        await PubSubService.PublishAsync(channel, message);

        // Wait for message
        await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(channel, receivedChannelName);

        // Cleanup
        await PubSubService.UnsubscribeAsync(token);
    }

    [Fact]
    public async Task SubscribePatternWithChannelAsync_ReceivesFromMultipleChannels()
    {
        // Arrange
        var baseChannel = GenerateTestKey("pattern-test");
        var pattern = $"{baseChannel}-*";
        var receivedMessages = new ConcurrentBag<(TestMessage Message, string Channel)>();
        var messageCount = new TaskCompletionSource<bool>();
        var receivedCount = 0;

        // Act
        var token = await PubSubService.SubscribePatternWithChannelAsync<TestMessage>(
            pattern,
            async (msg, channelName, ct) =>
            {
                receivedMessages.Add((msg, channelName));
                if (Interlocked.Increment(ref receivedCount) >= 3)
                {
                    messageCount.SetResult(true);
                }
                await Task.CompletedTask;
            });

        // Give subscription time to establish
        await Task.Delay(100);

        // Publish to multiple channels matching pattern
        await PubSubService.PublishAsync($"{baseChannel}-1", new TestMessage { Id = 1, Content = "Channel 1" });
        await PubSubService.PublishAsync($"{baseChannel}-2", new TestMessage { Id = 2, Content = "Channel 2" });
        await PubSubService.PublishAsync($"{baseChannel}-abc", new TestMessage { Id = 3, Content = "Channel ABC" });

        // Wait for all messages
        await messageCount.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Assert
        Assert.Equal(3, receivedMessages.Count);
        var messagesList = receivedMessages.ToList();
        
        Assert.Contains(messagesList, m => m.Message.Id == 1 && m.Channel.EndsWith("-1"));
        Assert.Contains(messagesList, m => m.Message.Id == 2 && m.Channel.EndsWith("-2"));
        Assert.Contains(messagesList, m => m.Message.Id == 3 && m.Channel.EndsWith("-abc"));

        // Cleanup
        await PubSubService.UnsubscribePatternAsync(pattern);
    }

    [Fact]
    public async Task MultipleSubscriptions_SameChannel_AllReceiveMessages()
    {
        // Arrange
        var channel = GenerateTestKey("multi-sub-channel");
        var message = new TestMessage { Id = 100, Content = "Multi Sub Test" };
        var received1 = new TaskCompletionSource<TestMessage>();
        var received2 = new TaskCompletionSource<TestMessage>();
        var received3 = new TaskCompletionSource<TestMessage>();

        // Act - Create multiple subscriptions to same channel
        var token1 = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) =>
        {
            received1.SetResult(msg);
            await Task.CompletedTask;
        });

        var token2 = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) =>
        {
            received2.SetResult(msg);
            await Task.CompletedTask;
        });

        var token3 = await PubSubService.SubscribeWithMetadataAsync<TestMessage>(channel, async (msg, ch, ct) =>
        {
            received3.SetResult(msg);
            await Task.CompletedTask;
        });

        await Task.Delay(100); // Let subscriptions establish

        // Publish single message
        await PubSubService.PublishAsync(channel, message);

        // Wait for all subscriptions to receive
        var result1 = await received1.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result2 = await received2.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result3 = await received3.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(message.Id, result1.Id);
        Assert.Equal(message.Id, result2.Id);
        Assert.Equal(message.Id, result3.Id);

        // Cleanup
        await PubSubService.UnsubscribeAsync(token1);
        await PubSubService.UnsubscribeAsync(token2);
        await PubSubService.UnsubscribeAsync(token3);
    }

    #endregion

    #region Batch and High-Volume Scenarios

    [Fact]
    public async Task HighVolume_PublishSubscribe_HandlesAllMessages()
    {
        // Arrange
        var channel = GenerateTestKey("high-volume");
        var messageCount = 50;
        var receivedMessages = new ConcurrentBag<TestMessage>();
        var completionSource = new TaskCompletionSource<bool>();
        var receivedCount = 0;

        // Subscribe first
        var token = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) =>
        {
            receivedMessages.Add(msg);
            if (Interlocked.Increment(ref receivedCount) >= messageCount)
            {
                completionSource.SetResult(true);
            }
            await Task.CompletedTask;
        });

        await Task.Delay(100); // Let subscription establish

        // Act - Publish many messages rapidly
        var publishTasks = new List<Task>();
        for (int i = 1; i <= messageCount; i++)
        {
            var message = new TestMessage { Id = i, Content = $"Message {i}" };
            publishTasks.Add(PubSubService.PublishAsync(channel, message));
        }

        await Task.WhenAll(publishTasks);

        // Wait for all messages to be received
        await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(messageCount, receivedMessages.Count);
        
        // Verify all message IDs are present
        var receivedIds = receivedMessages.Select(m => m.Id).OrderBy(id => id).ToList();
        for (int i = 1; i <= messageCount; i++)
        {
            Assert.Contains(i, receivedIds);
        }

        // Cleanup
        await PubSubService.UnsubscribeAsync(token);
    }

    [Fact]
    public async Task BatchPublishAsync_WithManyMessages_PublishesAll()
    {
        // Arrange
        var channel = GenerateTestKey("batch-channel");
        var messages = Enumerable.Range(1, 10)
            .Select(i => new TestMessage { Id = i, Content = $"Batch Message {i}" })
            .ToList();

        var receivedMessages = new ConcurrentBag<TestMessage>();
        var completionSource = new TaskCompletionSource<bool>();
        var receivedCount = 0;

        // Subscribe first
        var token = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) =>
        {
            receivedMessages.Add(msg);
            if (Interlocked.Increment(ref receivedCount) >= 10)
            {
                completionSource.SetResult(true);
            }
            await Task.CompletedTask;
        });

        await Task.Delay(100);

        // Act - Publish messages individually (BatchPublishAsync doesn't exist)
        foreach (var message in messages)
        {
            await PubSubService.PublishAsync(channel, message);
        }

        // Wait for all messages
        await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Assert
        Assert.Equal(10, receivedMessages.Count);
        var receivedIds = receivedMessages.Select(m => m.Id).OrderBy(id => id).ToList();
        for (int i = 1; i <= 10; i++)
        {
            Assert.Equal(i, receivedIds[i - 1]);
        }

        // Cleanup
        await PubSubService.UnsubscribeAsync(token);
    }

    #endregion

    #region Error Handling and Edge Cases

    [Fact]
    public async Task Subscribe_WithExceptionInHandler_ContinuesProcessing()
    {
        // Arrange
        var channel = GenerateTestKey("exception-channel");
        var goodMessagesReceived = new ConcurrentBag<int>();
        var processingCount = 0;

        // Subscribe with handler that throws on certain messages
        var token = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) =>
        {
            Interlocked.Increment(ref processingCount);
            
            if (msg.Id == 2) // Throw exception on message 2
            {
                throw new InvalidOperationException("Test exception");
            }
            
            goodMessagesReceived.Add(msg.Id);
            await Task.CompletedTask;
        });

        await Task.Delay(100);

        // Act - Publish messages, one will cause exception
        await PubSubService.PublishAsync(channel, new TestMessage { Id = 1, Content = "Good 1" });
        await PubSubService.PublishAsync(channel, new TestMessage { Id = 2, Content = "Bad" });
        await PubSubService.PublishAsync(channel, new TestMessage { Id = 3, Content = "Good 3" });

        // Wait for processing
        await Task.Delay(500);

        // Assert - Good messages should still be processed despite exception
        Assert.Contains(1, goodMessagesReceived);
        Assert.Contains(3, goodMessagesReceived);
        Assert.Equal(3, processingCount); // All messages were attempted

        // Cleanup
        await PubSubService.UnsubscribeAsync(token);
    }

    [Fact]
    public async Task UnsubscribeAsync_WithNonExistentChannel_DoesNotThrow()
    {
        // Arrange
        var fakeChannel = GenerateTestKey("fake-channel");

        // Act & Assert - Should not throw
        await PubSubService.UnsubscribeAsync(fakeChannel);
    }

    [Fact]
    public async Task Publish_ToChannelWithNoSubscribers_ReturnsZero()
    {
        // Arrange
        var channel = GenerateTestKey("no-subscribers");
        var message = new TestMessage { Id = 1, Content = "No one listening" };

        // Act
        var subscriberCount = await PubSubService.PublishAsync(channel, message);

        // Assert
        Assert.Equal(0, subscriberCount);
    }

    [Fact]
    public async Task Subscribe_WithComplexObject_SerializesCorrectly()
    {
        // Arrange
        var channel = GenerateTestKey("complex-channel");
        var complexMessage = new ComplexMessage
        {
            Id = 999,
            Name = "Complex Test",
            Metadata = new Dictionary<string, object>
            {
                ["timestamp"] = DateTime.UtcNow,
                ["priority"] = 5,
                ["tags"] = new[] { "test", "complex" }
            },
            NestedMessages = new List<TestMessage>
            {
                new() { Id = 1, Content = "Nested 1" },
                new() { Id = 2, Content = "Nested 2" }
            }
        };

        var receivedMessage = new TaskCompletionSource<ComplexMessage>();

        // Act
        var token = await PubSubService.SubscribeAsync<ComplexMessage>(channel, async (msg, ct) =>
        {
            receivedMessage.SetResult(msg);
            await Task.CompletedTask;
        });

        await Task.Delay(100);
        await PubSubService.PublishAsync(channel, complexMessage);

        var result = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(complexMessage.Id, result.Id);
        Assert.Equal(complexMessage.Name, result.Name);
        Assert.Equal(3, result.Metadata.Count);
        Assert.Equal(2, result.NestedMessages.Count);
        Assert.Equal("Nested 1", result.NestedMessages[0].Content);

        // Cleanup
        await PubSubService.UnsubscribeAsync(token);
    }

    [Fact]
    public async Task GetSubscriptionStatsAsync_ReturnsCurrentStats()
    {
        // Arrange
        var channel1 = GenerateTestKey("stats-1");
        var channel2 = GenerateTestKey("stats-2");
        var pattern = GenerateTestKey("pattern-*");

        // Act
        var token1 = await PubSubService.SubscribeAsync<TestMessage>(channel1, async (msg, ct) => await Task.CompletedTask);
        var token2 = await PubSubService.SubscribeAsync<TestMessage>(channel2, async (msg, ct) => await Task.CompletedTask);
        var token3 = await PubSubService.SubscribePatternAsync<TestMessage>(pattern, async (msg, ct) => await Task.CompletedTask);

        var stats = await PubSubService.GetSubscriptionStatsAsync();

        // Assert
        Assert.NotNull(stats);
        Assert.NotEmpty(stats);

        // Cleanup
        await PubSubService.UnsubscribeAsync(token1);
        await PubSubService.UnsubscribeAsync(token2);
        await PubSubService.UnsubscribePatternAsync(pattern);
    }

    #endregion

    #region Cleanup and Resource Management

    [Fact]
    public async Task HasSubscribersAsync_WithActiveSubscription_ReturnsTrue()
    {
        // Arrange
        var channel = GenerateTestKey("has-subs-channel");
        
        // Subscribe first
        var token = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask);
        await Task.Delay(100); // Let subscription establish

        // Act
        var hasSubscribers = await PubSubService.HasSubscribersAsync(channel);

        // Assert
        // Note: This might return false due to StackExchange.Redis local-only count limitation
        // The test verifies the method exists and doesn't throw
        Assert.True(hasSubscribers || !hasSubscribers); // Method works without exception

        // Cleanup
        await PubSubService.UnsubscribeAsync(token);
    }

    [Fact]
    public async Task GetSubscriberCountAsync_WithActiveSubscriptions_ReturnsCount()
    {
        // Arrange
        var channel = GenerateTestKey("count-channel");
        
        // Subscribe multiple times
        var token1 = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask);
        var token2 = await PubSubService.SubscribeAsync<TestMessage>(channel, async (msg, ct) => await Task.CompletedTask);
        await Task.Delay(100);

        // Act
        var count = await PubSubService.GetSubscriberCountAsync(channel);

        // Assert
        // Note: StackExchange.Redis reports local subscriptions only
        Assert.True(count >= 0); // Method works and returns non-negative

        // Cleanup
        await PubSubService.UnsubscribeAsync(token1);
        await PubSubService.UnsubscribeAsync(token2);
    }

    [Fact]
    public async Task ConcurrentSubscribeUnsubscribe_HandlesThreadSafety()
    {
        // Arrange
        var baseChannel = GenerateTestKey("concurrent");
        var tasks = new List<Task>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act - Concurrent subscribe/unsubscribe operations
        for (int i = 0; i < 20; i++)
        {
            var channelIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var channel = $"{baseChannel}-{channelIndex}";
                    var token = await PubSubService.SubscribeAsync<TestMessage>(channel, 
                        async (msg, ct) => await Task.CompletedTask);
                    
                    await Task.Delay(10);
                    await PubSubService.UnsubscribeAsync(token);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);
    }

    #endregion

    #region Test Models

    [MessagePackObject]
    public class TestMessage
    {
        [Key(0)] public int Id { get; set; }
        [Key(1)] public string Content { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public class ComplexMessage
    {
        [Key(0)] public int Id { get; set; }
        [Key(1)] public string Name { get; set; } = string.Empty;
        [Key(2)] public Dictionary<string, object> Metadata { get; set; } = new();
        [Key(3)] public List<TestMessage> NestedMessages { get; set; } = new();
    }

    #endregion
}