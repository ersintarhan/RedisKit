using FluentAssertions;
using RedisKit.Models;
using RedisKit.Tests.InMemory;
using Xunit;

namespace RedisKit.Tests.InMemoryBasedTests;

public class PubSubServiceBusinessLogicTests
{
    private readonly InMemoryRedisPubSub _pubSub;

    public PubSubServiceBusinessLogicTests()
    {
        _pubSub = new InMemoryRedisPubSub();
    }

    [Fact]
    public async Task PublishAsync_Should_Deliver_Message_To_Subscribers()
    {
        // Arrange
        var channel = "test-channel";
        var receivedMessage = "";
        var messageReceived = new TaskCompletionSource<bool>();

        await _pubSub.SubscribeAsync<string>(channel, async (msg, ct) =>
        {
            receivedMessage = msg;
            messageReceived.SetResult(true);
            await Task.CompletedTask;
        });

        // Act
        var count = await _pubSub.PublishAsync(channel, "Hello World");
        await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        count.Should().Be(1);
        receivedMessage.Should().Be("Hello World");
    }

    [Fact]
    public async Task PublishAsync_Should_Return_Zero_When_No_Subscribers()
    {
        // Act
        var count = await _pubSub.PublishAsync("empty-channel", "Message");

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task SubscribeAsync_Should_Return_SubscriptionToken()
    {
        // Act
        var token = await _pubSub.SubscribeAsync("channel", async (string msg, CancellationToken ct) => await Task.CompletedTask);

        // Assert
        token.Should().NotBeNull();
        token.ChannelOrPattern.Should().Be("channel");
        token.Type.Should().Be(SubscriptionType.Channel);
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Remove_Subscription()
    {
        // Arrange
        var channel = "unsub-channel";
        var token = await _pubSub.SubscribeAsync(channel, async (string msg, CancellationToken ct) => await Task.CompletedTask);

        // Act
        await _pubSub.UnsubscribeAsync(token);
        var count = await _pubSub.PublishAsync(channel, "Message");

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task SubscribePatternAsync_Should_Match_Patterns()
    {
        // Arrange
        var receivedMessages = new List<string>();
        var pattern = "test-*";
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();
        var messageCount = 0;

        await _pubSub.SubscribePatternAsync<string>(pattern, async (msg, ct) =>
        {
            receivedMessages.Add(msg);
            messageCount++;
            if (messageCount == 1) tcs1.SetResult(true);
            if (messageCount == 2) tcs2.SetResult(true);
            await Task.CompletedTask;
        });

        // Act
        var count1 = await _pubSub.PublishAsync("test-1", "Message1");
        var count2 = await _pubSub.PublishAsync("test-2", "Message2");
        var count3 = await _pubSub.PublishAsync("other-channel", "Message3");

        // Wait for messages to be processed
        await Task.WhenAll(tcs1.Task, tcs2.Task).WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        count1.Should().Be(1, "test-1 should match pattern test-*");
        count2.Should().Be(1, "test-2 should match pattern test-*");
        count3.Should().Be(0, "other-channel should not match pattern test-*");
        receivedMessages.Should().HaveCount(2);
        receivedMessages.Should().Contain("Message1");
        receivedMessages.Should().Contain("Message2");
    }

    [Fact]
    public async Task GetSubscriberCountAsync_Should_Return_Count()
    {
        // Arrange
        var channel = "count-channel";
        await _pubSub.SubscribeAsync(channel, async (string msg, CancellationToken ct) => await Task.CompletedTask);
        await _pubSub.SubscribeAsync(channel, async (string msg, CancellationToken ct) => await Task.CompletedTask);

        // Act
        var count = await _pubSub.GetSubscriberCountAsync(channel);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task HasSubscribersAsync_Should_Return_True_When_Has_Subscribers()
    {
        // Arrange
        var channel = "has-subs";
        await _pubSub.SubscribeAsync(channel, async (string msg, CancellationToken ct) => await Task.CompletedTask);

        // Act
        var hasSubs = await _pubSub.HasSubscribersAsync(channel);

        // Assert
        hasSubs.Should().BeTrue();
    }

    [Fact]
    public async Task HasSubscribersAsync_Should_Return_False_When_No_Subscribers()
    {
        // Act
        var hasSubs = await _pubSub.HasSubscribersAsync("no-subs");

        // Assert
        hasSubs.Should().BeFalse();
    }

    [Fact]
    public async Task GetSubscriptionStatsAsync_Should_Return_Stats()
    {
        // Arrange
        await _pubSub.SubscribeAsync("channel1", async (string msg, CancellationToken ct) => await Task.CompletedTask);
        await _pubSub.SubscribeAsync("channel2", async (string msg, CancellationToken ct) => await Task.CompletedTask);
        await _pubSub.SubscribePatternAsync("pattern*", async (string msg, CancellationToken ct) => await Task.CompletedTask);

        // Act
        var stats = await _pubSub.GetSubscriptionStatsAsync();

        // Assert
        stats.Should().HaveCount(3);
        stats.Should().Contain(s => s.ChannelOrPattern == "channel1" && s.Type == SubscriptionType.Channel);
        stats.Should().Contain(s => s.ChannelOrPattern == "channel2" && s.Type == SubscriptionType.Channel);
        stats.Should().Contain(s => s.ChannelOrPattern == "pattern*" && s.Type == SubscriptionType.Pattern);
    }

    [Fact]
    public async Task PublishManyAsync_Should_Publish_Multiple_Messages()
    {
        // Arrange
        var receivedMessages = new Dictionary<string, string>();
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        await _pubSub.SubscribeAsync<string>("channel1", async (msg, ct) =>
        {
            receivedMessages["channel1"] = msg;
            tcs1.SetResult(true);
            await Task.CompletedTask;
        });

        await _pubSub.SubscribeAsync<string>("channel2", async (msg, ct) =>
        {
            receivedMessages["channel2"] = msg;
            tcs2.SetResult(true);
            await Task.CompletedTask;
        });

        var messages = new[]
        {
            ("channel1", "Message1"),
            ("channel2", "Message2")
        };

        // Act
        await _pubSub.PublishManyAsync(messages);
        await Task.WhenAll(tcs1.Task, tcs2.Task).WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        receivedMessages.Should().HaveCount(2);
        receivedMessages["channel1"].Should().Be("Message1");
        receivedMessages["channel2"].Should().Be("Message2");
    }

    [Fact]
    public async Task SubscribeWithMetadataAsync_Should_Include_Channel_Name()
    {
        // Arrange
        var channel = "metadata-channel";
        string? receivedChannel = null;
        string? receivedMessage = null;
        var tcs = new TaskCompletionSource<bool>();

        await _pubSub.SubscribeWithMetadataAsync<string>(channel, async (msg, ch, ct) =>
        {
            receivedMessage = msg;
            receivedChannel = ch;
            tcs.SetResult(true);
            await Task.CompletedTask;
        });

        // Act
        await _pubSub.PublishAsync(channel, "Test Message");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        receivedChannel.Should().Be(channel);
        receivedMessage.Should().Be("Test Message");
    }

    [Fact]
    public async Task SubscribePatternWithChannelAsync_Should_Include_Channel_Name()
    {
        // Arrange
        var pattern = "test-*";
        string? receivedChannel = null;
        string? receivedMessage = null;
        var tcs = new TaskCompletionSource<bool>();

        await _pubSub.SubscribePatternWithChannelAsync<string>(pattern, async (msg, ch, ct) =>
        {
            receivedMessage = msg;
            receivedChannel = ch;
            tcs.SetResult(true);
            await Task.CompletedTask;
        });

        // Act
        await _pubSub.PublishAsync("test-123", "Pattern Message");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        receivedChannel.Should().Be("test-123");
        receivedMessage.Should().Be("Pattern Message");
    }

    [Fact]
    public async Task Clear_Should_Remove_All_Subscriptions()
    {
        // Arrange
        await _pubSub.SubscribeAsync("channel1", async (string msg, CancellationToken ct) => await Task.CompletedTask);
        await _pubSub.SubscribeAsync("channel2", async (string msg, CancellationToken ct) => await Task.CompletedTask);

        // Act
        _pubSub.Clear();
        var count1 = await _pubSub.PublishAsync("channel1", "Message");
        var count2 = await _pubSub.PublishAsync("channel2", "Message");

        // Assert
        count1.Should().Be(0);
        count2.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_Should_Clear_Subscriptions()
    {
        // Arrange
        await _pubSub.SubscribeAsync("channel", async (string msg, CancellationToken ct) => await Task.CompletedTask);

        // Act
        _pubSub.Dispose();
        var count = await _pubSub.PublishAsync("channel", "Message");

        // Assert
        count.Should().Be(0);
    }
}