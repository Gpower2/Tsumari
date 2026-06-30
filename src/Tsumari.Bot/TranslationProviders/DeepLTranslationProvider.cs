using System;
using System.Threading.Tasks;
using DeepL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.TranslationProviders
{
    public class DeepLTranslationProvider : ITranslationProvider
    {
        private const string FreeServerUrl = "https://api-free.deepl.com";
        private const string PaidServerUrl = "https://api.deepl.com";
        private readonly DeepLLanguageService _deepLLanguageService;
        private readonly ILogger<DeepLTranslationProvider> _logger;
        private readonly Translator? _translator;
        private readonly string? _configuredServerUrl;
        private readonly string _configuredPlan = "Not configured";

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
                _configuredPlan = "Not configured";
                _logger.LogApiKeyMissing();
                return;
            }

            try
            {
                var options = new TranslatorOptions();
                if (apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase))
                {
                    options.ServerUrl = FreeServerUrl;
                    _configuredPlan = "Free";
                    _logger.LogFreeApiRoutingSelected();
                }
                else
                {
                    options.ServerUrl = PaidServerUrl;
                    _configuredPlan = "Paid";
                    _logger.LogPaidApiRoutingSelected();
                }

                _configuredServerUrl = options.ServerUrl;
                _translator = new Translator(apiKey, options);
            }
            catch (Exception ex)
            {
                _logger.LogTranslatorInitializationFailed(ex);
            }
        }

        public bool IsActive => _translator != null;

        public bool UsesCharacterQuota => true;

        public TranslationProviderConfigurationReport GetConfigurationReport()
        {
            return new TranslationProviderConfigurationReport(
                "DeepL",
                GetType().Name,
                IsActive,
                UsesCharacterQuota,
                [
                    new("Plan", _configuredPlan),
                    new("Endpoint", _configuredServerUrl ?? "not configured"),
                    new("Capabilities", "Single-language detection and translation")
                ]);
        }

        public async Task<LanguageAnalysisResult> AnalyzeLanguageAsync(string text)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            var response = await _translator!.TranslateTextAsync(text, null, "EN-US");
            return LanguageAnalysisResult.SingleLanguage(
                response.DetectedSourceLanguageCode.ToUpperInvariant(),
                isMixed: null,
                hasClearDominantLanguage: null);
        }

        public async Task<string> TranslateTextAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            var normalizedTargetLanguageCode = await _deepLLanguageService.NormalizeTargetLanguageCodeAsync(targetLanguageCode);
            var normalizedSourceLanguageCode = _deepLLanguageService.NormalizeSourceLanguageCode(sourceLanguageCode);
            var response = await _translator!.TranslateTextAsync(
                text,
                string.IsNullOrWhiteSpace(normalizedSourceLanguageCode) ? null : normalizedSourceLanguageCode,
                normalizedTargetLanguageCode);
            return response.Text;
        }
    }
}
