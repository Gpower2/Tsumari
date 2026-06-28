using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class DiscordMessagePublisherServiceLog
    {
        [LoggerMessage(
            EventId = 1500,
            Level = LogLevel.Error,
            Message = "CDN re-upload fail: Could not download {Filename} from {Url}"
        )]
        public static partial void LogAttachmentDownloadFailed(this ILogger logger, Exception exception, string filename, string url);

        [LoggerMessage(
            EventId = 1501,
            Level = LogLevel.Error,
            Message = "Failed to edit buttons into sent message {MessageId} in channel {ChannelId}."
        )]
        public static partial void LogJumpButtonEditFailed(this ILogger logger, Exception exception, ulong messageId, ulong channelId);

        [LoggerMessage(
            EventId = 1502,
            Level = LogLevel.Error,
            Message = "Failed to send files/message to channel {ChannelId}. Fallback to text."
        )]
        public static partial void LogSendWithFilesFailed(this ILogger logger, Exception exception, ulong channelId);

        [LoggerMessage(
            EventId = 1503,
            Level = LogLevel.Error,
            Message = "Fallback message transmission failed completely for channel {ChannelId}"
        )]
        public static partial void LogFallbackSendFailed(this ILogger logger, Exception exception, ulong channelId);

        [LoggerMessage(
            EventId = 1504,
            Level = LogLevel.Error,
            Message = "Failed to dispose MemoryStream in mirroring routine."
        )]
        public static partial void LogMemoryStreamDisposeFailed(this ILogger logger, Exception exception);
    }
}
