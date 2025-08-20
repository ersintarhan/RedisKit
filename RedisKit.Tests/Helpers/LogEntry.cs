using Microsoft.Extensions.Logging;

namespace RedisKit.Tests.Helpers;

/// <summary>
///     Represents a log entry captured by InMemoryLogger
/// </summary>
public class LogEntry
{
    public LogLevel LogLevel { get; init; }
    public EventId EventId { get; init; }
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public object? State { get; init; }
    public DateTime Timestamp { get; init; }
}