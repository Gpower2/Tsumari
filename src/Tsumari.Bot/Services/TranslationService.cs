using Microsoft.Extensions.Logging;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.Services
{
    public class TranslationService
    {
        private const int MaxLoggedTextPreviewLength = 15;
        private readonly DatabaseService _dbService;
        private readonly ITranslationProvider _translationProvider;
        private readonly ILogger<TranslationService> _logger;
        private readonly ResiliencyHelper _analysisResiliencyHelper;
        private readonly ResiliencyHelper _translationResiliencyHelper;

        public const int MonthlyCharacterLimit = 500000;

        public TranslationService(
            DatabaseService dbService,
            ITranslationProvider translationProvider,
            ILogger<TranslationService> logger,
            ILoggerFactory loggerFactory)
        {
            _dbService = dbService;
            _translationProvider = translationProvider;
            _logger = logger;

            _analysisResiliencyHelper = CreateResiliencyHelper(loggerFactory, "LanguageAnalysis");
            _translationResiliencyHelper = CreateResiliencyHelper(loggerFactory, "Translation");

            _logger.LogProviderImplementationConfigured(_translationProvider.GetType().Name);
        }

        public bool IsActive => _translationProvider.IsActive;

        public bool UsesCharacterQuota => _translationProvider.UsesCharacterQuota;

        public TranslationProviderConfigurationReport GetProviderConfigurationReport()
        {
            return _translationProvider.GetConfigurationReport();
        }

        public void LogProviderConfiguration()
        {
            var report = GetProviderConfigurationReport();
            _logger.LogProviderConfigurationReport(
                report.ProviderName,
                report.ImplementationName,
                report.IsActive,
                report.UsesCharacterQuota,
                FormatProviderDetails(report.Details));
        }

        public async Task<bool> CanTranslateAsync(int characterCount)
        {
            if (_translationProvider.UsesCharacterQuota)
            {
                var currentUsage = await _dbService.GetCurrentMonthUsageAsync();
                return (currentUsage + characterCount) <= MonthlyCharacterLimit;
            }

            return true;
        }

        public async Task IncrementTranslationUsageAsync(int charCount)
        {
            if (_translationProvider.UsesCharacterQuota)
            {
                await _dbService.IncrementUsageAsync(charCount);
            }
        }

        public async Task<LanguageAnalysisResult> AnalyzeLanguageAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return LanguageAnalysisResult.SingleLanguage("EN");
            }

            if (!IsActive)
            {
                throw new InvalidOperationException("TranslationService is not active.");
            }

            int charCount = text.Length;
            if (!await CanTranslateAsync(charCount))
            {
                _logger.LogDetectionRequestBlockedByQuota(MonthlyCharacterLimit);
                throw new InvalidOperationException("Monthly translation quota limit reached.");
            }

            var analysis = await _analysisResiliencyHelper.ExecuteAsync(() => _translationProvider.AnalyzeLanguageAsync(text));
            await IncrementTranslationUsageAsync(charCount);

            _logger.LogLanguageAnalyzed(
                analysis.PrimaryLanguageCode,
                string.Join(", ", analysis.DetectedLanguages.Select(language => language.LanguageCode)),
                analysis.IsMixed,
                analysis.HasClearDominantLanguage,
                CreateTextPreview(text));

            return analysis;
        }

        public async Task<string> TranslateTextAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (!IsActive)
            {
                throw new InvalidOperationException("TranslationService is not active.");
            }

            int charCount = text.Length;
            if (!await CanTranslateAsync(charCount))
            {
                _logger.LogTranslationRequestBlockedByQuota(MonthlyCharacterLimit);
                throw new InvalidOperationException("Monthly translation quota limit reached.");
            }

            string translatedResult = await _translationResiliencyHelper.ExecuteAsync(() => _translationProvider.TranslateTextAsync(text, targetLanguageCode, sourceLanguageCode));
            await IncrementTranslationUsageAsync(charCount);

            return translatedResult;
        }

        private static ResiliencyHelper CreateResiliencyHelper(ILoggerFactory loggerFactory, string operationName)
        {
            return new ResiliencyHelper(
                // Keep retries conservative: provider calls are user-facing, but we do not want a
                // temporary provider outage to multiply cost/latency or stall the Discord event loop.
                failureThreshold: 3,
                breakDuration: TimeSpan.FromSeconds(30),
                maxRetryAttempts: 3,
                initialRetryDelay: TimeSpan.FromSeconds(1),
                logger: loggerFactory.CreateLogger($"ResiliencyHelper.{operationName}")
            );
        }

        private static string CreateTextPreview(string text)
        {
            return text.Length <= MaxLoggedTextPreviewLength
                ? text
                : new string(text.AsSpan(0, MaxLoggedTextPreviewLength));
        }

        private static string FormatProviderDetails(IReadOnlyList<TranslationProviderConfigurationItem> details)
        {
            if (details.Count == 0)
            {
                return "none";
            }

            return string.Join("; ", details.Select(detail => $"{detail.Label}={detail.Value}"));
        }
    }
}
