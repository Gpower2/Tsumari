using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class StartupMessageSyncServiceLog
    {
        [LoggerMessage(
            EventId = 2110,
            Level = LogLevel.Information,
            Message = "Startup sync has no baseline for master channel {MasterChannelId}; skipping.")]
        public static partial void LogStartupSyncNoBaseline(this ILogger logger, ulong masterChannelId);

        [LoggerMessage(
            EventId = 2111,
            Level = LogLevel.Information,
            Message = "Startup sync found no missed messages for master channel {MasterChannelId}.")]
        public static partial void LogStartupSyncNoMissedMessages(this ILogger logger, ulong masterChannelId);

        [LoggerMessage(
            EventId = 2112,
            Level = LogLevel.Error,
            Message = "Startup sync failed for master channel {MasterChannelId}: {ErrorMessage}")]
        public static partial void LogStartupSyncChannelFailed(this ILogger logger, ulong masterChannelId, string errorMessage);

        [LoggerMessage(
            EventId = 2113,
            Level = LogLevel.Warning,
            Message = "Startup sync announcement could not be posted to master channel {MasterChannelId}.")]
        public static partial void LogStartupSyncAnnouncementFailed(this ILogger logger, ulong masterChannelId);

        [LoggerMessage(
            EventId = 2114,
            Level = LogLevel.Warning,
            Message = "Startup sync completion message could not be posted to master channel {MasterChannelId}.")]
        public static partial void LogStartupSyncCompletionFailed(this ILogger logger, ulong masterChannelId);

        [LoggerMessage(
            EventId = 2115,
            Level = LogLevel.Information,
            Message = "Startup sync completed. Channels checked: {ChannelsChecked}, channels synced: {ChannelsSynced}, processed: {ProcessedCount}, failed: {FailedCount}, skipped: {SkippedCount}.")]
        public static partial void LogStartupSyncCompleted(this ILogger logger, int channelsChecked, int channelsSynced, int processedCount, int failedCount, int skippedCount);

        [LoggerMessage(
            EventId = 2116,
            Level = LogLevel.Warning,
            Message = "Failed to resolve timestamp from Discord for message {MessageId} in channel {ChannelId}; falling back to snowflake timestamp.")]
        public static partial void LogStartupSyncTimestampResolveFailed(this ILogger logger, ulong channelId, ulong messageId, Exception exception);

        [LoggerMessage(
            EventId = 2117,
            Level = LogLevel.Error,
            Message = "Startup sync encountered an unexpected error for master channel {MasterChannelId}.")]
        public static partial void LogStartupSyncChannelException(this ILogger logger, ulong masterChannelId, Exception exception);
    }
}
