using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;

namespace RedisKit.Tests.Helpers;

/// <summary>
///     Helper class for standardizing Redis test setup
/// </summary>
public static class RedisTestHelper
{
    /// <summary>
    ///     Creates a service collection with standard test services configured
    /// </summary>
    /// <param name="configureOptions">Optional action to configure Redis options</param>
    /// <param name="includeCircuitBreaker">Whether to include circuit breaker configuration</param>
    /// <returns>Configured service collection</returns>
    public static IServiceCollection CreateTestServices(
        Action<RedisOptions>? configureOptions = null,
        bool includeCircuitBreaker = true)
    {
        var services = new ServiceCollection();

        // Add logging (simplified for test environment)
        services.AddLogging();

        // Configure Redis options
        services.Configure<RedisOptions>(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.DefaultTtl = TimeSpan.FromMinutes(5);
            options.CacheKeyPrefix = "test:";
            options.RetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromSeconds(1);
            options.OperationTimeout = TimeSpan.FromSeconds(5);
            options.Serializer = SerializerType.SystemTextJson;

            // Apply custom configuration if provided
            configureOptions?.Invoke(options);
        });

        // Add circuit breaker if requested
        if (includeCircuitBreaker)
            services.Configure<CircuitBreakerSettings>(settings =>
            {
                settings.Enabled = true;
                settings.FailureThreshold = 5;
                settings.SuccessThreshold = 3;
                settings.BreakDuration = TimeSpan.FromSeconds(30);
                settings.FailureWindow = TimeSpan.FromMinutes(1);
            });

        return services;
    }

    /// <summary>
    ///     Creates a mock circuit breaker for testing
    /// </summary>
    /// <param name="state">Initial circuit state</param>
    /// <param name="canExecute">Whether the circuit allows execution</param>
    /// <returns>Mock circuit breaker</returns>
    public static Mock<IRedisCircuitBreaker> CreateMockCircuitBreaker(
        CircuitState state = CircuitState.Closed,
        bool canExecute = true)
    {
        var mock = new Mock<IRedisCircuitBreaker>();

        mock.Setup(x => x.State).Returns(state);
        mock.Setup(x => x.CanExecuteAsync()).ReturnsAsync(canExecute);
        mock.Setup(x => x.RecordSuccessAsync()).Returns(Task.CompletedTask);
        mock.Setup(x => x.RecordFailureAsync(It.IsAny<Exception?>())).Returns(Task.CompletedTask);
        mock.Setup(x => x.ResetAsync()).Returns(Task.CompletedTask);
        mock.Setup(x => x.OpenAsync()).Returns(Task.CompletedTask);
        mock.Setup(x => x.GetNextRetryTime()).Returns((DateTime?)null);
        mock.Setup(x => x.GetStats()).Returns(new RedisCircuitBreakerStats
        {
            State = state,
            FailureCount = 0,
            SuccessCount = 0,
            LastFailureTime = DateTime.MinValue,
            OpenedAt = DateTime.MinValue,
            TimeUntilHalfOpen = null
        });

        return mock;
    }

    /// <summary>
    ///     Creates a mock Redis database for testing
    /// </summary>
    /// <returns>Mock Redis database</returns>
    public static Mock<IDatabaseAsync> CreateMockDatabase()
    {
        var mock = new Mock<IDatabaseAsync>();

        // Setup common operations
        mock.Setup(x => x.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(10));

        mock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        mock.Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        mock.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        mock.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        mock.Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        return mock;
    }

    /// <summary>
    ///     Creates a mock Redis subscriber for testing
    /// </summary>
    /// <returns>Mock Redis subscriber</returns>
    public static Mock<ISubscriber> CreateMockSubscriber()
    {
        var mock = new Mock<ISubscriber>();

        mock.Setup(x => x.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((ChannelMessageQueue)null!);

        mock.Setup(x => x.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        mock.Setup(x => x.UnsubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    /// <summary>
    ///     Creates a mock connection multiplexer for testing
    /// </summary>
    /// <param name="isConnected">Whether the connection is connected</param>
    /// <returns>Mock connection multiplexer</returns>
    public static Mock<IConnectionMultiplexer> CreateMockMultiplexer(bool isConnected = true)
    {
        var mock = new Mock<IConnectionMultiplexer>();
        var mockDatabase = CreateMockDatabase();
        var mockSubscriber = CreateMockSubscriber();

        // Need to create a mock IDatabase that also implements IDatabaseAsync
        var mockDatabaseSync = new Mock<IDatabase>();
        mockDatabaseSync.As<IDatabaseAsync>().Setup(x => x.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(10));

        mock.Setup(x => x.IsConnected).Returns(isConnected);
        mock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDatabaseSync.Object);
        mock.Setup(x => x.GetSubscriber(It.IsAny<object>()))
            .Returns(mockSubscriber.Object);
        mock.Setup(x => x.GetEndPoints(It.IsAny<bool>()))
            .Returns(new[] { new IPEndPoint(IPAddress.Loopback, 6379) });

        return mock;
    }

    /// <summary>
    ///     Adds mock Redis services for testing without actual Redis connection
    /// </summary>
    public static IServiceCollection AddMockRedisServices(this IServiceCollection services)
    {
        // Add mock circuit breaker
        services.AddSingleton<IRedisCircuitBreaker>(new TestCircuitBreakerFactory());

        // Add mock multiplexer and related services
        var mockMultiplexer = CreateMockMultiplexer();
        var mockDatabase = CreateMockDatabase();
        var mockSubscriber = CreateMockSubscriber();

        services.AddSingleton(mockMultiplexer.Object);
        services.AddSingleton(mockDatabase.Object);
        services.AddSingleton(mockSubscriber.Object);

        return services;
    }

    /// <summary>
    ///     Creates a test circuit breaker factory for dependency injection
    /// </summary>
    public class TestCircuitBreakerFactory : IRedisCircuitBreaker
    {
        private readonly bool _canExecute;

        public TestCircuitBreakerFactory(CircuitState state = CircuitState.Closed, bool canExecute = true)
        {
            State = state;
            _canExecute = canExecute;
        }

        public CircuitState State { get; }

        public Task<bool> CanExecuteAsync()
        {
            return Task.FromResult(_canExecute);
        }

        public Task RecordSuccessAsync()
        {
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(Exception? exception = null)
        {
            return Task.CompletedTask;
        }

        public Task ResetAsync()
        {
            return Task.CompletedTask;
        }

        public Task OpenAsync()
        {
            return Task.CompletedTask;
        }

        public DateTime? GetNextRetryTime()
        {
            return null;
        }

        public RedisCircuitBreakerStats GetStats()
        {
            return new RedisCircuitBreakerStats
            {
                State = State,
                FailureCount = 0,
                SuccessCount = 0,
                LastFailureTime = DateTime.MinValue,
                OpenedAt = DateTime.MinValue,
                TimeUntilHalfOpen = null
            };
        }
    }
}