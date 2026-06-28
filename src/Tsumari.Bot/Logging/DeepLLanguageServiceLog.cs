using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Logging
{
    public static partial class DeepLLanguageServiceLog
    {
        [LoggerMessage(
            EventId = 2100,
            Level = LogLevel.Warning,
            Message = "Failed to retrieve supported DeepL target language codes. Falling back to legacy mappings."
        )]
        public static partial void LogSupportedTargetLanguagesLoadFailed(this ILogger logger, Exception exception);
    }
}
