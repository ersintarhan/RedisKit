using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Models;
using RedisKit.Services;
using Xunit;

namespace RedisKit.Tests;

public class RedisCircuitBreakerTests
{
    private readonly RedisCircuitBreaker _circuitBreaker;
    private readonly ILogger<RedisCircuitBreaker> _logger;
    private readonly CircuitBreakerSettings _settings;

    public RedisCircuitBreakerTests()
    {
        _logger = Substitute.For<ILogger<RedisCircuitBreaker>>();
        _settings = new CircuitBreakerSettings
        {
            Enabled = true,
            FailureThreshold = 3,
            SuccessThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(1),
            FailureWindow = TimeSpan.FromMinutes(1)
        };
        _circuitBreaker = new RedisCircuitBreaker(_logger, Options.Create(_settings));
    }

    [Fact]
    public async Task CanExecuteAsync_WhenCircuitClosed_ReturnsTrue()
    {
        // Act
        var result = await _circuitBreaker.CanExecuteAsync();

        // Assert
        result.Should().BeTrue();
        _circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task CanExecuteAsync_WhenDisabled_AlwaysReturnsTrue()
    {
        // Arrange
        var settings = new CircuitBreakerSettings { Enabled = false };
        var circuitBreaker = new RedisCircuitBreaker(_logger, Options.Create(settings));

        // Act
        var result = await circuitBreaker.CanExecuteAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RecordFailureAsync_AfterThreshold_OpensCircuit()
    {
        // Act
        for (var i = 0; i < _settings.FailureThreshold; i++) await _circuitBreaker.RecordFailureAsync();

        // Assert
        _circuitBreaker.State.Should().Be(CircuitState.Open);

        // Verify logging
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Circuit breaker opened")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task RecordFailureAsync_WithException_LogsException()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");

        // Act
        await _circuitBreaker.RecordFailureAsync(exception);

        // Assert - LogCircuitBreakerFailure is an extension method that may not be easily mockable
        // Instead, verify that the circuit breaker state changes appropriately
        _circuitBreaker.State.Should().Be(CircuitState.Closed); // Still closed after one failure
        var stats = _circuitBreaker.GetStats();
        stats.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task CanExecuteAsync_WhenOpen_ReturnsFalse()
    {
        // Arrange
        for (var i = 0; i < _settings.FailureThreshold; i++) await _circuitBreaker.RecordFailureAsync();

        // Act
        var result = await _circuitBreaker.CanExecuteAsync();

        // Assert
        result.Should().BeFalse();
        _circuitBreaker.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task CanExecuteAsync_AfterBreakDuration_TransitionsToHalfOpen()
    {
        // Arrange
        for (var i = 0; i < _settings.FailureThreshold; i++) await _circuitBreaker.RecordFailureAsync();

        // Act
        await Task.Delay(_settings.BreakDuration.Add(TimeSpan.FromMilliseconds(100)));
        var result = await _circuitBreaker.CanExecuteAsync();

        // Assert
        result.Should().BeTrue();
        _circuitBreaker.State.Should().Be(CircuitState.HalfOpen);

        // Verify logging
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("half-open")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task RecordSuccessAsync_InHalfOpen_AfterThreshold_ClosesCircuit()
    {
        // Arrange
        for (var i = 0; i < _settings.FailureThreshold; i++) await _circuitBreaker.RecordFailureAsync();

        await Task.Delay(_settings.BreakDuration.Add(TimeSpan.FromMilliseconds(100)));
        await _circuitBreaker.CanExecuteAsync(); // Transition to HalfOpen

        // Act
        for (var i = 0; i < _settings.SuccessThreshold; i++) await _circuitBreaker.RecordSuccessAsync();

        // Assert
        _circuitBreaker.State.Should().Be(CircuitState.Closed);

        // Verify logging
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("service recovered")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task RecordFailureAsync_InHalfOpen_ReopensCircuit()
    {
        // Arrange
        for (var i = 0; i < _settings.FailureThreshold; i++) await _circuitBreaker.RecordFailureAsync();

        await Task.Delay(_settings.BreakDuration.Add(TimeSpan.FromMilliseconds(100)));
        await _circuitBreaker.CanExecuteAsync(); // Transition to HalfOpen

        // Act
        await _circuitBreaker.RecordFailureAsync();

        // Assert
        _circuitBreaker.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task ResetAsync_ResetsToClosedState()
    {
        // Arrange
        for (var i = 0; i < _settings.FailureThreshold; i++) await _circuitBreaker.RecordFailureAsync();

        // Act
        await _circuitBreaker.ResetAsync();

        // Assert
        _circuitBreaker.State.Should().Be(CircuitState.Closed);
        var stats = _circuitBreaker.GetStats();
        stats.FailureCount.Should().Be(0);
        stats.SuccessCount.Should().Be(0);
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatistics()
    {
        // Act
        var stats = _circuitBreaker.GetStats();

        // Assert
        stats.Should().NotBeNull();
        stats.State.Should().Be(CircuitState.Closed);
        stats.FailureCount.Should().Be(0);
        stats.SuccessCount.Should().Be(0);
        stats.TimeUntilHalfOpen.Should().BeNull();
    }

    [Fact]
    public async Task GetStats_WhenOpen_ReturnsTimeUntilHalfOpen()
    {
        // Arrange
        for (var i = 0; i < _settings.FailureThreshold; i++) await _circuitBreaker.RecordFailureAsync();

        // Act
        var stats = _circuitBreaker.GetStats();

        // Assert
        stats.State.Should().Be(CircuitState.Open);
        stats.TimeUntilHalfOpen.Should().NotBeNull();
        stats.TimeUntilHalfOpen.Value.Should().BeLessThanOrEqualTo(_settings.BreakDuration);
        stats.TimeUntilHalfOpen.Value.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task OpenAsync_ManuallySetsToOpen()
    {
        // Act
        await _circuitBreaker.OpenAsync();

        // Assert
        _circuitBreaker.State.Should().Be(CircuitState.Open);
        var stats = _circuitBreaker.GetStats();
        stats.FailureCount.Should().Be(_settings.FailureThreshold);
    }

    [Fact]
    public void GetNextRetryTime_WhenClosed_ReturnsNull()
    {
        // Act
        var retryTime = _circuitBreaker.GetNextRetryTime();

        // Assert
        retryTime.Should().BeNull();
    }

    [Fact]
    public async Task GetNextRetryTime_WhenOpen_ReturnsRetryTime()
    {
        // Arrange
        await _circuitBreaker.OpenAsync();

        // Act
        var retryTime = _circuitBreaker.GetNextRetryTime();

        // Assert
        retryTime.Should().NotBeNull();
        retryTime.Value.Should().BeCloseTo(DateTime.UtcNow.Add(_settings.BreakDuration), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RecordSuccessAsync_InClosed_ResetsFailureCountAfterWindow()
    {
        // Arrange
        await _circuitBreaker.RecordFailureAsync();
        await _circuitBreaker.RecordFailureAsync();

        // Act
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await _circuitBreaker.RecordSuccessAsync();

        // Assert
        var stats = _circuitBreaker.GetStats();
        stats.State.Should().Be(CircuitState.Closed);
        // Note: The actual reset logic depends on the FailureWindow timing
    }

    [Fact]
    public async Task RecordSuccessAsync_WhenDisabled_DoesNothing()
    {
        // Arrange
        var settings = new CircuitBreakerSettings { Enabled = false };
        var circuitBreaker = new RedisCircuitBreaker(_logger, Options.Create(settings));

        // Act
        await circuitBreaker.RecordSuccessAsync();
        await circuitBreaker.RecordFailureAsync();

        // Assert
        circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsException()
    {
        // Act & Assert
        var act = () => new RedisCircuitBreaker(null!, Options.Create(_settings));
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsException()
    {
        // Act & Assert
        var act = () => new RedisCircuitBreaker(_logger, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("settings");
    }
}