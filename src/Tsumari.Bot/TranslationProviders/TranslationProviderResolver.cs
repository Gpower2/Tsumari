using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.TranslationProviders
{
    public class TranslationProviderResolver
    {
        private readonly IConfiguration _configuration;
        private readonly DeepLTranslationProvider _deepLTranslationProvider;
        private readonly OllamaTranslationProvider _ollamaTranslationProvider;
        private readonly OpenAITranslationProvider _openAITranslationProvider;
        private readonly ILogger<TranslationProviderResolver> _logger;

        public TranslationProviderResolver(
            IConfiguration configuration,
            DeepLTranslationProvider deepLTranslationProvider,
            OllamaTranslationProvider ollamaTranslationProvider,
            OpenAITranslationProvider openAITranslationProvider,
            ILogger<TranslationProviderResolver> logger)
        {
            _configuration = configuration;
            _deepLTranslationProvider = deepLTranslationProvider;
            _ollamaTranslationProvider = ollamaTranslationProvider;
            _openAITranslationProvider = openAITranslationProvider;
            _logger = logger;
        }

        public ITranslationProvider Resolve()
        {
            var providerString = _configuration["Translation:Provider"];
            if (string.IsNullOrWhiteSpace(providerString))
            {
                return ResolveOllamaFallback("Translation:Provider is not configured.");
            }

            if (!Enum.TryParse<TranslationProvider>(providerString, true, out var parsedProvider))
            {
                return ResolveOllamaFallback($"Translation:Provider value '{providerString}' is invalid.");
            }

            ITranslationProvider provider = parsedProvider switch
            {
                TranslationProvider.Ollama => _ollamaTranslationProvider,
                TranslationProvider.OpenAI => _openAITranslationProvider,
                _ => _deepLTranslationProvider,
            };

            if (!provider.IsActive)
            {
                _logger.LogWarning(
                    "Translation provider {Provider} was selected but is not active. Translation features will remain unavailable until the provider is configured correctly.",
                    parsedProvider);
            }
            else
            {
                _logger.LogInformation("Translation provider {Provider} selected from configuration.", parsedProvider);
            }

            return provider;
        }

        private ITranslationProvider ResolveOllamaFallback(string reason)
        {
            if (_ollamaTranslationProvider.IsActive)
            {
                _logger.LogWarning(
                    "{Reason} Falling back to Ollama instead of defaulting to paid DeepL.",
                    reason);
            }
            else
            {
                _logger.LogWarning(
                    "{Reason} Falling back to Ollama instead of defaulting to paid DeepL, but the Ollama provider is not active so translation features will remain unavailable.",
                    reason);
            }

            return _ollamaTranslationProvider;
        }
    }
}
