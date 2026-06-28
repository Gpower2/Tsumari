using Microsoft.Extensions.Logging;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Logging
{
    public static partial class ResiliencyHelperLog
    {
        [LoggerMessage(
            EventId = 1700,
            Level = LogLevel.Warning,
            Message = "Circuit breaker is OPEN. Fast-failing request."
        )]
        public static partial void LogCircuitBreakerOpenFastFail(this ILogger logger);

        [LoggerMessage(
            EventId = 1701,
            Level = LogLevel.Error,
            Message = "Operation failed after {Attempts} attempts. Circuit state is {State}."
        )]
        public static partial void LogOperationFailedAfterAttempts(this ILogger logger, Exception exception, int attempts, CircuitState state);

        [LoggerMessage(
            EventId = 1702,
            Level = LogLevel.Warning,
            Message = "Operation failed. Retrying in {DelayMilliseconds}ms (Attempt {Attempt}/{MaxAttempts}). Error: {ErrorMessage}"
        )]
        public static partial void LogOperationRetrying(this ILogger logger, Exception exception, double delayMilliseconds, int attempt, int maxAttempts, string errorMessage);

        [LoggerMessage(
            EventId = 1703,
            Level = LogLevel.Information,
            Message = "Circuit breaker transitioned to HALF-OPEN. Testing service availability."
        )]
        public static partial void LogCircuitBreakerHalfOpen(this ILogger logger);

        [LoggerMessage(
            EventId = 1704,
            Level = LogLevel.Information,
            Message = "Circuit breaker transitioned to CLOSED. Service restored successfully."
        )]
        public static partial void LogCircuitBreakerClosed(this ILogger logger);

        [LoggerMessage(
            EventId = 1705,
            Level = LogLevel.Warning,
            Message = "Operation failed in HALF-OPEN state. Circuit breaker returned to OPEN."
        )]
        public static partial void LogCircuitBreakerReturnedToOpen(this ILogger logger);

        [LoggerMessage(
            EventId = 1706,
            Level = LogLevel.Critical,
            Message = "Sequential failures ({Failures}) exceeded threshold ({Threshold}). Circuit breaker tripped to OPEN."
        )]
        public static partial void LogCircuitBreakerTrippedOpen(this ILogger logger, int failures, int threshold);
    }
}
