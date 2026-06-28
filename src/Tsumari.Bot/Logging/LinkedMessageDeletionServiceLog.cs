using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class LinkedMessageDeletionServiceLog
    {
        [LoggerMessage(
            EventId = 1600,
            Level = LogLevel.Warning,
            Message = "Delete sync could not resolve linked message {MirroredMessageId} in channel {ChannelId} while processing original delete {OriginalMessageId}."
        )]
        public static partial void LogLinkedMessageNotResolvedDuringDelete(this ILogger logger, ulong mirroredMessageId, ulong channelId, ulong originalMessageId);

        [LoggerMessage(
            EventId = 1601,
            Level = LogLevel.Error,
            Message = "Delete sync failed while deleting linked message {MirroredMessageId} in channel {ChannelId} for original message {OriginalMessageId}."
        )]
        public static partial void LogLinkedMessageDeleteFailed(this ILogger logger, Exception exception, ulong mirroredMessageId, ulong channelId, ulong originalMessageId);

        [LoggerMessage(
            EventId = 1602,
            Level = LogLevel.Debug,
            Message = "Delete event for message {MessageId} has no original-family mirrors; any mirrored-link row will be removed and fan-out is skipped."
        )]
        public static partial void LogDeleteMessageSkippedWithoutMirrors(this ILogger logger, ulong messageId);
    }
}
