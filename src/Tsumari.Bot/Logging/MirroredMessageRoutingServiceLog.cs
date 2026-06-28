using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class MirroredMessageRoutingServiceLog
    {
        [LoggerMessage(
            EventId = 1100,
            Level = LogLevel.Error,
            Message = "Unhandled error inside routing pipeline for message {MessageId}."
        )]
        public static partial void LogRoutingPipelineFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 1101,
            Level = LogLevel.Information,
            Message = "Processing message {MessageId} in channel {ChannelName} (Master: {IsMaster}, Local: {IsLocalized})"
        )]
        public static partial void LogProcessingMessage(this ILogger logger, ulong messageId, string channelName, bool isMaster, bool isLocalized);

        [LoggerMessage(
            EventId = 1102,
            Level = LogLevel.Warning,
            Message = "Translation service is currently inactive. Outbound routing aborted."
        )]
        public static partial void LogTranslationServiceInactive(this ILogger logger);

        [LoggerMessage(
            EventId = 1103,
            Level = LogLevel.Error,
            Message = "Failed to run language detection. Fallback to EN."
        )]
        public static partial void LogLanguageDetectionFailedFallbackToEnglish(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 1104,
            Level = LogLevel.Error,
            Message = "Failed to translate message {MessageId} to {TargetLanguageCode}. Forwarding raw."
        )]
        public static partial void LogMasterTranslationFailedForwardingRaw(this ILogger logger, Exception exception, ulong messageId, string targetLanguageCode);

        [LoggerMessage(
            EventId = 1105,
            Level = LogLevel.Error,
            Message = "Channel {ChannelId} has incomplete configuration in localized table."
        )]
        public static partial void LogLocalizedChannelConfigurationIncomplete(this ILogger logger, ulong channelId);

        [LoggerMessage(
            EventId = 1106,
            Level = LogLevel.Error,
            Message = "Match flow failed to translate to sibling {TargetLanguageCode}."
        )]
        public static partial void LogMatchFlowSiblingTranslationFailed(this ILogger logger, Exception exception, string targetLanguageCode);

        [LoggerMessage(
            EventId = 1107,
            Level = LogLevel.Error,
            Message = "Mismatch flow failed native translation reply in channel {ChannelId}."
        )]
        public static partial void LogMismatchFlowNativeReplyTranslationFailed(this ILogger logger, Exception exception, ulong channelId);

        [LoggerMessage(
            EventId = 1108,
            Level = LogLevel.Error,
            Message = "Mismatch flow failed sibling translation to {TargetLanguageCode}."
        )]
        public static partial void LogMismatchFlowSiblingTranslationFailed(this ILogger logger, Exception exception, string targetLanguageCode);
    }
}
