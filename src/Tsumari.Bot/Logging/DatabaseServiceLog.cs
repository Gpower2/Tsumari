using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class DatabaseServiceLog
    {
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Information,
            Message = "Initializing database schemas..."
        )]
        public static partial void LogInitializingDatabaseSchemas(this ILogger logger);

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Database tables initialized successfully."
        )]
        public static partial void LogDatabaseTablesInitialized(this ILogger logger);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Critical,
            Message = "Failed to initialize database tables."
        )]
        public static partial void LogDatabaseTablesInitializationFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Information,
            Message = "Registering master channel: {ChannelId}"
        )]
        public static partial void LogRegisteringMasterChannel(this ILogger logger, ulong channelId);

        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Information,
            Message = "Registering local channel: {LocalChannelId} for master: {ParentMasterChannelId} in language: {TargetLanguageCode}"
        )]
        public static partial void LogRegisteringLocalChannel(this ILogger logger, ulong localChannelId, ulong parentMasterChannelId, string targetLanguageCode);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Information,
            Message = "Attempting to unregister channel {ChannelId}"
        )]
        public static partial void LogAttemptingToUnregisterChannel(this ILogger logger, ulong channelId);

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Information,
            Message = "Unregistered channel {ChannelId}. Rows affected: {RowsDeleted}"
        )]
        public static partial void LogChannelUnregistered(this ILogger logger, ulong channelId, int rowsDeleted);

        [LoggerMessage(
            EventId = 1007,
            Level = LogLevel.Error,
            Message = "Failed to unregister channel {ChannelId}"
        )]
        public static partial void LogChannelUnregisterFailed(this ILogger logger, Exception exception, ulong channelId);

        [LoggerMessage(
            EventId = 1008,
            Level = LogLevel.Information,
            Message = "Incremented monthly translation usage by {CharacterCount} characters."
        )]
        public static partial void LogTranslationUsageIncremented(this ILogger logger, int characterCount);
    }
}
