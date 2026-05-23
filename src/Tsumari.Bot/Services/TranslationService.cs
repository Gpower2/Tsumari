using System;
using System.Threading.Tasks;
using DeepL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class TranslationService
    {
        private readonly DatabaseService _dbService;
        private readonly ILogger<TranslationService> _logger;
        private readonly Translator? _translator;
        private readonly ResiliencyHelper _resiliencyHelper;
        
        public const int MonthlyCharacterLimit = 500000;

        public TranslationService(
            IConfiguration configuration,
            DatabaseService dbService,
            ILogger<TranslationService> logger,
            ILoggerFactory loggerFactory)
        {
            _dbService = dbService;
            _logger = logger;

            // Instantiate ResiliencyHelper specifically for DeepL API operations
            _resiliencyHelper = new ResiliencyHelper(
                failureThreshold: 3,
                breakDuration: TimeSpan.FromSeconds(30),
                maxRetryAttempts: 3,
                initialRetryDelay: TimeSpan.FromSeconds(1),
                logger: loggerFactory.CreateLogger<ResiliencyHelper>()
            );

            var apiKey = configuration["DeepL:ApiKey"] ?? configuration["DeepLKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogCritical("DeepL API Key is missing! Bot will not be able to perform translations.");
                return;
            }

            try
            {
                var options = new TranslatorOptions();
                
                // Explicitly check for Free Tier suffix ':fx' as requested
                if (apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase))
                {
                    options.ServerUrl = "https://api-free.deepl.com";
                    _logger.LogInformation("DeepL Key verified with ':fx' suffix. Hardcoded routing to 'https://api-free.deepl.com'.");
                }
                else
                {
                    options.ServerUrl = "https://api.deepl.com";
                    _logger.LogInformation("DeepL Key parsed. Routing to 'https://api.deepl.com'.");
                }

                _translator = new Translator(apiKey, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize DeepL Translator client.");
            }
        }

        /// <summary>
        /// True if the translator is successfully configured and active.
        /// </summary>
        public bool IsActive => _translator != null;

        /// <summary>
        /// Evaluates if the current monthly usage allows translating the given character count.
        /// </summary>
        public async Task<bool> CanTranslateAsync(int characterCount)
        {
            var currentUsage = await _dbService.GetCurrentMonthUsageAsync();
            return (currentUsage + characterCount) <= MonthlyCharacterLimit;
        }

        /// <summary>
        /// Automatically detects the true language code of the raw string payload.
        /// </summary>
        public async Task<string> DetectLanguageAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "EN"; // Default fallback

            if (!IsActive)
                throw new InvalidOperationException("TranslationService is not active or missing API configuration.");

            int charCount = text.Length;

            // Character quota guard check
            if (!await CanTranslateAsync(charCount))
            {
                _logger.LogWarning("Detection request blocked! Monthly translation limit of {Limit} characters exceeded.", MonthlyCharacterLimit);
                throw new InvalidOperationException("Monthly translation quota limit reached.");
            }

            string code = await _resiliencyHelper.ExecuteAsync(async () =>
            {
                // To detect language using standard DeepL .NET SDK, we translate to EN-US with sourceLanguage = null.
                var response = await _translator!.TranslateTextAsync(text, null, "EN-US");
                return response.DetectedSourceLanguageCode.ToUpperInvariant();
            });

            // Increment usage post success
            await _dbService.IncrementUsageAsync(charCount);

            _logger.LogInformation("Language detected: '{Lang}' for text prefix '{Prefix}'", 
                code, text.Substring(0, Math.Min(text.Length, 15)));

            return code;
        }

        /// <summary>
        /// Translates text into the designated target language code, enforcing usage limits.
        /// </summary>
        public async Task<string> TranslateTextAsync(string text, string targetLanguageCode)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!IsActive)
                throw new InvalidOperationException("TranslationService is not active or missing API configuration.");

            // Standardize language code format (e.g. DeepL expects EN-US, EN-GB or EN, EL, IT, etc.)
            // DeepL expects target languages like "en-US", "el", "it". We will sanitize the inputs.
            string sanitizedTargetLang = SanitizeLanguageCode(targetLanguageCode);

            int charCount = text.Length;

            // Character quota guard check
            if (!await CanTranslateAsync(charCount))
            {
                _logger.LogWarning("Translation request blocked! Monthly translation limit of {Limit} characters exceeded.", MonthlyCharacterLimit);
                throw new InvalidOperationException("Monthly translation quota limit reached.");
            }

            string translatedResult = await _resiliencyHelper.ExecuteAsync(async () =>
            {
                var response = await _translator!.TranslateTextAsync(text, null, sanitizedTargetLang);
                return response.Text;
            });

            // Increment usage post success
            await _dbService.IncrementUsageAsync(charCount);

            return translatedResult;
        }

        private static string SanitizeLanguageCode(string code)
        {
            var clean = code.Trim().ToUpperInvariant();
            
            // Map simple 2-letter codes to DeepL specific targets if necessary
            // E.g., EN -> EN-US, PT -> PT-PT (DeepL requires region details for some, others are simple like EL, IT)
            return clean switch
            {
                "EN" => "EN-US",
                "PT" => "PT-PT",
                _ => clean
            };
        }
    }
}
