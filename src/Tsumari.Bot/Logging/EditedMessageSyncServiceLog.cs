using Discord;
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
            Message = "Failed to analyze language for edited message {MessageId}."
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

        [LoggerMessage(
            EventId = 1206,
            Level = LogLevel.Debug,
            Message = "Skipping edited message {MessageId} because gateway payload type {MessageType} does not implement IUserMessage."
        )]
        public static partial void LogSkippingNonUserEditedMessage(this ILogger logger, ulong messageId, string messageType);

        [LoggerMessage(
            EventId = 1207,
            Level = LogLevel.Debug,
            Message = "Skipping edited message {MessageId} because it was authored by a bot or source {MessageSource} is not user-authored."
        )]
        public static partial void LogSkippingBotOrNonUserEdit(this ILogger logger, ulong messageId, MessageSource messageSource);

        [LoggerMessage(
            EventId = 1208,
            Level = LogLevel.Debug,
            Message = "Skipping edited message {MessageId} because the cached before/after content is unchanged."
        )]
        public static partial void LogSkippingUnchangedEditedMessage(this ILogger logger, ulong messageId);

        [LoggerMessage(
            EventId = 1209,
            Level = LogLevel.Debug,
            Message = "Skipping edited message {MessageId} because no mirrored messages are tracked for it."
        )]
        public static partial void LogSkippingEditedMessageWithoutMirrors(this ILogger logger, ulong messageId);

        [LoggerMessage(
            EventId = 1210,
            Level = LogLevel.Debug,
            Message = "Skipping mirrored edit publish for original message {OriginalMessageId} because destination channel {ChannelId} for mirrored message {MirroredMessageId} could not be resolved."
        )]
        public static partial void LogEditedMirroredChannelNotResolved(this ILogger logger, ulong originalMessageId, ulong channelId, ulong mirroredMessageId);
    }
}
