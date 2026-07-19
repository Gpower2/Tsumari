using System;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class HistoricalMessageSyncServiceLog
    {
        [LoggerMessage(
            EventId = 1910,
            Level = LogLevel.Information,
            Message = "Historical sync scanning channel {ChannelId} (master={IsMaster})."
        )]
        public static partial void LogHistoricalSyncScanningChannel(this ILogger logger, ulong channelId, bool isMaster);

        [LoggerMessage(
            EventId = 1911,
            Level = LogLevel.Warning,
            Message = "Historical sync could not resolve channel {ChannelId}."
        )]
        public static partial void LogHistoricalSyncChannelNotResolved(this ILogger logger, ulong channelId);

        [LoggerMessage(
            EventId = 1912,
            Level = LogLevel.Information,
            Message = "Historical sync found {CandidateCount} unprocessed messages for master {MasterChannelId} since {Cutoff}; processing chronologically."
        )]
        public static partial void LogHistoricalSyncProcessingStarted(this ILogger logger, int candidateCount, ulong masterChannelId, DateTimeOffset cutoff);

        [LoggerMessage(
            EventId = 1913,
            Level = LogLevel.Error,
            Message = "Historical sync failed to route message {MessageId}."
        )]
        public static partial void LogHistoricalSyncMessageFailed(this ILogger logger, Exception exception, ulong messageId);

        [LoggerMessage(
            EventId = 1914,
            Level = LogLevel.Information,
            Message = "Historical sync completed for master {MasterChannelId}. Processed: {ProcessedCount}, failed: {FailedCount}, skipped: {SkippedCount}."
        )]
        public static partial void LogHistoricalSyncCompleted(this ILogger logger, ulong masterChannelId, int processedCount, int failedCount, int skippedCount);
    }
}
