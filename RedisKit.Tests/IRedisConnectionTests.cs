using FluentAssertions;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Models;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

/// <summary>
/// Tests for IRedisConnection interface methods
/// </summary>
public class IRedisConnectionTests
{
    private readonly IRedisConnection _connection;
    private readonly ConnectionHealthStatus _healthStatus;

    public IRedisConnectionTests()
    {
        _connection = Substitute.For<IRedisConnection>();
        _healthStatus = new ConnectionHealthStatus
        {
            IsHealthy = true,
            LastCheckTime = DateTime.UtcNow,
            ResponseTime = TimeSpan.FromMilliseconds(10),
            LastResponseTime = TimeSpan.FromMilliseconds(10),
            ConsecutiveFailures = 0,
            CircuitState = CircuitState.Closed,
            TotalRequests = 100,
            FailedRequests = 5
        };
    }

    #region GetHealthStatus Tests

    [Fact]
    public void GetHealthStatus_ReturnsHealthStatus()
    {
        // Arrange
        _connection.GetHealthStatus().Returns(_healthStatus);

        // Act
        var status = _connection.GetHealthStatus();

        // Assert
        status.Should().NotBeNull();
        status.Should().BeSameAs(_healthStatus);
        status.IsHealthy.Should().BeTrue();
        status.ConsecutiveFailures.Should().Be(0);
        status.CircuitState.Should().Be(CircuitState.Closed);
        status.TotalRequests.Should().Be(100);
        status.FailedRequests.Should().Be(5);
        status.SuccessRate.Should().BeApproximately(0.95, 0.01);
        
        _connection.Received(1).GetHealthStatus();
    }

    [Fact]
    public void GetHealthStatus_WhenUnhealthy_ReturnsCorrectStatus()
    {
        // Arrange
        var unhealthyStatus = new ConnectionHealthStatus
        {
            IsHealthy = false,
            LastCheckTime = DateTime.UtcNow.AddMinutes(-1),
            ResponseTime = TimeSpan.FromSeconds(5),
            ConsecutiveFailures = 10,
            CircuitState = CircuitState.Open,
            TotalRequests = 100,
            FailedRequests = 100,
            LastError = "Connection timeout"
        };
        
        _connection.GetHealthStatus().Returns(unhealthyStatus);

        // Act
        var status = _connection.GetHealthStatus();

        // Assert
        status.Should().NotBeNull();
        status.IsHealthy.Should().BeFalse();
        status.ConsecutiveFailures.Should().Be(10);
        status.CircuitState.Should().Be(CircuitState.Open);
        status.LastError.Should().Be("Connection timeout");
        status.SuccessRate.Should().Be(0);
        
        _connection.Received(1).GetHealthStatus();
    }

    [Fact]
    public void GetHealthStatus_WhenHalfOpen_ReturnsCorrectStatus()
    {
        // Arrange
        var halfOpenStatus = new ConnectionHealthStatus
        {
            IsHealthy = false,
            LastCheckTime = DateTime.UtcNow,
            ResponseTime = TimeSpan.FromMilliseconds(100),
            ConsecutiveFailures = 3,
            CircuitState = CircuitState.HalfOpen,
            TotalRequests = 50,
            FailedRequests = 10
        };
        
        _connection.GetHealthStatus().Returns(halfOpenStatus);

        // Act
        var status = _connection.GetHealthStatus();

        // Assert
        status.Should().NotBeNull();
        status.CircuitState.Should().Be(CircuitState.HalfOpen);
        status.SuccessRate.Should().BeApproximately(0.8, 0.01);
        
        _connection.Received(1).GetHealthStatus();
    }

    [Fact]
    public void GetHealthStatus_MultipleCalls_ReturnsSameReference()
    {
        // Arrange
        _connection.GetHealthStatus().Returns(_healthStatus);

        // Act
        var status1 = _connection.GetHealthStatus();
        var status2 = _connection.GetHealthStatus();
        var status3 = _connection.GetHealthStatus();

        // Assert
        status1.Should().BeSameAs(_healthStatus);
        status2.Should().BeSameAs(_healthStatus);
        status3.Should().BeSameAs(_healthStatus);
        
        _connection.Received(3).GetHealthStatus();
    }

    #endregion

    #region ResetCircuitBreakerAsync Tests

    [Fact]
    public async Task ResetCircuitBreakerAsync_CallsMethod()
    {
        // Arrange
        _connection.ResetCircuitBreakerAsync().Returns(Task.CompletedTask);

        // Act
        await _connection.ResetCircuitBreakerAsync();

        // Assert
        await _connection.Received(1).ResetCircuitBreakerAsync();
    }

    [Fact]
    public async Task ResetCircuitBreakerAsync_MultipleCalls_WorksCorrectly()
    {
        // Arrange
        _connection.ResetCircuitBreakerAsync().Returns(Task.CompletedTask);

        // Act
        await _connection.ResetCircuitBreakerAsync();
        await _connection.ResetCircuitBreakerAsync();
        await _connection.ResetCircuitBreakerAsync();

        // Assert
        await _connection.Received(3).ResetCircuitBreakerAsync();
    }

    [Fact]
    public async Task ResetCircuitBreakerAsync_WithException_PropagatesException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Cannot reset circuit breaker");
        _connection.ResetCircuitBreakerAsync()
            .Returns(Task.FromException(expectedException));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _connection.ResetCircuitBreakerAsync());
        
        exception.Message.Should().Be("Cannot reset circuit breaker");
        await _connection.Received(1).ResetCircuitBreakerAsync();
    }

    [Fact]
    public async Task ResetCircuitBreakerAsync_WithCancellation_PropagatesCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        _connection.ResetCircuitBreakerAsync()
            .Returns(Task.FromCanceled(cts.Token));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _connection.ResetCircuitBreakerAsync());
        
        await _connection.Received(1).ResetCircuitBreakerAsync();
    }

    #endregion

    #region GetDatabaseAsync Tests

    [Fact]
    public async Task GetDatabaseAsync_ReturnsDatabase()
    {
        // Arrange
        var database = Substitute.For<IDatabaseAsync>();
        _connection.GetDatabaseAsync().Returns(Task.FromResult(database));

        // Act
        var result = await _connection.GetDatabaseAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(database);
        await _connection.Received(1).GetDatabaseAsync();
    }

    [Fact]
    public async Task GetDatabaseAsync_WithException_PropagatesException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Connection failed");
        _connection.GetDatabaseAsync()
            .Returns(Task.FromException<IDatabaseAsync>(expectedException));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _connection.GetDatabaseAsync());
        
        exception.Message.Should().Be("Connection failed");
        await _connection.Received(1).GetDatabaseAsync();
    }

    #endregion

    #region GetSubscriberAsync Tests

    [Fact]
    public async Task GetSubscriberAsync_ReturnsSubscriber()
    {
        // Arrange
        var subscriber = Substitute.For<ISubscriber>();
        _connection.GetSubscriberAsync().Returns(Task.FromResult(subscriber));

        // Act
        var result = await _connection.GetSubscriberAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(subscriber);
        await _connection.Received(1).GetSubscriberAsync();
    }

    [Fact]
    public async Task GetSubscriberAsync_WithException_PropagatesException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Cannot get subscriber");
        _connection.GetSubscriberAsync()
            .Returns(Task.FromException<ISubscriber>(expectedException));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _connection.GetSubscriberAsync());
        
        exception.Message.Should().Be("Cannot get subscriber");
        await _connection.Received(1).GetSubscriberAsync();
    }

    #endregion

    #region GetMultiplexerAsync Tests

    [Fact]
    public async Task GetMultiplexerAsync_ReturnsMultiplexer()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));

        // Act
        var result = await _connection.GetMultiplexerAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(multiplexer);
        await _connection.Received(1).GetMultiplexerAsync();
    }

    [Fact]
    public async Task GetMultiplexerAsync_WithException_PropagatesException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Cannot get multiplexer");
        _connection.GetMultiplexerAsync()
            .Returns(Task.FromException<IConnectionMultiplexer>(expectedException));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _connection.GetMultiplexerAsync());
        
        exception.Message.Should().Be("Cannot get multiplexer");
        await _connection.Received(1).GetMultiplexerAsync();
    }

    #endregion

    #region Combined Operations Tests

    [Fact]
    public async Task AllMethods_CanBeCalled_InSequence()
    {
        // Arrange
        var database = Substitute.For<IDatabaseAsync>();
        var subscriber = Substitute.For<ISubscriber>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        
        _connection.GetHealthStatus().Returns(_healthStatus);
        _connection.ResetCircuitBreakerAsync().Returns(Task.CompletedTask);
        _connection.GetDatabaseAsync().Returns(Task.FromResult(database));
        _connection.GetSubscriberAsync().Returns(Task.FromResult(subscriber));
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));

        // Act
        var status = _connection.GetHealthStatus();
        await _connection.ResetCircuitBreakerAsync();
        var db = await _connection.GetDatabaseAsync();
        var sub = await _connection.GetSubscriberAsync();
        var mux = await _connection.GetMultiplexerAsync();

        // Assert
        status.Should().BeSameAs(_healthStatus);
        db.Should().BeSameAs(database);
        sub.Should().BeSameAs(subscriber);
        mux.Should().BeSameAs(multiplexer);
        
        _connection.Received(1).GetHealthStatus();
        await _connection.Received(1).ResetCircuitBreakerAsync();
        await _connection.Received(1).GetDatabaseAsync();
        await _connection.Received(1).GetSubscriberAsync();
        await _connection.Received(1).GetMultiplexerAsync();
    }

    [Fact]
    public void HealthStatus_SuccessRate_CalculatedCorrectly()
    {
        // Arrange & Act
        var status1 = new ConnectionHealthStatus
        {
            TotalRequests = 100,
            FailedRequests = 25
        };
        
        var status2 = new ConnectionHealthStatus
        {
            TotalRequests = 0,
            FailedRequests = 0
        };
        
        var status3 = new ConnectionHealthStatus
        {
            TotalRequests = 1000,
            FailedRequests = 50
        };

        // Assert
        status1.SuccessRate.Should().BeApproximately(0.75, 0.001);
        status2.SuccessRate.Should().Be(0); // No requests
        status3.SuccessRate.Should().BeApproximately(0.95, 0.001);
    }

    #endregion
}