using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class InteractionModuleLog
    {
        [LoggerMessage(
            EventId = 1800,
            Level = LogLevel.Information,
            Message = "Channel {ChannelName} ({ChannelId}) registered as a Master channel by {Username}."
        )]
        public static partial void LogMasterChannelRegistered(this ILogger logger, string channelName, ulong channelId, string username);

        [LoggerMessage(
            EventId = 1801,
            Level = LogLevel.Error,
            Message = "Error registering master channel {ChannelId}."
        )]
        public static partial void LogMasterChannelRegistrationFailed(this ILogger logger, Exception exception, ulong channelId);

        [LoggerMessage(
            EventId = 1802,
            Level = LogLevel.Information,
            Message = "Local channel {LocalChannelName} registered to Master {MasterChannelName} with language {LanguageCode} by {Username}."
        )]
        public static partial void LogLocalizedChannelRegistered(this ILogger logger, string localChannelName, string masterChannelName, string languageCode, string username);

        [LoggerMessage(
            EventId = 1803,
            Level = LogLevel.Error,
            Message = "Error registering localized channel {LocalChannelId} for master {MasterChannelId}."
        )]
        public static partial void LogLocalizedChannelRegistrationFailed(this ILogger logger, Exception exception, ulong localChannelId, ulong masterChannelId);

        [LoggerMessage(
            EventId = 1804,
            Level = LogLevel.Information,
            Message = "Unregistered channel {ChannelId} by user {Username}."
        )]
        public static partial void LogChannelUnregisteredByUser(this ILogger logger, ulong channelId, string username);

        [LoggerMessage(
            EventId = 1805,
            Level = LogLevel.Error,
            Message = "Error unregistering channel {ChannelId}."
        )]
        public static partial void LogChannelUnregisterByUserFailed(this ILogger logger, Exception exception, ulong channelId);
    }
}
