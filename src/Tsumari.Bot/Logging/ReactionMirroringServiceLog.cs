using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class ReactionMirroringServiceLog
    {
        [LoggerMessage(
            EventId = 1300,
            Level = LogLevel.Warning,
            Message = "Reaction mirroring could not resolve channel {ChannelId} for message {MessageId}."
        )]
        public static partial void LogReactionChannelNotResolved(this ILogger logger, ulong channelId, ulong messageId);

        [LoggerMessage(
            EventId = 1301,
            Level = LogLevel.Warning,
            Message = "Reaction mirroring could not fetch message {MessageId} in channel {ChannelId}."
        )]
        public static partial void LogReactionMessageNotFetched(this ILogger logger, ulong messageId, ulong channelId);

        [LoggerMessage(
            EventId = 1302,
            Level = LogLevel.Error,
            Message = "Reaction mirroring failed while fetching message {MessageId} in channel {ChannelId}."
        )]
        public static partial void LogReactionMessageFetchFailed(this ILogger logger, Exception exception, ulong messageId, ulong channelId);

        [LoggerMessage(
            EventId = 1303,
            Level = LogLevel.Error,
            Message = "Reaction mirroring failed while reconciling emoji {Emoji} on message {MessageId} in channel {ChannelId}."
        )]
        public static partial void LogReactionReconcileFailed(this ILogger logger, Exception exception, string emoji, ulong messageId, ulong channelId);
    }
}
