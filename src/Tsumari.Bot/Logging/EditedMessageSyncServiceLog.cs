using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class EditedMessageSyncServiceLog
    {
        [LoggerMessage(
            EventId = 1200,
            Level = LogLevel.Error,
            Message = "Unhandled error handling edited message {MessageId}."
        )]
        public static partial void LogEditedMessageHandlingFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 1201,
            Level = LogLevel.Error,
            Message = "Failed to detect language for edited message {MessageId}."
        )]
        public static partial void LogEditedMessageLanguageDetectionFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 1202,
            Level = LogLevel.Error,
            Message = "Failed to retranslate edited message {MessageId} to {TargetLanguageCode}."
        )]
        public static partial void LogEditedMessageRetranslationFailed(this ILogger logger, Exception exception, ulong messageId, string targetLanguageCode);

        [LoggerMessage(
            EventId = 1203,
            Level = LogLevel.Warning,
            Message = "Could not fetch mirrored IUserMessage {MirroredMessageId} in channel {ChannelId} for edited message."
        )]
        public static partial void LogEditedMirroredMessageNotFetched(this ILogger logger, ulong mirroredMessageId, ulong channelId);

        [LoggerMessage(
            EventId = 1204,
            Level = LogLevel.Error,
            Message = "Error while updating mirrored message {MirroredMessageId} for edited original {OriginalMessageId}."
        )]
        public static partial void LogEditedMirroredMessageUpdateFailed(this ILogger logger, Exception exception, ulong mirroredMessageId, ulong originalMessageId);

        [LoggerMessage(
            EventId = 1205,
            Level = LogLevel.Error,
            Message = "Unhandled failure while processing edited message {MessageId}."
        )]
        public static partial void LogEditedMessageProcessingFailed(this ILogger logger, Exception exception, ulong messageId);
    }
}
