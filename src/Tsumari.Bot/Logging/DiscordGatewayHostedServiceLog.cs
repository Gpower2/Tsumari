using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class DiscordGatewayHostedServiceLog
    {
        [LoggerMessage(
            EventId = 2300,
            Level = LogLevel.Information,
            Message = "Starting Tsumari Discord Gateway Hosted Service..."
        )]
        public static partial void LogStartingHostedService(this ILogger logger);

        [LoggerMessage(
            EventId = 2301,
            Level = LogLevel.Critical,
            Message = "Discord token is completely missing from configuration. Shutting down the hosted service."
        )]
        public static partial void LogMissingDiscordToken(this ILogger logger);

        [LoggerMessage(
            EventId = 2302,
            Level = LogLevel.Information,
            Message = "Discord gateway hosted service cancellation requested."
        )]
        public static partial void LogCancellationRequested(this ILogger logger);

        [LoggerMessage(
            EventId = 2303,
            Level = LogLevel.Critical,
            Message = "A fatal exception crashed the gateway client lifecycle."
        )]
        public static partial void LogGatewayLifecycleCrashed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2304,
            Message = "[Discord.Net] {Source}: {Message}"
        )]
        public static partial void LogDiscordNetMessage(this ILogger logger, LogLevel logLevel, string source, string message, Exception? exception);

        [LoggerMessage(
            EventId = 2305,
            Level = LogLevel.Information,
            Message = "Tsumari is connected to Discord Gateway as: {User}"
        )]
        public static partial void LogConnectedToGateway(this ILogger logger, string user);

        [LoggerMessage(
            EventId = 2306,
            Level = LogLevel.Information,
            Message = "Administrative slash commands registered globally."
        )]
        public static partial void LogAdministrativeCommandsRegistered(this ILogger logger);

        [LoggerMessage(
            EventId = 2307,
            Level = LogLevel.Error,
            Message = "Error occurred during hosted-service initialization on Ready event."
        )]
        public static partial void LogReadyInitializationFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2308,
            Level = LogLevel.Error,
            Message = "Slash command execution failed: {Reason}"
        )]
        public static partial void LogSlashCommandExecutionFailed(this ILogger logger, string reason);

        [LoggerMessage(
            EventId = 2309,
            Level = LogLevel.Error,
            Message = "Unhandled error handling edited message {MessageId}."
        )]
        public static partial void LogEditedMessageEventHandlingFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2310,
            Level = LogLevel.Error,
            Message = "Unhandled error routing received message {MessageId}."
        )]
        public static partial void LogMessageRoutingFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2311,
            Level = LogLevel.Error,
            Message = "Unhandled error synchronizing delete for message {MessageId}."
        )]
        public static partial void LogDeleteSynchronizationFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2312,
            Level = LogLevel.Error,
            Message = "Unhandled error synchronizing bulk delete in channel {ChannelId}."
        )]
        public static partial void LogBulkDeleteSynchronizationFailed(this ILogger logger, Exception exception, ulong channelId);

        [LoggerMessage(
            EventId = 2313,
            Level = LogLevel.Error,
            Message = "Unhandled error synchronizing edited message {MessageId}."
        )]
        public static partial void LogEditSynchronizationFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2314,
            Level = LogLevel.Error,
            Message = "Unhandled error mirroring added reaction for message {MessageId}."
        )]
        public static partial void LogReactionAddedMirroringFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2315,
            Level = LogLevel.Error,
            Message = "Unhandled error mirroring removed reaction for message {MessageId}."
        )]
        public static partial void LogReactionRemovedMirroringFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2316,
            Level = LogLevel.Error,
            Message = "Unhandled error mirroring cleared reactions for message {MessageId}."
        )]
        public static partial void LogReactionsClearedMirroringFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2317,
            Level = LogLevel.Error,
            Message = "Unhandled error mirroring removed-for-emote reactions for message {MessageId}."
        )]
        public static partial void LogReactionsRemovedForEmoteMirroringFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 2318,
            Level = LogLevel.Information,
            Message = "Disconnecting and disposing Discord client connection..."
        )]
        public static partial void LogDisconnectingClient(this ILogger logger);

        [LoggerMessage(
            EventId = 2319,
            Level = LogLevel.Warning,
            Message = "Discord client logout failed during shutdown."
        )]
        public static partial void LogClientLogoutFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2320,
            Level = LogLevel.Warning,
            Message = "Discord client stop failed during shutdown."
        )]
        public static partial void LogClientStopFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 2321,
            Level = LogLevel.Trace,
            Message = "Received Discord Ready event."
        )]
        public static partial void LogReadyEventReceived(this ILogger logger);

        [LoggerMessage(
            EventId = 2322,
            Level = LogLevel.Trace,
            Message = "Received Discord interaction event of type {InteractionType} in channel {ChannelId} from user {UserId}."
        )]
        public static partial void LogInteractionCreatedEventReceived(this ILogger logger, string interactionType, ulong? channelId, ulong userId);

        [LoggerMessage(
            EventId = 2323,
            Level = LogLevel.Trace,
            Message = "Received Discord message event for message {MessageId} in channel {ChannelId} with source {MessageSource}. Enqueued: {Enqueued}."
        )]
        public static partial void LogMessageReceivedEventReceived(this ILogger logger, ulong messageId, ulong channelId, MessageSource messageSource, bool enqueued);

        [LoggerMessage(
            EventId = 2324,
            Level = LogLevel.Trace,
            Message = "Received Discord message delete event for message {MessageId} in channel {ChannelId}. Enqueued: {Enqueued}."
        )]
        public static partial void LogMessageDeletedEventReceived(this ILogger logger, ulong messageId, ulong channelId, bool enqueued);

        [LoggerMessage(
            EventId = 2325,
            Level = LogLevel.Trace,
            Message = "Received Discord bulk delete event for {MessageCount} messages in channel {ChannelId}. Enqueued: {Enqueued}."
        )]
        public static partial void LogMessagesBulkDeletedEventReceived(this ILogger logger, int messageCount, ulong channelId, bool enqueued);

        [LoggerMessage(
            EventId = 2326,
            Level = LogLevel.Trace,
            Message = "Received Discord message update event for message {MessageId} in channel {ChannelId}. Cached before snapshot: {HadCachedSnapshot}. Enqueued: {Enqueued}."
        )]
        public static partial void LogMessageUpdatedEventReceived(this ILogger logger, ulong messageId, ulong channelId, bool hadCachedSnapshot, bool enqueued);

        [LoggerMessage(
            EventId = 2327,
            Level = LogLevel.Trace,
            Message = "Received Discord reaction added event for message {MessageId} in channel {ChannelId} from user {UserId} with emoji {Emoji} and type {ReactionType}. Enqueued: {Enqueued}."
        )]
        public static partial void LogReactionAddedEventReceived(this ILogger logger, ulong messageId, ulong channelId, ulong userId, string emoji, ReactionType reactionType, bool enqueued);

        [LoggerMessage(
            EventId = 2328,
            Level = LogLevel.Trace,
            Message = "Received Discord reaction removed event for message {MessageId} in channel {ChannelId} from user {UserId} with emoji {Emoji} and type {ReactionType}. Enqueued: {Enqueued}."
        )]
        public static partial void LogReactionRemovedEventReceived(this ILogger logger, ulong messageId, ulong channelId, ulong userId, string emoji, ReactionType reactionType, bool enqueued);

        [LoggerMessage(
            EventId = 2329,
            Level = LogLevel.Trace,
            Message = "Received Discord reactions cleared event for message {MessageId} in channel {ChannelId}. Enqueued: {Enqueued}."
        )]
        public static partial void LogReactionsClearedEventReceived(this ILogger logger, ulong messageId, ulong channelId, bool enqueued);

        [LoggerMessage(
            EventId = 2330,
            Level = LogLevel.Trace,
            Message = "Received Discord reactions removed-for-emote event for message {MessageId} in channel {ChannelId} with emoji {Emoji}. Enqueued: {Enqueued}."
        )]
        public static partial void LogReactionsRemovedForEmoteEventReceived(this ILogger logger, ulong messageId, ulong channelId, string emoji, bool enqueued);
    }
}
