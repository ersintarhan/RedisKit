using Microsoft.Extensions.Logging;

namespace RedisKit.Tests.Helpers;

/// <summary>
///     In-memory logger implementation for testing source-generated logging extensions
/// </summary>
public class InMemoryLogger : ILogger
{
    private readonly List<LogEntry> _logEntries = new();

    public IReadOnlyList<LogEntry> LogEntries => _logEntries.AsReadOnly();

    public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var entry = new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            State = state,
            Timestamp = DateTime.UtcNow
        };

        _logEntries.Add(entry);
    }

    public void Clear() => _logEntries.Clear();

    public bool HasLogEntry(LogLevel logLevel, string messageSubstring)
    {
        return _logEntries.Any(e => e.LogLevel == logLevel && e.Message.Contains(messageSubstring));
    }

    public bool HasLogEntry(EventId eventId)
    {
        return _logEntries.Any(e => e.EventId == eventId);
    }

    public LogEntry? GetLastLogEntry() => _logEntries.LastOrDefault();

    public IEnumerable<LogEntry> GetLogEntries(LogLevel logLevel) => _logEntries.Where(e => e.LogLevel == logLevel);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

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