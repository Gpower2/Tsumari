using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.TranslationProviders
{
    public class OllamaTranslationProvider : LlmTranslationProviderBase
    {
        private static readonly JsonSerializerOptions RequestJsonSerializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OllamaTranslationProvider> _logger;
        private readonly string? _apiUrl;
        private readonly string? _model;
        private readonly string? _keepAlive;

        public OllamaTranslationProvider(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OllamaTranslationProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiUrl = configuration["Translation:Ollama:ApiUrl"] ?? "http://localhost:11434/api/generate";
            _model = configuration["Translation:Ollama:Model"] ?? "aya:8b";
            _keepAlive = configuration["Translation:Ollama:KeepAlive"] ?? "15m";
        }

        protected override string ProviderName => "Ollama";

        protected override string? ConfiguredEndpoint => _apiUrl;

        protected override string? ConfiguredModel => _model;

        public override bool IsActive => !string.IsNullOrWhiteSpace(_apiUrl) && !string.IsNullOrWhiteSpace(_model);

        public override TranslationProviderConfigurationReport GetConfigurationReport()
        {
            var report = base.GetConfigurationReport();
            if (string.IsNullOrWhiteSpace(_keepAlive))
            {
                return report;
            }

            return report with
            {
                Details =
                [
                    new("Endpoint", report.Details.First(detail => detail.Label == "Endpoint").Value),
                    new("Model", report.Details.First(detail => detail.Label == "Model").Value),
                    new("KeepAlive", _keepAlive),
                    new("Capabilities", report.Details.First(detail => detail.Label == "Capabilities").Value)
                ]
            };
        }

        protected override Task<string> CallLanguageAnalysisModelAsync(string systemPrompt, string userPrompt)
        {
            // Ollama's JSON mode avoids markdown-fenced payloads and trims a small but measurable
            // amount of analysis output work without changing the decision logic in the prompt.
            return CallModelAsync(systemPrompt, userPrompt, responseFormat: "json");
        }

        protected override Task<string> CallModelAsync(string systemPrompt, string userPrompt)
        {
            return CallModelAsync(systemPrompt, userPrompt, responseFormat: null);
        }

        private async Task<string> CallModelAsync(string systemPrompt, string userPrompt, string? responseFormat)
        {
            var payload = new
            {
                model = _model,
                prompt = $"{systemPrompt}\n\n{userPrompt}",
                // The bot needs a single completed translation payload, not token-by-token streaming.
                stream = false,
                format = responseFormat,
                // Keep the model resident across typical Discord idle gaps so the next message
                // does not pay a full cold-start load before language analysis can begin.
                keep_alive = string.IsNullOrWhiteSpace(_keepAlive) ? null : _keepAlive,
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

            using var requestContent = new StringContent(
                JsonSerializer.Serialize(payload, RequestJsonSerializerOptions),
                Encoding.UTF8,
                "application/json");
            using var httpClient = _httpClientFactory.CreateClient(HttpClientNames.OllamaTranslation);
            using var response = await httpClient.PostAsync(_apiUrl, requestContent);
            var responseBody = await response.ReadStringWithStatusCheckAsync(_logger, "calling the Ollama translation endpoint");

            using var jsonDoc = JsonDocument.Parse(responseBody);
            return jsonDoc.RootElement.GetProperty("response").GetString() ?? string.Empty;
        }
    }
}
