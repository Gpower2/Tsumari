using Microsoft.Extensions.Logging;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.Logging
{
    public static partial class TranslationProviderResolverLog
    {
        [LoggerMessage(
            EventId = 1900,
            Level = LogLevel.Warning,
            Message = "Translation provider {Provider} was selected but is not active. Translation features will remain unavailable until the provider is configured correctly."
        )]
        public static partial void LogSelectedProviderInactive(this ILogger logger, TranslationProvider provider);

        [LoggerMessage(
            EventId = 1901,
            Level = LogLevel.Information,
            Message = "Translation provider {Provider} selected from configuration."
        )]
        public static partial void LogSelectedProvider(this ILogger logger, TranslationProvider provider);

        [LoggerMessage(
            EventId = 1902,
            Level = LogLevel.Warning,
            Message = "{Reason} Falling back to Ollama instead of defaulting to paid DeepL."
        )]
        public static partial void LogFallingBackToOllama(this ILogger logger, string reason);

        [LoggerMessage(
            EventId = 1903,
            Level = LogLevel.Warning,
            Message = "{Reason} Falling back to Ollama instead of defaulting to paid DeepL, but the Ollama provider is not active so translation features will remain unavailable."
        )]
        public static partial void LogFallingBackToInactiveOllama(this ILogger logger, string reason);
    }
}
