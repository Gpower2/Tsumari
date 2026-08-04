using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class DiscordRestRetryPolicyLog
    {
        [LoggerMessage(
            EventId = 2600,
            Level = LogLevel.Warning,
            Message = "Discord REST operation '{OperationName}' timed out on attempt {Attempt} of {MaxRetries}. Retrying..."
        )]
        public static partial void LogDiscordRestTimeout(this ILogger logger, Exception exception, string operationName, int attempt, int maxRetries);

        [LoggerMessage(
            EventId = 2601,
            Level = LogLevel.Warning,
            Message = "Discord REST operation '{OperationName}' failed with retryable error on attempt {Attempt} of {MaxRetries} (HTTP {HttpCode}). Retrying..."
        )]
        public static partial void LogDiscordRestRetryableFailure(this ILogger logger, Exception exception, string operationName, int attempt, int maxRetries, int? httpCode);

        [LoggerMessage(
            EventId = 2602,
            Level = LogLevel.Warning,
            Message = "Retrying Discord REST operation '{OperationName}' (attempt {Attempt} of {MaxRetries})..."
        )]
        public static partial void LogDiscordRestRetry(this ILogger logger, string operationName, int attempt, int maxRetries);

        [LoggerMessage(
            EventId = 2603,
            Level = LogLevel.Error,
            Message = "Discord REST operation '{OperationName}' failed after {MaxRetries} retries."
        )]
        public static partial void LogDiscordRestOperationFailed(this ILogger logger, Exception exception, string operationName, int maxRetries);
    }
}
