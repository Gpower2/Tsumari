using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class GatewayEventGroupResolverLog
    {
        [LoggerMessage(
            EventId = 2500,
            Level = LogLevel.Warning,
            Message = "Received unsupported gateway event type {EventType} in the group resolver."
        )]
        public static partial void LogUnsupportedGatewayEventType(this ILogger logger, string eventType);
    }
}
