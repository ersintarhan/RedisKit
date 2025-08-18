using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Interfaces;
using RedisKit.Logging;
using RedisKit.Models;

namespace RedisKit.Services;

/// <summary>
///     Implements the Circuit Breaker pattern for Redis connections
/// </summary>
internal class RedisCircuitBreaker : IRedisCircuitBreaker
{
    private readonly ILogger<RedisCircuitBreaker> _logger;
    private readonly CircuitBreakerSettings _settings;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private int _failureCount;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private DateTime _openedAt = DateTime.MinValue;
    private int _successCount;

    public RedisCircuitBreaker(ILogger<RedisCircuitBreaker> logger, IOptions<CircuitBreakerSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    ///     Gets the current circuit state
    /// </summary>
    public CircuitState State { get; private set; } = CircuitState.Closed;

    /// <summary>
    ///     Checks if the circuit allows the operation to proceed
    /// </summary>
    public async Task<bool> CanExecuteAsync()
    {
        if (!_settings.Enabled)
            return true;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return State switch
            {
                CircuitState.Closed => true,
                CircuitState.Open => await CheckIfCanTransitionToHalfOpen(),
                CircuitState.HalfOpen => true,
                _ => false
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    ///     Records a successful operation
    /// </summary>
    public async Task RecordSuccessAsync()
    {
        if (!_settings.Enabled)
            return;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            switch (State)
            {
                case CircuitState.HalfOpen:
                    _successCount++;
                    if (_successCount >= _settings.SuccessThreshold)
                    {
                        TransitionTo(CircuitState.Closed);
                        _failureCount = 0;
                        _successCount = 0;
                    }

                    break;

                case CircuitState.Closed:
                    // Reset failure count on success in closed state
                    if (_failureCount > 0 && DateTime.UtcNow - _lastFailureTime > _settings.FailureWindow) _failureCount = 0;
                    break;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    ///     Records a failed operation
    /// </summary>
    public async Task RecordFailureAsync(Exception? exception = null)
    {
        if (!_settings.Enabled)
            return;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _lastFailureTime = DateTime.UtcNow;

            switch (State)
            {
                case CircuitState.Closed:
                    _failureCount++;
                    if (_failureCount >= _settings.FailureThreshold)
                    {
                        TransitionTo(CircuitState.Open);
                        _openedAt = DateTime.UtcNow;
                    }

                    break;

                case CircuitState.HalfOpen:
                    // Single failure in half-open state reopens the circuit
                    TransitionTo(CircuitState.Open);
                    _openedAt = DateTime.UtcNow;
                    _successCount = 0;
                    break;
            }

            if (exception != null) _logger.LogCircuitBreakerFailure(State, exception);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    ///     Resets the circuit breaker to closed state
    /// </summary>
    public async Task ResetAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            TransitionTo(CircuitState.Closed);
            _failureCount = 0;
            _successCount = 0;
            _lastFailureTime = DateTime.MinValue;
            _openedAt = DateTime.MinValue;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    ///     Gets the current circuit breaker statistics
    /// </summary>
    public RedisCircuitBreakerStats GetStats()
    {
        return new RedisCircuitBreakerStats
        {
            State = State,
            FailureCount = _failureCount,
            SuccessCount = _successCount,
            LastFailureTime = _lastFailureTime,
            OpenedAt = _openedAt,
            TimeUntilHalfOpen = GetTimeUntilHalfOpen()
        };
    }

    /// <summary>
    ///     Manually opens the circuit breaker
    /// </summary>
    public async Task OpenAsync()
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            TransitionTo(CircuitState.Open);
            _openedAt = DateTime.UtcNow;
            _failureCount = _settings.FailureThreshold; // Set to threshold to keep it open
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    ///     Gets the time when the circuit breaker will attempt to transition from Open to HalfOpen
    /// </summary>
    public DateTime? GetNextRetryTime()
    {
        if (State != CircuitState.Open)
            return null;

        return _openedAt.Add(_settings.BreakDuration);
    }

    private Task<bool> CheckIfCanTransitionToHalfOpen()
    {
        // Check if circuit was never opened (defensive check)
        if (_openedAt == DateTime.MinValue)
            return Task.FromResult(false);

        if (DateTime.UtcNow - _openedAt >= _settings.BreakDuration)
        {
            TransitionTo(CircuitState.HalfOpen);
            _successCount = 0;
            return Task.FromResult(true); // Synchronously return true to allow operation
        }

        return Task.FromResult(false);
    }

    private void TransitionTo(CircuitState newState)
    {
        if (State != newState)
        {
            var oldState = State;
            State = newState;
            _logger.LogCircuitBreakerTransition(oldState, newState);
        }
    }

    private TimeSpan? GetTimeUntilHalfOpen()
    {
        if (State != CircuitState.Open)
            return null;

        var elapsed = DateTime.UtcNow - _openedAt;
        var remaining = _settings.BreakDuration - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}