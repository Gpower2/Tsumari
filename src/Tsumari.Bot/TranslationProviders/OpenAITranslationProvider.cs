using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.TranslationProviders
{
    public class OpenAITranslationProvider : LlmTranslationProviderBase
    {
        private const int MaxLoggedResponsePreviewLength = 1024;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OpenAITranslationProvider> _logger;
        private readonly string? _apiUrl;
        private readonly string? _model;
        private readonly string? _apiKey;

        public OpenAITranslationProvider(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OpenAITranslationProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiUrl = configuration["Translation:OpenAI:ApiUrl"] ?? "http://localhost:8080/v1/chat/completions";
            _model = configuration["Translation:OpenAI:Model"] ?? "mistral-7b";
            _apiKey = configuration["Translation:OpenAI:ApiKey"] ?? "dummy";
        }

        public override bool IsActive => !string.IsNullOrWhiteSpace(_apiUrl) && !string.IsNullOrWhiteSpace(_model);

        protected override async Task<string> CallModelAsync(string systemPrompt, string userPrompt)
        {
            var payload = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                // Deterministic decoding keeps provider behavior closer to a traditional translation API.
                temperature = 0.0
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            using var httpClient = _httpClientFactory.CreateClient(HttpClientNames.OpenAITranslation);
            using var response = await httpClient.SendAsync(requestMessage);
            var responseBody = await response.ReadStringWithStatusCheckAsync(_logger, "calling the OpenAI-compatible translation endpoint");

            return ParseMessageContent(responseBody);
        }

        private static string ParseMessageContent(string responseBody)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(responseBody);
                var rootElement = jsonDoc.RootElement;

                if (!rootElement.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array)
                {
                    throw CreateInvalidResponseException("did not contain a 'choices' array.", responseBody);
                }

                if (choicesElement.GetArrayLength() == 0)
                {
                    throw CreateInvalidResponseException("contained an empty 'choices' array.", responseBody);
                }

                var firstChoice = choicesElement[0];
                if (firstChoice.ValueKind != JsonValueKind.Object)
                {
                    throw CreateInvalidResponseException("did not contain an object as the first item in 'choices'.", responseBody);
                }

                if (!firstChoice.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.Object)
                {
                    throw CreateInvalidResponseException("did not contain a 'message' object in the first choice.", responseBody);
                }

                if (!messageElement.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.String)
                {
                    throw CreateInvalidResponseException("did not contain a string 'content' value in the first choice message.", responseBody);
                }

                return contentElement.GetString() ?? string.Empty;
            }
            catch (JsonException ex)
            {
                throw CreateInvalidResponseException("was not valid JSON.", responseBody, ex);
            }
        }

        private static InvalidOperationException CreateInvalidResponseException(
            string detail,
            string responseBody,
            Exception? innerException = null)
        {
            return new InvalidOperationException(
                $"OpenAI-compatible translation response {detail} Response body: {TruncateResponseBody(responseBody)}",
                innerException);
        }

        private static string TruncateResponseBody(string responseBody)
        {
            if (TrimWhitespace(responseBody.AsSpan()).IsEmpty)
            {
                return "(empty)";
            }

            if (responseBody.Length <= MaxLoggedResponsePreviewLength)
            {
                return responseBody;
            }

            return string.Create(
                MaxLoggedResponsePreviewLength + 3,
                responseBody,
                static (destination, source) =>
                {
                    source.AsSpan(0, MaxLoggedResponsePreviewLength).CopyTo(destination);
                    "...".AsSpan().CopyTo(destination[MaxLoggedResponsePreviewLength..]);
                });
        }

        private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value)
        {
            var start = 0;
            while (start < value.Length && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            var end = value.Length - 1;
            while (end >= start && char.IsWhiteSpace(value[end]))
            {
                end--;
            }

            return value[start..(end + 1)];
        }
    }
}
