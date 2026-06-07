using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeepL;

namespace Tsumari.Bot.Services
{
    public enum TranslationProvider
    {
        DeepL,
        Ollama,
        OpenAI,
    }

    public class TranslationService
    {
        private readonly DatabaseService _dbService;
        private readonly ILogger<TranslationService> _logger;
        private readonly Translator? _translator;
        private readonly ResiliencyHelper _resiliencyHelper;
        private readonly IHttpClientFactory _httpClientFactory;
        
        private readonly TranslationProvider _provider;
        private readonly string? _llmUrl;
        private readonly string? _llmModel;
        private readonly string? _llmApiKey;
        
        public const int MonthlyCharacterLimit = 500000;

        public TranslationService(
            IConfiguration configuration,
            DatabaseService dbService,
            ILogger<TranslationService> logger,
            ILoggerFactory loggerFactory,
            IHttpClientFactory httpClientFactory)
        {
            _dbService = dbService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;

            // Instantiate ResiliencyHelper specifically for external translation calls
            _resiliencyHelper = new ResiliencyHelper(
                failureThreshold: 3,
                breakDuration: TimeSpan.FromSeconds(30),
                maxRetryAttempts: 3,
                initialRetryDelay: TimeSpan.FromSeconds(1),
                logger: loggerFactory.CreateLogger<ResiliencyHelper>()
            );

            // Parse translation provider
            var providerString = configuration["Translation:Provider"] ?? "DeepL";
            if (Enum.TryParse<TranslationProvider>(providerString, true, out var parsedProvider))
            {
                _provider = parsedProvider;
            }
            else
            {
                _provider = TranslationProvider.DeepL;
            }

            _logger.LogInformation("Translation Service configured to use provider: {Provider}", _provider);

            if (_provider == TranslationProvider.Ollama)
            {
                _llmUrl = configuration["Translation:Ollama:ApiUrl"] ?? "http://localhost:11434/api/generate";
                _llmModel = configuration["Translation:Ollama:Model"] ?? "aya:8b";
            }
            else if (_provider == TranslationProvider.OpenAI)
            {
                _llmUrl = configuration["Translation:OpenAI:ApiUrl"] ?? "http://localhost:8080/v1/chat/completions";
                _llmModel = configuration["Translation:OpenAI:Model"] ?? "mistral-7b";
                _llmApiKey = configuration["Translation:OpenAI:ApiKey"] ?? "dummy";
            }
            else
            {
                // DeepL Initialization
                var apiKey = configuration["DeepL:ApiKey"] ?? configuration["DeepLKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogCritical("DeepL API Key is missing! Bot will not be able to perform translations with DeepL.");
                    return;
                }

                try
                {
                    var options = new TranslatorOptions();
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
        }

        /// <summary>
        /// True if the translator configuration is valid and active.
        /// </summary>
        public bool IsActive => _provider != TranslationProvider.DeepL || _translator != null;

        /// <summary>
        /// Evaluates if the current monthly usage allows translating the given character count.
        /// This only applies to DeepL since local LLMs do not have a character quota
        /// </summary>
        public async Task<bool> CanTranslateAsync(int characterCount)
        {
            if (_provider == TranslationProvider.DeepL)
            {
                var currentUsage = await _dbService.GetCurrentMonthUsageAsync();
                return (currentUsage + characterCount) <= MonthlyCharacterLimit;
            }

            return true;
        }

        /// <summary>
        /// Increments the monthly usage count by the specified character count. This should be called after a successful translation 
        /// or detection operation to track usage against the quota. 
        /// For local LLM providers, this method does not apply.
        /// </summary>
        /// <param name="charCount"></param>
        /// <returns></returns>
        public async Task IncrementTranslationUsageAsync(int charCount)
        {
            if (_provider == TranslationProvider.DeepL)
            {
                await _dbService.IncrementUsageAsync(charCount);
            }
        }

        /// <summary>
        /// Automatically detects the true language code of the raw string payload.
        /// </summary>
        public async Task<string> DetectLanguageAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "EN"; // Default fallback

            if (!IsActive)
                throw new InvalidOperationException("TranslationService is not active.");

            int charCount = text.Length;

            // Character quota guard check
            if (!await CanTranslateAsync(charCount))
            {
                _logger.LogWarning("Detection request blocked! Monthly translation limit of {Limit} characters exceeded.", MonthlyCharacterLimit);
                throw new InvalidOperationException("Monthly translation quota limit reached.");
            }

            string code = await _resiliencyHelper.ExecuteAsync(async () =>
            {
                if (_provider == TranslationProvider.DeepL)
                {
                    var response = await _translator!.TranslateTextAsync(text, null, "EN-US");
                    return response.DetectedSourceLanguageCode.ToUpperInvariant();
                }
                else
                {
                    // For local LLMs, we prompt the model to perform language detection
                    return await DetectLanguageWithLLMAsync(text);
                }
            });

            // Increment usage post success
            await IncrementTranslationUsageAsync(charCount);

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
                throw new InvalidOperationException("TranslationService is not active.");

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
                if (_provider == TranslationProvider.DeepL)
                {
                    var response = await _translator!.TranslateTextAsync(text, null, sanitizedTargetLang);
                    return response.Text;
                }
                else
                {
                    return await TranslateTextWithLLMAsync(text, sanitizedTargetLang);
                }
            });

            // Increment usage post success
            await IncrementTranslationUsageAsync(charCount);

            return translatedResult;
        }

        private async Task<string> DetectLanguageWithLLMAsync(string text)
        {
            var systemPrompt = "You are a precise language detection tool. Detect the primary language of the user text. Return ONLY the ISO 639-1 two-letter code (e.g. EN, EL, IT, FR, DE, ES) in uppercase, with absolutely no formatting, notes, or conversational filler.";
            var userPrompt = $"Text to detect:\n{text}";

            var rawResult = await CallLLMApiAsync(systemPrompt, userPrompt);
            var cleanResult = rawResult.Trim().ToUpperInvariant();
            
            // Clean up common LLM markdown output artifacts if present (e.g. "EN" instead of "**EN**")
            cleanResult = cleanResult.Replace("*", "").Replace("`", "").Trim();
            
            return cleanResult;
        }

        private async Task<string> TranslateTextWithLLMAsync(string text, string targetLanguage)
        {
            var systemPrompt = $"You are a professional translation assistant. Your goal is to accurately convey the meaning and nuances of the original text while adhering to {targetLanguage} grammar, vocabulary, and cultural sensitivities. Maintain the original tone, emojis, and markdown formatting. Produce ONLY the {targetLanguage} translation, without absolutely any additional explanations, notes, conversational filler, or commentary and introductory text.";
            var userPrompt = $"Text to translate:\n{text}";

            var result = await CallLLMApiAsync(systemPrompt, userPrompt);
            
            // Strip out any surrounding quotes that models sometimes add
            var cleanResult = result.Trim();
            if (cleanResult.StartsWith('\"') && cleanResult.EndsWith('\"') && cleanResult.Length > 2)
            {
                cleanResult = cleanResult.Substring(1, cleanResult.Length - 2).Trim();
            }

            return cleanResult;
        }

        private async Task<string> CallLLMApiAsync(string systemPrompt, string userPrompt)
        {
            if (_provider == TranslationProvider.Ollama)
            {
                // Ollama Generate API payload
                var payload = new
                {
                    model = _llmModel,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    stream = false,
                    options = new 
                    { 
                        temperature = 0.0f, // Low temperature for high precision, strips away all creative seed variation
                        top_p = 0.90f,
                        num_ctx = 4096, // Limit context size to save VRAM and maintain speed
                    }
                };

                using var requestContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var httpClient = _httpClientFactory.CreateClient();
                var response = await httpClient.PostAsync(_llmUrl, requestContent);
                response.EnsureSuccessStatusCode();

                using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                return jsonDoc.RootElement.GetProperty("response").GetString() ?? string.Empty;
            }
            else if (_provider == TranslationProvider.OpenAI)
            {
                // OpenAI-compatible Chat API payload
                var payload = new
                {
                    model = _llmModel,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.0
                };

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _llmUrl)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_llmApiKey))
                {
                    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _llmApiKey);
                }

                using var httpClient = _httpClientFactory.CreateClient();
                var response = await httpClient.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();

                using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                return jsonDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            }

            throw new InvalidOperationException("Invalid LLM configuration.");
        }

        private static string SanitizeLanguageCode(string code)
        {
            var clean = code.Trim().ToUpperInvariant();
            return clean switch
            {
                "EN" => "EN-US",
                "PT" => "PT-PT",
                _ => clean
            };
        }
    }
}
