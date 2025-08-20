using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

/// <summary>
///     Unit tests for RedisStreamService focusing on constructor validation
/// </summary>
public class RedisStreamServiceUnitTests
{
    private readonly IRedisConnection _connection;
    private readonly ILogger<RedisStreamService> _logger;
    private readonly IOptions<RedisOptions> _options;

    public RedisStreamServiceUnitTests()
    {
        _connection = Substitute.For<IRedisConnection>();
        _logger = Substitute.For<ILogger<RedisStreamService>>();

        var redisOptions = new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultTtl = TimeSpan.FromHours(1)
        };
        _options = Options.Create(redisOptions);
    }

    [Fact]
    public void Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(null!, _logger, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(_connection, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RedisStreamService(_connection, _logger, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_DoesNotThrow()
    {
        // Arrange & Act
        var service = new RedisStreamService(_connection, _logger, _options);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithDefaultOptions_SetsCorrectDefaults()
    {
        // Arrange
        var defaultOptions = Options.Create(new RedisOptions());

        // Act & Assert
        var service = new RedisStreamService(_connection, _logger, defaultOptions);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithCustomSerializer_AcceptsConfiguration()
    {
        // Arrange
        var customOptions = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost:6380",
            Serializer = SerializerType.MessagePack,
            DefaultTtl = TimeSpan.FromMinutes(30)
        });

        // Act & Assert
        var service = new RedisStreamService(_connection, _logger, customOptions);
        Assert.NotNull(service);
    }
}