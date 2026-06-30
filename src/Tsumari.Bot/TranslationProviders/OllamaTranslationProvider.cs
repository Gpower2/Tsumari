using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.TranslationProviders
{
    public class OllamaTranslationProvider : LlmTranslationProviderBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OllamaTranslationProvider> _logger;
        private readonly string? _apiUrl;
        private readonly string? _model;

        public OllamaTranslationProvider(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OllamaTranslationProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiUrl = configuration["Translation:Ollama:ApiUrl"] ?? "http://localhost:11434/api/generate";
            _model = configuration["Translation:Ollama:Model"] ?? "aya:8b";
        }

        protected override string ProviderName => "Ollama";

        protected override string? ConfiguredEndpoint => _apiUrl;

        protected override string? ConfiguredModel => _model;

        public override bool IsActive => !string.IsNullOrWhiteSpace(_apiUrl) && !string.IsNullOrWhiteSpace(_model);

        protected override async Task<string> CallModelAsync(string systemPrompt, string userPrompt)
        {
            var payload = new
            {
                model = _model,
                prompt = $"{systemPrompt}\n\n{userPrompt}",
                // The bot needs a single completed translation payload, not token-by-token streaming.
                stream = false,
                options = new
                {
                    // Keep decoding deterministic so repeated translations of the same text do not drift.
                    temperature = 0.0f,
                    // Restrict the token candidate pool further to favor literal, stable translations.
                    top_p = 0.10f,
                    // Translation requests are short; a moderate context window keeps resource usage low.
                    num_ctx = 4096,
                }
            };

            using var requestContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var httpClient = _httpClientFactory.CreateClient(HttpClientNames.OllamaTranslation);
            using var response = await httpClient.PostAsync(_apiUrl, requestContent);
            var responseBody = await response.ReadStringWithStatusCheckAsync(_logger, "calling the Ollama translation endpoint");

            using var jsonDoc = JsonDocument.Parse(responseBody);
            return jsonDoc.RootElement.GetProperty("response").GetString() ?? string.Empty;
        }
    }
}
