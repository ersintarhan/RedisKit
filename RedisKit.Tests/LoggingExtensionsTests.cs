using Microsoft.Extensions.Logging;
using RedisKit.Logging;
using RedisKit.Models;
using RedisKit.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

/// <summary>
///     Tests for source-generated logging extensions using InMemoryLogger
/// </summary>
public class LoggingExtensionsTests
{
    private readonly InMemoryLogger _logger;

    public LoggingExtensionsTests()
    {
        _logger = new InMemoryLogger();
    }

    #region Cache Service Logging Tests

    [Fact]
    public void LogGetAsync_WithValidKey_LogsDebugMessage()
    {
        // Act
        _logger.LogGetAsync("test-key");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Getting item from cache with key: test-key"));
        Assert.True(_logger.HasLogEntry(new EventId(1)));
    }

    [Fact]
    public void LogGetAsyncSuccess_WithValidKey_LogsDebugMessage()
    {
        // Act
        _logger.LogGetAsyncSuccess("test-key");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Successfully retrieved item from cache with key: test-key"));
        Assert.True(_logger.HasLogEntry(new EventId(2)));
    }

    [Fact]
    public void LogGetAsyncError_WithException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("Cache error");

        // Act
        _logger.LogGetAsyncError("error-key", exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error getting item from cache with key: error-key"));
        Assert.True(_logger.HasLogEntry(new EventId(3)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogSetAsync_WithValidKey_LogsDebugMessage()
    {
        // Act
        _logger.LogSetAsync("new-key");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Setting item in cache with key: new-key"));
        Assert.True(_logger.HasLogEntry(new EventId(4)));
    }

    [Fact]
    public void LogSetAsyncSuccess_WithValidKey_LogsDebugMessage()
    {
        // Act
        _logger.LogSetAsyncSuccess("new-key");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Successfully set item in cache with key: new-key"));
        Assert.True(_logger.HasLogEntry(new EventId(5)));
    }

    [Fact]
    public void LogSetAsyncError_WithException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("Set error");

        // Act
        _logger.LogSetAsyncError("error-key", exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error setting item in cache with key: error-key"));
        Assert.True(_logger.HasLogEntry(new EventId(6)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogDeleteAsync_WithValidKey_LogsDebugMessage()
    {
        // Act
        _logger.LogDeleteAsync("delete-key");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Deleting item from cache with key: delete-key"));
        Assert.True(_logger.HasLogEntry(new EventId(7)));
    }

    [Fact]
    public void LogDeleteAsyncSuccess_WithValidKey_LogsDebugMessage()
    {
        // Act
        _logger.LogDeleteAsyncSuccess("delete-key");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Successfully deleted item from cache with key: delete-key"));
        Assert.True(_logger.HasLogEntry(new EventId(8)));
    }

    [Fact]
    public void LogDeleteAsyncError_WithException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("Delete error");

        // Act
        _logger.LogDeleteAsyncError("error-key", exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error deleting item from cache with key: error-key"));
        Assert.True(_logger.HasLogEntry(new EventId(9)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogGetManyAsync_WithKeys_LogsDebugMessage()
    {
        // Act
        _logger.LogGetManyAsync("key1,key2,key3");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Getting multiple items from cache with keys: key1,key2,key3"));
        Assert.True(_logger.HasLogEntry(new EventId(10)));
    }

    [Fact]
    public void LogGetManyAsyncSuccess_WithCount_LogsDebugMessage()
    {
        // Act
        _logger.LogGetManyAsyncSuccess(5);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Successfully retrieved 5 items from cache"));
        Assert.True(_logger.HasLogEntry(new EventId(11)));
    }

    [Fact]
    public void LogGetManyAsyncError_WithException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("GetMany error");

        // Act
        _logger.LogGetManyAsyncError(exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error getting multiple items from cache"));
        Assert.True(_logger.HasLogEntry(new EventId(12)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogSetManyAsync_WithCount_LogsDebugMessage()
    {
        // Act
        _logger.LogSetManyAsync(3);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Setting multiple items in cache with 3 keys"));
        Assert.True(_logger.HasLogEntry(new EventId(13)));
    }

    [Fact]
    public void LogSetManyAsyncSuccess_WithCount_LogsDebugMessage()
    {
        // Act
        _logger.LogSetManyAsyncSuccess(3);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Successfully set 3 items in cache"));
        Assert.True(_logger.HasLogEntry(new EventId(14)));
    }

    [Fact]
    public void LogSetManyAsyncError_WithException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("SetMany error");

        // Act
        _logger.LogSetManyAsyncError(exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error setting multiple items in cache"));
        Assert.True(_logger.HasLogEntry(new EventId(15)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogExistsAsync_WithValidKey_LogsDebugMessage()
    {
        // Act
        _logger.LogExistsAsync("check-key");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Checking if key exists in cache: check-key"));
        Assert.True(_logger.HasLogEntry(new EventId(16)));
    }

    [Fact]
    public void LogExistsAsyncSuccess_WithKeyAndExists_LogsDebugMessage()
    {
        // Act
        _logger.LogExistsAsyncSuccess("check-key", true);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Key check-key exists: True"));
        Assert.True(_logger.HasLogEntry(new EventId(17)));
    }

    [Fact]
    public void LogExistsAsyncError_WithException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("Exists error");

        // Act
        _logger.LogExistsAsyncError("error-key", exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error checking if key exists in cache: error-key"));
        Assert.True(_logger.HasLogEntry(new EventId(18)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    #endregion

    #region Lua Script Logging Tests

    [Fact]
    public void LogLuaScriptSupported_LogsInformationMessage()
    {
        // Act
        _logger.LogLuaScriptSupported();

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Information, "Lua scripts are supported, using optimized path"));
        Assert.True(_logger.HasLogEntry(new EventId(19)));
    }

    [Fact]
    public void LogLuaScriptTestFailed_LogsWarningMessage()
    {
        // Act
        _logger.LogLuaScriptTestFailed();

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Warning, "Lua scripts test failed, using fallback mode"));
        Assert.True(_logger.HasLogEntry(new EventId(20)));
    }

    [Fact]
    public void LogLuaScriptNotSupported_WithException_LogsWarningMessage()
    {
        // Arrange
        var exception = new Exception("Script not supported");

        // Act
        _logger.LogLuaScriptNotSupported(exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Warning, "Lua scripts not supported, falling back to standard commands"));
        Assert.True(_logger.HasLogEntry(new EventId(21)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogSetManyAsyncProgress_WithProcessedAndTotal_LogsDebugMessage()
    {
        // Act
        _logger.LogSetManyAsyncProgress(3, 10);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "SetManyAsync progress: 3/10 items"));
        Assert.True(_logger.HasLogEntry(new EventId(22)));
    }

    [Fact]
    public void LogSlowSetManyAsync_WithParameters_LogsWarningMessage()
    {
        // Act
        _logger.LogSlowSetManyAsync(100, 5000, "Lua");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Warning, "Slow SetManyAsync detected: Count=100, Duration=5000ms, Strategy=Lua"));
        Assert.True(_logger.HasLogEntry(new EventId(23)));
    }

    [Fact]
    public void LogSetManyPartialSuccess_WithExpectedAndActual_LogsWarningMessage()
    {
        // Act
        _logger.LogSetManyPartialSuccess(10, 8);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Warning, "Some keys were not set successfully. Expected: 10, Actual: 8"));
        Assert.True(_logger.HasLogEntry(new EventId(24)));
    }

    [Fact]
    public void LogLuaScriptNotInCache_LogsWarningMessage()
    {
        // Act
        _logger.LogLuaScriptNotInCache();

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Warning, "Lua script not in cache, switching to fallback mode"));
        Assert.True(_logger.HasLogEntry(new EventId(25)));
    }

    [Fact]
    public void LogLuaScriptExecutionFailed_WithException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("Script execution failed");

        // Act
        _logger.LogLuaScriptExecutionFailed(exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Lua script execution failed, switching to fallback mode"));
        Assert.True(_logger.HasLogEntry(new EventId(26)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogSetManyBatchSizeWarning_WithCountAndThreshold_LogsWarningMessage()
    {
        // Act
        _logger.LogSetManyBatchSizeWarning(1500, 1000);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Warning, "SetManyAsync called with 1500 values, which exceeds recommended limit of 1000"));
        Assert.True(_logger.HasLogEntry(new EventId(27)));
    }

    #endregion

    #region PubSub Service Logging Tests

    [Fact]
    public void LogPublishAsync_WithChannel_LogsDebugMessage()
    {
        // Act
        _logger.LogPublishAsync("test-channel");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Publishing message to channel: test-channel"));
        Assert.True(_logger.HasLogEntry(new EventId(101)));
    }

    [Fact]
    public void LogPublishAsyncSuccess_WithChannel_LogsDebugMessage()
    {
        // Act
        _logger.LogPublishAsyncSuccess("test-channel");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Successfully published message to channel: test-channel"));
        Assert.True(_logger.HasLogEntry(new EventId(102)));
    }

    [Fact]
    public void LogPublishAsyncError_WithChannelAndException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("Publish error");

        // Act
        _logger.LogPublishAsyncError("error-channel", exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error publishing message to channel: error-channel"));
        Assert.True(_logger.HasLogEntry(new EventId(103)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogSubscribeAsync_WithChannel_LogsDebugMessage()
    {
        // Act
        _logger.LogSubscribeAsync("subscribe-channel");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Subscribing to channel: subscribe-channel"));
        Assert.True(_logger.HasLogEntry(new EventId(104)));
    }

    [Fact]
    public void LogSubscribeAsyncSuccess_WithChannel_LogsDebugMessage()
    {
        // Act
        _logger.LogSubscribeAsyncSuccess("subscribe-channel");

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Debug, "Successfully subscribed to channel: subscribe-channel"));
        Assert.True(_logger.HasLogEntry(new EventId(105)));
    }

    [Fact]
    public void LogSubscribeAsyncError_WithChannelAndException_LogsErrorMessage()
    {
        // Arrange
        var exception = new Exception("Subscribe error");

        // Act
        _logger.LogSubscribeAsyncError("error-channel", exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Error, "Error subscribing to channel: error-channel"));
        Assert.True(_logger.HasLogEntry(new EventId(106)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    #endregion

    #region Circuit Breaker Logging Tests

    [Fact]
    public void LogCircuitBreakerFailure_WithStateAndException_LogsWarningMessage()
    {
        // Arrange
        var exception = new Exception("Circuit breaker failure");

        // Act
        _logger.LogCircuitBreakerFailure(CircuitState.Open, exception);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Warning, "Circuit breaker recorded failure in Open state"));
        Assert.True(_logger.HasLogEntry(new EventId(501)));
        var logEntry = _logger.GetLastLogEntry();
        Assert.NotNull(logEntry);
        Assert.Equal(exception, logEntry.Exception);
    }

    [Fact]
    public void LogCircuitBreakerTransition_WithStates_LogsInformationMessage()
    {
        // Act
        _logger.LogCircuitBreakerTransition(CircuitState.Closed, CircuitState.Open);

        // Assert
        Assert.True(_logger.HasLogEntry(LogLevel.Information, "Circuit breaker transitioned from Closed to Open"));
        Assert.True(_logger.HasLogEntry(new EventId(502)));
    }

    #endregion

    #region Helper Methods Tests

    [Fact]
    public void InMemoryLogger_ClearMethod_RemovesAllEntries()
    {
        // Arrange
        _logger.LogGetAsync("key1");
        _logger.LogSetAsync("key2");
        Assert.Equal(2, _logger.LogEntries.Count);

        // Act
        _logger.Clear();

        // Assert
        Assert.Empty(_logger.LogEntries);
    }

    [Fact]
    public void InMemoryLogger_GetLogEntries_FiltersCorrectly()
    {
        // Arrange
        _logger.LogGetAsync("key1");                           // Debug
        _logger.LogLuaScriptSupported();                       // Information
        _logger.LogGetAsyncError("key2", new Exception());     // Error

        // Act
        var debugEntries = _logger.GetLogEntries(LogLevel.Debug).ToList();
        var infoEntries = _logger.GetLogEntries(LogLevel.Information).ToList();
        var errorEntries = _logger.GetLogEntries(LogLevel.Error).ToList();

        // Assert
        Assert.Single(debugEntries);
        Assert.Single(infoEntries);
        Assert.Single(errorEntries);
    }

    [Fact]
    public void InMemoryLogger_IsEnabled_AlwaysReturnsTrue()
    {
        // Act & Assert
        Assert.True(_logger.IsEnabled(LogLevel.Trace));
        Assert.True(_logger.IsEnabled(LogLevel.Debug));
        Assert.True(_logger.IsEnabled(LogLevel.Information));
        Assert.True(_logger.IsEnabled(LogLevel.Warning));
        Assert.True(_logger.IsEnabled(LogLevel.Error));
        Assert.True(_logger.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void InMemoryLogger_HasLogEntryEventId_WorksCorrectly()
    {
        // Arrange
        _logger.LogGetAsync("test-key");
        _logger.LogSetAsync("another-key");

        // Act & Assert
        Assert.True(_logger.HasLogEntry(new EventId(1))); // LogGetAsync
        Assert.True(_logger.HasLogEntry(new EventId(4))); // LogSetAsync
        Assert.False(_logger.HasLogEntry(new EventId(999))); // Non-existent
    }

    #endregion
}