using Microsoft.Extensions.Logging;
using NSubstitute;
using RedisKit.Exceptions;
using RedisKit.Helpers;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests.Helpers;

public class RedisOperationExecutorTests
{
    private readonly ILogger<RedisOperationExecutorTests> _logger;

    public RedisOperationExecutorTests()
    {
        _logger = Substitute.For<ILogger<RedisOperationExecutorTests>>();
    }

    [Fact]
    public async Task ExecuteAsync_Success_ShouldReturnResult()
    {
        // Arrange
        var expectedResult = new TestResult { Value = "Success" };

        // Act
        var result = await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                await Task.Delay(10);
                return expectedResult;
            },
            _logger,
            "test-key"
        );

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Success", result.Value);
    }

    [Fact]
    public async Task ExecuteAsync_RedisConnectionException_ShouldWrapException()
    {
        // Arrange & Act & Assert
        var exception = await Assert.ThrowsAsync<RedisKitConnectionException>(async () =>
            await RedisOperationExecutor.ExecuteAsync<TestResult>(
                () => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"),
                _logger,
                "test-key"
            )
        );

        Assert.Contains("Failed to connect to Redis", exception.Message);
        Assert.IsType<RedisConnectionException>(exception.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_RedisTimeoutException_ShouldWrapException()
    {
        // Arrange & Act & Assert
        var exception = await Assert.ThrowsAsync<RedisKitTimeoutException>(async () =>
            await RedisOperationExecutor.ExecuteAsync<TestResult>(
                () => throw new RedisTimeoutException("Timeout", CommandStatus.Unknown),
                _logger,
                "test-key"
            )
        );

        Assert.Contains("Redis operation timed out", exception.Message);
        Assert.IsType<RedisTimeoutException>(exception.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_RedisServerException_ShouldWrapException()
    {
        // Arrange & Act & Assert
        var exception = await Assert.ThrowsAsync<RedisKitServerException>(async () =>
            await RedisOperationExecutor.ExecuteAsync<TestResult>(
                () => throw new RedisServerException("Server error"),
                _logger,
                "test-key"
            )
        );

        Assert.Contains("Redis server error", exception.Message);
        Assert.IsType<RedisServerException>(exception.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_GenericException_ShouldWrapInRedisKitException()
    {
        // Arrange & Act & Assert
        var exception = await Assert.ThrowsAsync<RedisKitException>(async () =>
            await RedisOperationExecutor.ExecuteAsync<TestResult>(
                () => throw new InvalidOperationException("Something went wrong"),
                _logger,
                "test-key"
            )
        );

        Assert.Contains("Unexpected error", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomExceptionHandler_ShouldUseHandler()
    {
        // Arrange
        var customResult = new TestResult { Value = "Handled" };

        // Act
        var result = await RedisOperationExecutor.ExecuteAsync(
            () => throw new RedisServerException("Custom error"),
            _logger,
            "test-key",
            CancellationToken.None,
            handleSpecificExceptions: ex =>
            {
                if (ex is RedisServerException rse && rse.Message.Contains("Custom"))
                    return customResult;
                return null;
            }
        );

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Handled", result!.Value);
    }

    [Fact]
    public async Task ExecuteAsync_ValueTask_Success_ShouldComplete()
    {
        // Arrange
        var executed = false;

        // Act
        await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                await Task.Delay(10);
                executed = true;
            },
            _logger,
            "test-key"
        );

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task ExecuteAsync_ValueTask_WithException_ShouldWrapException()
    {
        // Arrange & Act & Assert
        var exception = await Assert.ThrowsAsync<RedisKitConnectionException>(async () =>
            await RedisOperationExecutor.ExecuteAsync(
                () => ValueTask.FromException(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed")),
                _logger,
                "test-key"
            )
        );

        Assert.Contains("Failed to connect to Redis", exception.Message);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_Success_ShouldNotRetry()
    {
        // Arrange
        var callCount = 0;
        var result = new TestResult { Value = "Success" };

        // Act
        var actualResult = await RedisOperationExecutor.ExecuteWithRetryAsync(
            async () =>
            {
                callCount++;
                await Task.Delay(10);
                return result;
            },
            _logger,
            3,
            "test-key"
        );

        // Assert
        Assert.Equal(1, callCount);
        Assert.NotNull(actualResult);
        Assert.Equal("Success", actualResult.Value);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TransientError_ShouldRetry()
    {
        // Arrange
        var callCount = 0;
        var result = new TestResult { Value = "Success" };

        // Act
        var actualResult = await RedisOperationExecutor.ExecuteWithRetryAsync(
            async () =>
            {
                callCount++;
                if (callCount < 3)
                    throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Transient");
                await Task.Delay(10);
                return result;
            },
            _logger,
            3,
            "test-key"
        );

        // Assert
        Assert.Equal(3, callCount);
        Assert.NotNull(actualResult);
        Assert.Equal("Success", actualResult.Value);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TimeoutError_ShouldRetry()
    {
        // Arrange
        var callCount = 0;
        var result = new TestResult { Value = "Success" };

        // Act
        var actualResult = await RedisOperationExecutor.ExecuteWithRetryAsync(
            async () =>
            {
                callCount++;
                if (callCount == 1)
                    throw new RedisTimeoutException("Timeout", CommandStatus.Unknown);
                await Task.Delay(10);
                return result;
            },
            _logger,
            2,
            "test-key"
        );

        // Assert
        Assert.Equal(2, callCount);
        Assert.NotNull(actualResult);
        Assert.Equal("Success", actualResult.Value);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_LoadingError_ShouldRetry()
    {
        // Arrange
        var callCount = 0;
        var result = new TestResult { Value = "Success" };

        // Act
        var actualResult = await RedisOperationExecutor.ExecuteWithRetryAsync(
            async () =>
            {
                callCount++;
                if (callCount == 1)
                    throw new RedisServerException("LOADING Redis is loading the dataset in memory");
                await Task.Delay(10);
                return result;
            },
            _logger,
            2,
            "test-key"
        );

        // Assert
        Assert.Equal(2, callCount);
        Assert.NotNull(actualResult);
        Assert.Equal("Success", actualResult.Value);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NonTransientError_ShouldNotRetry()
    {
        // Arrange
        var callCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<RedisKitException>(async () =>
            await RedisOperationExecutor.ExecuteWithRetryAsync<TestResult>(
                () =>
                {
                    callCount++;
                    throw new InvalidOperationException("Non-transient error");
                },
                _logger,
                3,
                "test-key"
            )
        );

        Assert.Equal(1, callCount);
    }


    [Fact]
    public async Task ExecuteWithRetryAsync_Cancellation_ShouldThrowOperationCanceled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await RedisOperationExecutor.ExecuteWithRetryAsync<TestResult>(
                async () =>
                {
                    await Task.Delay(100);
                    return new TestResult();
                },
                _logger,
                3,
                "test-key",
                cts.Token
            )
        );
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithDelay_ShouldDelayBetweenRetries()
    {
        // Arrange
        var callCount = 0;
        var startTime = DateTime.UtcNow;
        var result = new TestResult { Value = "Success" };

        // Act
        var actualResult = await RedisOperationExecutor.ExecuteWithRetryAsync(
            () =>
            {
                callCount++;
                if (callCount < 3)
                    throw new RedisTimeoutException("Timeout", CommandStatus.Unknown);
                return Task.FromResult<TestResult?>(result);
            },
            _logger,
            2,
            "test-key"
        );

        // Assert
        var elapsed = DateTime.UtcNow - startTime;
        Assert.Equal(3, callCount);
        Assert.True(elapsed.TotalMilliseconds >= 200); // At least 100ms * 2 delays
        Assert.NotNull(actualResult);
    }

    [Fact]
    public async Task ExecuteAsync_NullLogger_ShouldNotThrow()
    {
        // Arrange
        var result = new TestResult { Value = "Success" };

        // Act
        var actualResult = await RedisOperationExecutor.ExecuteAsync(
            () => Task.FromResult<TestResult?>(result),
            null,
            "test-key"
        );

        // Assert
        Assert.NotNull(actualResult);
        Assert.Equal("Success", actualResult.Value);
    }

    [Fact]
    public async Task ExecuteAsync_RedisKitException_ShouldNotWrapAgain()
    {
        // Arrange
        var originalException = new RedisKitConnectionException("Original message");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RedisKitConnectionException>(async () =>
            await RedisOperationExecutor.ExecuteAsync<TestResult>(
                () => throw originalException,
                _logger,
                "test-key"
            )
        );

        Assert.Same(originalException, exception);
    }

    [Fact]
    public async Task ExecuteWithSilentErrorHandlingAsync_Should_Return_Default_On_Pattern_Match()
    {
        // Arrange
        var defaultValue = new TestResult { Value = "Default" };

        // Act
        var result = await RedisOperationExecutor.ExecuteWithSilentErrorHandlingAsync(
            () => throw new RedisServerException("NOSCRIPT test error"),
            _logger,
            "NOSCRIPT",
            defaultValue,
            "test-key"
        );

        // Assert
        Assert.Equal(defaultValue, result);
    }

    [Fact]
    public async Task ExecuteWithSilentErrorHandlingAsync_Should_Throw_On_Non_Pattern_Match()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<RedisKitException>(async () =>
            await RedisOperationExecutor.ExecuteWithSilentErrorHandlingAsync<TestResult>(
                () => throw new RedisServerException("Different error"),
                _logger,
                "NOSCRIPT",
                defaultValue: null,
                "test-key"
            )
        );
    }

    [Fact]
    public async Task ExecuteWithFallbackAsync_Should_Execute_Fallback_On_Pattern_Match()
    {
        // Arrange
        var fallbackResult = new TestResult { Value = "Fallback" };
        var fallbackExecuted = false;

        // Act
        var result = await RedisOperationExecutor.ExecuteWithFallbackAsync(
            () => throw new RedisServerException("NOSCRIPT cache miss"),
            () =>
            {
                fallbackExecuted = true;
                return Task.FromResult<TestResult?>(fallbackResult);
            },
            _logger,
            "NOSCRIPT",
            "test-key"
        );

        // Assert
        Assert.True(fallbackExecuted);
        Assert.Equal(fallbackResult, result);
    }

    [Fact]
    public async Task ExecuteWithFallbackAsync_Should_Throw_On_Non_Pattern_Match()
    {
        // Arrange
        var fallbackExecuted = false;

        // Act & Assert
        await Assert.ThrowsAsync<RedisKitException>(async () =>
            await RedisOperationExecutor.ExecuteWithFallbackAsync<TestResult>(
                () => throw new RedisServerException("Different error"),
                () =>
                {
                    fallbackExecuted = true;
                    return Task.FromResult<TestResult?>(new TestResult());
                },
                _logger,
                "NOSCRIPT",
                "test-key"
            )
        );

        Assert.False(fallbackExecuted);
    }

    [Fact]
    public async Task ExecuteVoidAsync_Should_Complete_Successfully()
    {
        // Arrange
        var executed = false;

        // Act
        await RedisOperationExecutor.ExecuteVoidAsync(
            () =>
            {
                executed = true;
                return Task.CompletedTask;
            },
            _logger,
            "test-key"
        );

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task ExecuteVoidAsync_Should_Handle_RedisConnectionException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<RedisKitConnectionException>(async () =>
            await RedisOperationExecutor.ExecuteVoidAsync(
                () => throw new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, "Auth failed"),
                _logger,
                "test-key"
            )
        );
    }

    [Fact]
    public async Task ExecuteWithSilentErrorHandlingAsync_Should_Handle_Connection_Exceptions()
    {
        // Act & Assert
        await Assert.ThrowsAsync<RedisKitConnectionException>(async () =>
            await RedisOperationExecutor.ExecuteWithSilentErrorHandlingAsync<TestResult>(
                () => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "Connection failed"),
                _logger,
                "NOSCRIPT",
                defaultValue: null,
                "test-key"
            )
        );
    }

    [Fact]
    public async Task ExecuteWithFallbackAsync_Should_Handle_Timeout_Exceptions()
    {
        // Act & Assert
        await Assert.ThrowsAsync<RedisKitTimeoutException>(async () =>
            await RedisOperationExecutor.ExecuteWithFallbackAsync<TestResult>(
                () => throw new RedisTimeoutException("Timeout occurred", CommandStatus.Unknown),
                () => Task.FromResult<TestResult?>(new TestResult()),
                _logger,
                "NOSCRIPT",
                "test-key"
            )
        );
    }

    [Fact]
    public void RedisErrorPatterns_Should_Have_Correct_Constants()
    {
        // Assert
        Assert.Equal("NOSCRIPT", RedisErrorPatterns.NoScript);
        Assert.Equal("BUSYGROUP", RedisErrorPatterns.BusyGroup);
        Assert.Equal("unknown command", RedisErrorPatterns.UnknownCommand);
        Assert.Equal("ERR unknown command", RedisErrorPatterns.ErrUnknownCommand);
        Assert.Equal("ERR no such library", RedisErrorPatterns.NoSuchLibrary);
        Assert.Equal("LOADING", RedisErrorPatterns.Loading);
        Assert.Equal("ERR", RedisErrorPatterns.Err);
    }

    public class TestResult
    {
        public string Value { get; set; } = string.Empty;
    }
}