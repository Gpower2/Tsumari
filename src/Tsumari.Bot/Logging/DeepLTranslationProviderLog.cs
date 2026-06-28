using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class DeepLTranslationProviderLog
    {
        [LoggerMessage(
            EventId = 2000,
            Level = LogLevel.Critical,
            Message = "DeepL API Key is missing! Bot will not be able to perform translations with DeepL."
        )]
        public static partial void LogApiKeyMissing(this ILogger logger);

        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "DeepL Key verified with ':fx' suffix. Hardcoded routing to 'https://api-free.deepl.com'."
        )]
        public static partial void LogFreeApiRoutingSelected(this ILogger logger);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Information,
            Message = "DeepL Key parsed. Routing to 'https://api.deepl.com'."
        )]
        public static partial void LogPaidApiRoutingSelected(this ILogger logger);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Error,
            Message = "Failed to initialize DeepL Translator client."
        )]
        public static partial void LogTranslatorInitializationFailed(this ILogger logger, Exception exception);
    }
}
