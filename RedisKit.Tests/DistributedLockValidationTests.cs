using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using Xunit;

namespace RedisKit.Tests;

public class DistributedLockValidationTests
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisDistributedLock> _logger;
    private readonly RedisDistributedLock _lockService;

    public DistributedLockValidationTests()
    {
        _connection = Substitute.For<IRedisConnection>();
        _logger = Substitute.For<ILogger<RedisDistributedLock>>();
        var options = Options.Create(new DistributedLockOptions());
        _lockService = new RedisDistributedLock(_connection, options, _logger);
    }

    [Fact]
    public async Task AcquireLockAsync_WithExpiryLessThanOneSecond_ThrowsException()
    {
        // Arrange
        var expiry = TimeSpan.FromMilliseconds(999);

        // Act & Assert
        var act = async () => await _lockService.AcquireLockAsync("resource", expiry);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Lock expiry must be at least 1 second*")
            .WithParameterName("expiry");
    }

    [Fact]
    public async Task AcquireLockAsync_WithExpiryExactlyOneSecond_Succeeds()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(1);
        var database = Substitute.For<StackExchange.Redis.IDatabaseAsync>();
        _connection.GetDatabaseAsync().Returns(Task.FromResult(database));
        database.StringSetAsync(
            Arg.Any<StackExchange.Redis.RedisKey>(),
            Arg.Any<StackExchange.Redis.RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<StackExchange.Redis.When>(),
            Arg.Any<StackExchange.Redis.CommandFlags>())
            .Returns(Task.FromResult(false)); // Lock not acquired, but no exception

        // Act & Assert
        var act = async () => await _lockService.AcquireLockAsync("resource", expiry);
        await act.Should().NotThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AcquireLockAsync_WithExpiryMoreThanOneHour_ThrowsException()
    {
        // Arrange
        var expiry = TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1));

        // Act & Assert
        var act = async () => await _lockService.AcquireLockAsync("resource", expiry);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Lock expiry cannot exceed 1 hour*")
            .WithParameterName("expiry");
    }

    [Fact]
    public async Task AcquireLockAsync_WithExpiryExactlyOneHour_Succeeds()
    {
        // Arrange
        var expiry = TimeSpan.FromHours(1);
        var database = Substitute.For<StackExchange.Redis.IDatabaseAsync>();
        _connection.GetDatabaseAsync().Returns(Task.FromResult(database));
        database.StringSetAsync(
            Arg.Any<StackExchange.Redis.RedisKey>(),
            Arg.Any<StackExchange.Redis.RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<StackExchange.Redis.When>(),
            Arg.Any<StackExchange.Redis.CommandFlags>())
            .Returns(Task.FromResult(false)); // Lock not acquired, but no exception

        // Act & Assert
        var act = async () => await _lockService.AcquireLockAsync("resource", expiry);
        await act.Should().NotThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExtendLockAsync_WithExpiryLessThanOneSecond_ThrowsException()
    {
        // Arrange
        var handle = Substitute.For<ILockHandle>();
        var expiry = TimeSpan.FromMilliseconds(500);

        // Act & Assert
        var act = async () => await _lockService.ExtendLockAsync(handle, expiry);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Lock expiry must be at least 1 second*")
            .WithParameterName("expiry");
    }

    [Fact]
    public async Task ExtendLockAsync_WithExpiryMoreThanOneHour_ThrowsException()
    {
        // Arrange
        var handle = Substitute.For<ILockHandle>();
        var expiry = TimeSpan.FromHours(2);

        // Act & Assert
        var act = async () => await _lockService.ExtendLockAsync(handle, expiry);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Lock expiry cannot exceed 1 hour*")
            .WithParameterName("expiry");
    }

    [Fact]
    public async Task ExtendLockAsync_WithValidExpiry_CallsHandleExtend()
    {
        // Arrange
        var handle = Substitute.For<ILockHandle>();
        var expiry = TimeSpan.FromSeconds(30);
        handle.ExtendAsync(expiry, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        var result = await _lockService.ExtendLockAsync(handle, expiry);

        // Assert
        result.Should().BeTrue();
        await handle.Received(1).ExtendAsync(expiry, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)] // 1 second - minimum valid
    [InlineData(30)] // 30 seconds
    [InlineData(300)] // 5 minutes
    [InlineData(1800)] // 30 minutes
    [InlineData(3600)] // 1 hour - maximum valid
    public async Task AcquireLockAsync_WithValidExpiryValues_DoesNotThrow(int seconds)
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(seconds);
        var database = Substitute.For<StackExchange.Redis.IDatabaseAsync>();
        _connection.GetDatabaseAsync().Returns(Task.FromResult(database));
        database.StringSetAsync(
            Arg.Any<StackExchange.Redis.RedisKey>(),
            Arg.Any<StackExchange.Redis.RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<StackExchange.Redis.When>(),
            Arg.Any<StackExchange.Redis.CommandFlags>())
            .Returns(Task.FromResult(false));

        // Act & Assert
        var act = async () => await _lockService.AcquireLockAsync("resource", expiry);
        await act.Should().NotThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0)] // 0 seconds
    [InlineData(0.5)] // 500 milliseconds
    [InlineData(0.999)] // 999 milliseconds
    [InlineData(3601)] // 1 hour + 1 second
    [InlineData(7200)] // 2 hours
    public async Task AcquireLockAsync_WithInvalidExpiryValues_ThrowsException(double seconds)
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(seconds);

        // Act & Assert
        var act = async () => await _lockService.AcquireLockAsync("resource", expiry);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("expiry");
    }

    [Fact]
    public async Task AcquireLockAsync_WithNegativeExpiry_ThrowsException()
    {
        // Arrange
        var expiry = TimeSpan.FromSeconds(-10);

        // Act & Assert
        var act = async () => await _lockService.AcquireLockAsync("resource", expiry);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Expiry must be positive*")
            .WithParameterName("expiry");
    }

    [Fact]
    public async Task ExtendLockAsync_WithNullHandle_ThrowsException()
    {
        // Act & Assert
        var act = async () => await _lockService.ExtendLockAsync(null!, TimeSpan.FromSeconds(30));
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("handle");
    }

    [Fact]
    public async Task ExtendLockAsync_WithZeroExpiry_ThrowsException()
    {
        // Arrange
        var handle = Substitute.For<ILockHandle>();

        // Act & Assert
        var act = async () => await _lockService.ExtendLockAsync(handle, TimeSpan.Zero);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Expiry must be positive*")
            .WithParameterName("expiry");
    }
}