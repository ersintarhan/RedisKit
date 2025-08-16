using Microsoft.Extensions.Logging;
using RedisKit.Models;

namespace RedisKit.Services
{
    /// <summary>
    /// Implements the Circuit Breaker pattern for Redis connections
    /// </summary>
    internal class RedisCircuitBreaker
    {
        private readonly ILogger<RedisCircuitBreaker> _logger;
        private readonly CircuitBreakerSettings _settings;
        private CircuitState _state = CircuitState.Closed;
        private int _failureCount = 0;
        private int _successCount = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private DateTime _openedAt = DateTime.MinValue;
        private readonly SemaphoreSlim _stateLock = new(1, 1);

        public RedisCircuitBreaker(ILogger<RedisCircuitBreaker> logger, CircuitBreakerSettings settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Gets the current circuit state
        /// </summary>
        public CircuitState State => _state;

        /// <summary>
        /// Checks if the circuit allows the operation to proceed
        /// </summary>
        public async Task<bool> CanExecuteAsync()
        {
            if (!_settings.Enabled)
                return true;

            await _stateLock.WaitAsync();
            try
            {
                return _state switch
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
        /// Records a successful operation
        /// </summary>
        public async Task RecordSuccessAsync()
        {
            if (!_settings.Enabled)
                return;

            await _stateLock.WaitAsync();
            try
            {
                switch (_state)
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
                        if (_failureCount > 0 && DateTime.UtcNow - _lastFailureTime > _settings.FailureWindow)
                        {
                            _failureCount = 0;
                        }
                        break;
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <summary>
        /// Records a failed operation
        /// </summary>
        public async Task RecordFailureAsync(Exception? exception = null)
        {
            if (!_settings.Enabled)
                return;

            await _stateLock.WaitAsync();
            try
            {
                _lastFailureTime = DateTime.UtcNow;

                switch (_state)
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

                if (exception != null)
                {
                    _logger.LogWarning(exception, "Circuit breaker recorded failure in {State} state", _state);
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <summary>
        /// Resets the circuit breaker to closed state
        /// </summary>
        public async Task ResetAsync()
        {
            await _stateLock.WaitAsync();
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
        /// Gets the current circuit breaker statistics
        /// </summary>
        public RedisCircuitBreakerStats GetStats()
        {
            return new RedisCircuitBreakerStats
            {
                State = _state,
                FailureCount = _failureCount,
                SuccessCount = _successCount,
                LastFailureTime = _lastFailureTime,
                OpenedAt = _openedAt,
                TimeUntilHalfOpen = GetTimeUntilHalfOpen()
            };
        }

        private Task<bool> CheckIfCanTransitionToHalfOpen()
        {
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
            if (_state != newState)
            {
                var oldState = _state;
                _state = newState;
                _logger.LogInformation("Circuit breaker transitioned from {OldState} to {NewState}", oldState, newState);
            }
        }

        private TimeSpan? GetTimeUntilHalfOpen()
        {
            if (_state != CircuitState.Open)
                return null;

            var elapsed = DateTime.UtcNow - _openedAt;
            var remaining = _settings.BreakDuration - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}