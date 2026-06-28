using Discord;
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

        [LoggerMessage(
            EventId = 1304,
            Level = LogLevel.Debug,
            Message = "Skipping reaction event for message {MessageId} in channel {ChannelId} from user {UserId} because emoji {Emoji} with reaction type {ReactionType} is ignored."
        )]
        public static partial void LogReactionEventIgnored(this ILogger logger, ulong messageId, ulong channelId, ulong userId, string emoji, ReactionType reactionType);

        [LoggerMessage(
            EventId = 1305,
            Level = LogLevel.Debug,
            Message = "Skipping reaction reconciliation for message {MessageId} in channel {ChannelId} because no linked message family was found."
        )]
        public static partial void LogReactionFamilyNotFound(this ILogger logger, ulong messageId, ulong channelId);

        [LoggerMessage(
            EventId = 1306,
            Level = LogLevel.Debug,
            Message = "Skipping reaction reconciliation for original message {MessageId} in channel {ChannelId} because no linked messages could be fetched from Discord."
        )]
        public static partial void LogReactionFamilyMessagesNotFetched(this ILogger logger, ulong messageId, ulong channelId);

        [LoggerMessage(
            EventId = 1307,
            Level = LogLevel.Information,
            Message = "Processing added reaction for message {MessageId} in channel {ChannelId} from user {UserId} with emoji {Emoji} and type {ReactionType}."
        )]
        public static partial void LogProcessingReactionAdded(this ILogger logger, ulong messageId, ulong channelId, ulong userId, string emoji, ReactionType reactionType);

        [LoggerMessage(
            EventId = 1308,
            Level = LogLevel.Information,
            Message = "Processing removed reaction for message {MessageId} in channel {ChannelId} from user {UserId} with emoji {Emoji} and type {ReactionType}."
        )]
        public static partial void LogProcessingReactionRemoved(this ILogger logger, ulong messageId, ulong channelId, ulong userId, string emoji, ReactionType reactionType);

        [LoggerMessage(
            EventId = 1309,
            Level = LogLevel.Information,
            Message = "Processing cleared reactions for message {MessageId} in channel {ChannelId}."
        )]
        public static partial void LogProcessingReactionsCleared(this ILogger logger, ulong messageId, ulong channelId);

        [LoggerMessage(
            EventId = 1310,
            Level = LogLevel.Information,
            Message = "Processing removed-for-emote reactions for message {MessageId} in channel {ChannelId} with emoji {Emoji}."
        )]
        public static partial void LogProcessingReactionsRemovedForEmote(this ILogger logger, ulong messageId, ulong channelId, string emoji);
    }
}
