using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class TranslationService
    {
        private const int MaxLoggedTextPreviewLength = 15;
        private readonly DatabaseService _dbService;
        private readonly ITranslationProvider _translationProvider;
        private readonly ILogger<TranslationService> _logger;
        private readonly ResiliencyHelper _resiliencyHelper;

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

            _resiliencyHelper = new ResiliencyHelper(
                // Keep retries conservative: translation calls are user-facing, but we do not want a
                // temporary provider outage to multiply cost/latency or stall the Discord event loop.
                failureThreshold: 3,
                breakDuration: TimeSpan.FromSeconds(30),
                maxRetryAttempts: 3,
                initialRetryDelay: TimeSpan.FromSeconds(1),
                logger: loggerFactory.CreateLogger<ResiliencyHelper>()
            );

            _logger.LogProviderImplementationConfigured(_translationProvider.GetType().Name);
        }

        public bool IsActive => _translationProvider.IsActive;

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

        public async Task<string> DetectLanguageAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "EN";
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

            string code = await _resiliencyHelper.ExecuteAsync(() => _translationProvider.DetectLanguageAsync(text));
            await IncrementTranslationUsageAsync(charCount);

            _logger.LogLanguageDetected(code, CreateTextPreview(text));

            return code;
        }

        public async Task<string> TranslateTextAsync(string text, string targetLanguageCode)
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

            string translatedResult = await _resiliencyHelper.ExecuteAsync(() => _translationProvider.TranslateTextAsync(text, targetLanguageCode));
            await IncrementTranslationUsageAsync(charCount);

            return translatedResult;
        }

        private static string CreateTextPreview(string text)
        {
            return text.Length <= MaxLoggedTextPreviewLength
                ? text
                : new string(text.AsSpan(0, MaxLoggedTextPreviewLength));
        }
    }
}
