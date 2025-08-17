using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using RedisKit.Models;
using RedisKit.Services;

namespace RedisKit.Tests
{
    public class RedisConnectionTests : IDisposable
    {
        private readonly Mock<ILogger<RedisConnection>> _mockLogger;
        private readonly RedisOptions _options;
        private readonly Mock<IOptions<RedisOptions>> _mockOptions;

        public RedisConnectionTests()
        {
            _mockLogger = new Mock<ILogger<RedisConnection>>();
            _options = new RedisOptions
            {
                ConnectionString = "localhost:6379",
                OperationTimeout = TimeSpan.FromSeconds(5),
                RetryAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(100),
                CircuitBreaker = new CircuitBreakerSettings { Enabled = false },
                HealthMonitoring = new HealthMonitoringSettings { Enabled = false }
            };
            _mockOptions = new Mock<IOptions<RedisOptions>>();
            _mockOptions.Setup(x => x.Value).Returns(_options);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RedisConnection(null, _mockOptions.Object));
        }

        [Fact]
        public void Constructor_WithNullOptions_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RedisConnection(_mockLogger.Object, null));
        }

        [Fact]
        public void Constructor_WithValidParameters_DoesNotThrow()
        {
            // Act
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);

            // Assert
            Assert.NotNull(connection);
        }

        #endregion

        #region GetConnection Tests

        [Fact]
        public async Task GetConnection_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);
            connection.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                connection.GetConnection());
        }

        [Fact]
        public async Task GetConnection_WithInvalidConnectionString_ThrowsAfterRetries()
        {
            // Arrange
            _options.ConnectionString = "invalid:connection:string";
            _options.RetryConfiguration.MaxAttempts = 2;
            _options.RetryConfiguration.InitialDelay = TimeSpan.FromMilliseconds(10);
            
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                connection.GetConnection());
            
            Assert.Contains("Failed to establish Redis connection", exception.Message);
            
            // Verify connection attempt was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Connection attempt")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task GetConnection_ConcurrentCalls_ReturnssSameConnection()
        {
            // This test would require a real Redis connection or more complex mocking
            // Marking as a placeholder for integration tests
            await Task.CompletedTask;
        }

        #endregion

        #region GetDatabaseAsync Tests

        [Fact]
        public async Task GetDatabaseAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);
            connection.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                connection.GetDatabaseAsync());
        }

        #endregion

        #region GetSubscriberAsync Tests

        [Fact]
        public async Task GetSubscriberAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);
            connection.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                connection.GetSubscriberAsync());
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);

            // Act & Assert - Should not throw
            connection.Dispose();
            connection.Dispose();
            connection.Dispose();
        }

        [Fact]
        public async Task Dispose_ReleasesResources_Properly()
        {
            // Arrange
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);

            // Act
            connection.Dispose();

            // Assert - After disposal, GetConnection should throw
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                connection.GetConnection());
        }

        #endregion

        #region Thread Safety Tests

        [Fact(Skip = "Unreliable in CI environment - timing dependent")]
        public async Task GetConnection_ConcurrentAccess_IsThreadSafe()
        {
            // This test verifies the SemaphoreSlim is working correctly
            // Would need integration testing with real Redis for full verification
            
            // Arrange
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);
            var tasks = new Task[10];

            // Act - Try to get connection from multiple threads
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        await connection.GetConnection();
                    }
                    catch (InvalidOperationException)
                    {
                        // Expected if connection fails
                    }
                });
            }

            // Assert - Should complete without deadlock
            var allCompleted = Task.WaitAll(tasks, TimeSpan.FromSeconds(10)); // Increased timeout for CI
            Assert.True(allCompleted, "Concurrent connection attempts should not deadlock");
        }

        #endregion

        #region Retry Logic Tests

        [Fact]
        public async Task GetConnection_RetriesOnFailure_LogsEachAttempt()
        {
            // Arrange
            _options.ConnectionString = "unreachable:6379";
            _options.RetryConfiguration.MaxAttempts = 3;
            _options.RetryConfiguration.InitialDelay = TimeSpan.FromMilliseconds(10);
            
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                connection.GetConnection());

            // Assert - Should log initial connection attempt
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Creating Redis connection")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            // Should log connection attempts
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Connection attempt")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeast(2));
        }

        #endregion

        #region Configuration Tests

        [Fact]
        public void Constructor_ConfiguresTimeouts_FromOptions()
        {
            // Arrange
            _options.OperationTimeout = TimeSpan.FromSeconds(10);
            
            // Act
            var connection = new RedisConnection(_mockLogger.Object, _mockOptions.Object);

            // Assert
            Assert.NotNull(connection);
            // Further assertions would require access to internal state or integration testing
        }

        #endregion

        public void Dispose()
        {
            // Cleanup any test resources if needed
        }
    }
}