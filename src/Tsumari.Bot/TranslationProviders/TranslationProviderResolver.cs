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

            if (!Enum.IsDefined(parsedProvider))
            {
                return ResolveOllamaFallback($"Translation:Provider value '{providerString}' does not map to a supported provider.");
            }

            ITranslationProvider provider = parsedProvider switch
            {
                TranslationProvider.Ollama => _ollamaTranslationProvider,
                TranslationProvider.OpenAI => _openAITranslationProvider,
                _ => _deepLTranslationProvider,
            };

            if (!provider.IsActive)
            {
                _logger.LogSelectedProviderInactive(parsedProvider);
            }
            else
            {
                _logger.LogSelectedProvider(parsedProvider);
            }

            return provider;
        }

        private ITranslationProvider ResolveOllamaFallback(string reason)
        {
            if (_ollamaTranslationProvider.IsActive)
            {
                _logger.LogFallingBackToOllama(reason);
            }
            else
            {
                _logger.LogFallingBackToInactiveOllama(reason);
            }

            return _ollamaTranslationProvider;
        }
    }
}
