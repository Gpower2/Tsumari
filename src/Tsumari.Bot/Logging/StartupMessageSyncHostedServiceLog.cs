using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class StartupMessageSyncHostedServiceLog
    {
        [LoggerMessage(
            EventId = 2210,
            Level = LogLevel.Information,
            Message = "Discord client ready; running startup message sync.")]
        public static partial void LogStartupSyncHostedServiceReady(this ILogger logger);

        [LoggerMessage(
            EventId = 2211,
            Level = LogLevel.Error,
            Message = "Startup message sync failed with an unhandled exception.")]
        public static partial void LogStartupSyncHostedServiceFailed(this ILogger logger, Exception exception);
    }
}
