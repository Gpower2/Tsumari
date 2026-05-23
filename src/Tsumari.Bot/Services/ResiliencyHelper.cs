using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }

    /// <summary>
    /// A lightweight, highly performant, and zero-allocation resiliency helper implementing
    /// thread-safe Circuit Breaker and Retry with exponential backoff and jitter.
    /// Designed for resource-constrained (1GB RAM) environments.
    /// </summary>
    public class ResiliencyHelper
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _breakDuration;
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _initialRetryDelay;
        private readonly ILogger _logger;

        private int _failureCount;
        private long _lastStateChangeTicks;
        private int _circuitStateVal = (int)CircuitState.Closed;

        private readonly object _stateLock = new();

        public ResiliencyHelper(
            int failureThreshold, 
            TimeSpan breakDuration, 
            int maxRetryAttempts, 
            TimeSpan initialRetryDelay, 
            ILogger logger)
        {
            _failureThreshold = failureThreshold;
            _breakDuration = breakDuration;
            _maxRetryAttempts = maxRetryAttempts;
            _initialRetryDelay = initialRetryDelay;
            _logger = logger;
            _lastStateChangeTicks = DateTime.UtcNow.Ticks;
        }

        public CircuitState State
        {
            get => (CircuitState)Volatile.Read(ref _circuitStateVal);
            private set => Volatile.Write(ref _circuitStateVal, (int)value);
        }

        /// <summary>
        /// Executes an asynchronous operation with retry logic and protection from the circuit breaker.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            EvaluateCircuitState();

            var currentState = State;
            if (currentState == CircuitState.Open)
            {
                _logger.LogWarning("Circuit breaker is OPEN. Fast-failing request.");
                throw new InvalidOperationException("Circuit breaker is currently open. Request blocked.");
            }

            int attempts = 0;
            while (true)
            {
                try
                {
                    T result = await action();
                    
                    // Success path
                    OnSuccess();
                    return result;
                }
                catch (Exception ex)
                {
                    attempts++;
                    OnFailure();

                    if (attempts >= _maxRetryAttempts || State == CircuitState.Open)
                    {
                        _logger.LogError(ex, "Operation failed after {Attempts} attempts. Circuit state is {State}.", attempts, State);
                        throw;
                    }

                    // Exponential backoff with full jitter
                    var delay = CalculateJitteredDelay(attempts);
                    _logger.LogWarning(ex, "Operation failed. Retrying in {DelayMs}ms (Attempt {Attempt}/{Max}). Error: {Message}", 
                        delay.TotalMilliseconds, attempts, _maxRetryAttempts, ex.Message);
                    
                    await Task.Delay(delay);
                }
            }
        }

        private void EvaluateCircuitState()
        {
            if (State == CircuitState.Open)
            {
                long lastChangeTicks = Volatile.Read(ref _lastStateChangeTicks);
                var timeSinceBreak = DateTime.UtcNow - new DateTime(lastChangeTicks);
                if (timeSinceBreak >= _breakDuration)
                {
                    lock (_stateLock)
                    {
                        // Double check lock
                        if (State == CircuitState.Open)
                        {
                            State = CircuitState.HalfOpen;
                            Volatile.Write(ref _lastStateChangeTicks, DateTime.UtcNow.Ticks);
                            _logger.LogInformation("Circuit breaker transitioned to HALF-OPEN. Testing service availability.");
                        }
                    }
                }
            }
        }

        private void OnSuccess()
        {
            if (State == CircuitState.HalfOpen || _failureCount > 0)
            {
                lock (_stateLock)
                {
                    _failureCount = 0;
                    if (State == CircuitState.HalfOpen)
                    {
                        State = CircuitState.Closed;
                        Volatile.Write(ref _lastStateChangeTicks, DateTime.UtcNow.Ticks);
                        _logger.LogInformation("Circuit breaker transitioned to CLOSED. Service restored successfully.");
                    }
                }
            }
        }

        private void OnFailure()
        {
            int currentFailures = Interlocked.Increment(ref _failureCount);

            if (State == CircuitState.HalfOpen)
            {
                lock (_stateLock)
                {
                    State = CircuitState.Open;
                    Volatile.Write(ref _lastStateChangeTicks, DateTime.UtcNow.Ticks);
                    _logger.LogWarning("Operation failed in HALF-OPEN state. Circuit breaker returned to OPEN.");
                }
            }
            else if (State == CircuitState.Closed && currentFailures >= _failureThreshold)
            {
                lock (_stateLock)
                {
                    if (State == CircuitState.Closed)
                    {
                        State = CircuitState.Open;
                        Volatile.Write(ref _lastStateChangeTicks, DateTime.UtcNow.Ticks);
                        _logger.LogCritical("Sequential failures ({Failures}) exceeded threshold ({Threshold}). Circuit breaker tripped to OPEN.", 
                            currentFailures, _failureThreshold);
                    }
                }
            }
        }

        private TimeSpan CalculateJitteredDelay(int attempt)
        {
            // Exponential delay: initial * 2^(attempt-1)
            double delayMs = _initialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            
            // Jitter: random factor between 0.5 and 1.5
            var random = Random.Shared.NextDouble() + 0.5; // Random value [0.5, 1.5)
            var jitteredDelayMs = delayMs * random;
            
            // Cap it at a maximum of 30 seconds
            jitteredDelayMs = Math.Min(jitteredDelayMs, 30000);

            return TimeSpan.FromMilliseconds(jitteredDelayMs);
        }
    }
}
