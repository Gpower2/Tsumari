using System;
using System.Threading.Tasks;
using DeepL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.TranslationProviders
{
    public class DeepLTranslationProvider : ITranslationProvider
    {
        private readonly DeepLLanguageService _deepLLanguageService;
        private readonly ILogger<DeepLTranslationProvider> _logger;
        private readonly Translator? _translator;

        public DeepLTranslationProvider(
            IConfiguration configuration,
            DeepLLanguageService deepLLanguageService,
            ILogger<DeepLTranslationProvider> logger)
        {
            _deepLLanguageService = deepLLanguageService;
            _logger = logger;

            var apiKey = configuration["DeepL:ApiKey"] ?? configuration["DeepLKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogApiKeyMissing();
                return;
            }

            try
            {
                var options = new TranslatorOptions();
                if (apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase))
                {
                    options.ServerUrl = "https://api-free.deepl.com";
                    _logger.LogFreeApiRoutingSelected();
                }
                else
                {
                    options.ServerUrl = "https://api.deepl.com";
                    _logger.LogPaidApiRoutingSelected();
                }

                _translator = new Translator(apiKey, options);
            }
            catch (Exception ex)
            {
                _logger.LogTranslatorInitializationFailed(ex);
            }
        }

        public bool IsActive => _translator != null;

        public bool UsesCharacterQuota => true;

        public async Task<string> DetectLanguageAsync(string text)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            var response = await _translator!.TranslateTextAsync(text, null, "EN-US");
            return response.DetectedSourceLanguageCode.ToUpperInvariant();
        }

        public async Task<string> TranslateTextAsync(string text, string targetLanguageCode)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            var normalizedTargetLanguageCode = await _deepLLanguageService.NormalizeTargetLanguageCodeAsync(targetLanguageCode);
            var response = await _translator!.TranslateTextAsync(text, null, normalizedTargetLanguageCode);
            return response.Text;
        }
    }
}
