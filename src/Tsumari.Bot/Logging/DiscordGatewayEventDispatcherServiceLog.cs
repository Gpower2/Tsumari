using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class DiscordGatewayEventDispatcherServiceLog
    {
        [LoggerMessage(
            EventId = 2400,
            Level = LogLevel.Warning,
            Message = "Dropped gateway event {EventType} because the dispatcher is stopping."
        )]
        public static partial void LogGatewayEventDroppedDispatcherStopping(this ILogger logger, string eventType);

        [LoggerMessage(
            EventId = 2401,
            Level = LogLevel.Warning,
            Message = "Dropped gateway event {EventType} because the ingress queue is closed."
        )]
        public static partial void LogGatewayEventDroppedQueueClosed(this ILogger logger, string eventType);

        [LoggerMessage(
            EventId = 2402,
            Level = LogLevel.Error,
            Message = "Failed to resolve dispatch group for gateway event {EventType}."
        )]
        public static partial void LogGatewayEventGroupResolutionFailed(this ILogger logger, Exception exception, string eventType);

        [LoggerMessage(
            EventId = 2403,
            Level = LogLevel.Warning,
            Message = "Write into group queue {GroupKey} was rejected for gateway event {EventType}."
        )]
        public static partial void LogGroupQueueWriteRejected(this ILogger logger, ulong groupKey, string eventType);

        [LoggerMessage(
            EventId = 2404,
            Level = LogLevel.Error,
            Message = "Gateway event router loop crashed unexpectedly."
        )]
        public static partial void LogGatewayEventRouterLoopFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2405,
            Level = LogLevel.Error,
            Message = "Gateway group worker for group {GroupKey} crashed while processing {EventType}."
        )]
        public static partial void LogGatewayEventGroupWorkerFailed(this ILogger logger, Exception exception, ulong groupKey, string eventType);
    }
}
