using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class TranslationServiceLog
    {
        [LoggerMessage(
            EventId = 1400,
            Level = LogLevel.Information,
            Message = "Translation Service configured to use provider implementation: {ProviderType}"
        )]
        public static partial void LogProviderImplementationConfigured(this ILogger logger, string providerType);

        [LoggerMessage(
            EventId = 1401,
            Level = LogLevel.Warning,
            Message = "Detection request blocked! Monthly translation limit of {MonthlyCharacterLimit} characters exceeded."
        )]
        public static partial void LogDetectionRequestBlockedByQuota(this ILogger logger, int monthlyCharacterLimit);

        [LoggerMessage(
            EventId = 1402,
            Level = LogLevel.Information,
            Message = "Language detected: '{LanguageCode}' for text prefix '{TextPrefix}'"
        )]
        public static partial void LogLanguageDetected(this ILogger logger, string languageCode, string textPrefix);

        [LoggerMessage(
            EventId = 1403,
            Level = LogLevel.Warning,
            Message = "Translation request blocked! Monthly translation limit of {MonthlyCharacterLimit} characters exceeded."
        )]
        public static partial void LogTranslationRequestBlockedByQuota(this ILogger logger, int monthlyCharacterLimit);
    }
}
