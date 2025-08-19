using FluentAssertions;
using RedisKit.Models;
using RedisKit.Utilities;
using Xunit;

namespace RedisKit.Tests;

public class BackoffCalculatorTests
{
    [Fact]
    public void CalculateDelay_WithFixedStrategy_ReturnsConstantDelay()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Fixed,
            InitialDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(10),
            EnableJitter = false // Disable jitter for predictable test
        };

        // Act & Assert
        for (var i = 0; i < 5; i++)
        {
            var delay = BackoffCalculator.CalculateDelay(i, config);
            delay.Should().Be(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void CalculateDelay_WithLinearStrategy_ReturnsLinearlyIncreasingDelay()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Linear,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            EnableJitter = false // Disable jitter for predictable test
        };

        // Act & Assert
        BackoffCalculator.CalculateDelay(0, config).Should().Be(TimeSpan.FromSeconds(1));
        BackoffCalculator.CalculateDelay(1, config).Should().Be(TimeSpan.FromSeconds(2));
        BackoffCalculator.CalculateDelay(2, config).Should().Be(TimeSpan.FromSeconds(3));
        BackoffCalculator.CalculateDelay(3, config).Should().Be(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void CalculateDelay_WithExponentialStrategy_ReturnsExponentiallyIncreasingDelay()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Exponential,
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(100),
            EnableJitter = false // Disable jitter for predictable test
        };

        // Act & Assert
        BackoffCalculator.CalculateDelay(0, config).Should().Be(TimeSpan.FromSeconds(1));
        BackoffCalculator.CalculateDelay(1, config).Should().Be(TimeSpan.FromSeconds(2));
        BackoffCalculator.CalculateDelay(2, config).Should().Be(TimeSpan.FromSeconds(4));
        BackoffCalculator.CalculateDelay(3, config).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void CalculateDelay_WithExponentialWithJitterStrategy_ReturnsDelayWithJitter()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.ExponentialWithJitter,
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0,
            JitterFactor = 0.5,
            MaxDelay = TimeSpan.FromSeconds(100)
        };

        // Act
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 10; i++) delays.Add(BackoffCalculator.CalculateDelay(2, config)); // Same attempt number

        // Assert - with jitter, delays should vary
        delays.Distinct().Count().Should().BeGreaterThan(1);

        // Base delay for attempt 2 is 4 seconds
        // With 50% jitter, it should be between roughly 2 and 6 seconds
        delays.All(d => d.TotalSeconds >= 2 && d.TotalSeconds <= 6).Should().BeTrue();
    }

    [Fact]
    public void CalculateDelay_WithEnableJitterTrue_AddsJitterToNonJitterStrategies()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Linear,
            InitialDelay = TimeSpan.FromSeconds(2),
            EnableJitter = true,
            JitterFactor = 0.2,
            MaxDelay = TimeSpan.FromSeconds(30)
        };

        // Act
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 10; i++) delays.Add(BackoffCalculator.CalculateDelay(1, config)); // Same attempt number

        // Assert - with jitter enabled, delays should vary
        delays.Distinct().Count().Should().BeGreaterThan(1);

        // Base delay for attempt 1 with linear is 4 seconds
        // With 20% jitter, it should be between roughly 3.6 and 4.4 seconds
        delays.All(d => d.TotalSeconds >= 3.6 && d.TotalSeconds <= 4.4).Should().BeTrue();
    }

    [Fact]
    public void CalculateDelay_WithEnableJitterFalse_DoesNotAddJitter()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Linear,
            InitialDelay = TimeSpan.FromSeconds(2),
            EnableJitter = false,
            JitterFactor = 0.2,
            MaxDelay = TimeSpan.FromSeconds(30)
        };

        // Act
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 10; i++) delays.Add(BackoffCalculator.CalculateDelay(1, config));

        // Assert - without jitter, all delays should be identical
        delays.Distinct().Count().Should().Be(1);
        delays.All(d => d == TimeSpan.FromSeconds(4)).Should().BeTrue();
    }

    [Fact]
    public void CalculateDelay_WithDecorrelatedJitterStrategy_ReturnsRandomizedDelay()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.DecorrelatedJitter,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30)
        };

        // Act
        var delay1 = BackoffCalculator.CalculateDelay(0, config);
        var delay2 = BackoffCalculator.CalculateDelay(1, config, delay1);
        var delay3 = BackoffCalculator.CalculateDelay(2, config, delay2);

        // Assert
        delay1.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        delay2.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        delay3.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));

        // All should be within max delay
        delay1.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
        delay2.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
        delay3.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CalculateDelay_ExceedsMaxDelay_ReturnsMaxDelay()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Exponential,
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(5),
            EnableJitter = false // Disable jitter for predictable test
        };

        // Act
        var delay = BackoffCalculator.CalculateDelay(10, config); // Would be 1024 seconds without max

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CalculateDelay_WithNegativeAttempt_ThrowsException()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Fixed,
            InitialDelay = TimeSpan.FromSeconds(1),
            EnableJitter = false
        };

        // Act & Assert
        var act = () => BackoffCalculator.CalculateDelay(-1, config);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("attempt");
    }

    [Fact]
    public void CalculateDelay_WithNullConfig_ThrowsException()
    {
        // Act & Assert
        var act = () => BackoffCalculator.CalculateDelay(0, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("config");
    }

    [Fact]
    public void CalculateTotalMaxDelay_WithFixedStrategy_ReturnsCorrectTotal()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Fixed,
            InitialDelay = TimeSpan.FromSeconds(2),
            MaxAttempts = 3,
            MaxDelay = TimeSpan.FromSeconds(10),
            EnableJitter = false // Disable jitter for predictable test
        };

        // Act
        var totalDelay = BackoffCalculator.CalculateTotalMaxDelay(config);

        // Assert
        totalDelay.Should().Be(TimeSpan.FromSeconds(6)); // 2 + 2 + 2
    }

    [Fact]
    public void CalculateTotalMaxDelay_WithExponentialStrategy_ReturnsCorrectTotal()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            Strategy = BackoffStrategy.Exponential,
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0,
            MaxAttempts = 4,
            MaxDelay = TimeSpan.FromSeconds(100),
            EnableJitter = false // Disable jitter for predictable test
        };

        // Act
        var totalDelay = BackoffCalculator.CalculateTotalMaxDelay(config);

        // Assert
        totalDelay.Should().Be(TimeSpan.FromSeconds(15)); // 1 + 2 + 4 + 8
    }

    [Fact]
    public void CalculateTotalMaxDelay_WithNullConfig_ThrowsException()
    {
        // Act & Assert
        var act = () => BackoffCalculator.CalculateTotalMaxDelay(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("config");
    }
}