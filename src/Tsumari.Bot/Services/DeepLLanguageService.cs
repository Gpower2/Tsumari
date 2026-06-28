using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class DeepLLanguageService
    {
        private static readonly IReadOnlyDictionary<string, string> LegacyTargetLanguageFallbacks =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // These aliases keep older generic registrations working when DeepL exposes only the
                // region-specific target variant in its target-language metadata.
                ["EN"] = "EN-US",
                ["PT"] = "PT-PT",
            };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DeepLLanguageService> _logger;
        private readonly string? _apiKey;
        private readonly string _serverUrl;
        private readonly SemaphoreSlim _supportedLanguagesLock = new(1, 1);

        private HashSet<string>? _supportedTargetLanguageCodes;

        public DeepLLanguageService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<DeepLLanguageService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            _apiKey = configuration["DeepL:ApiKey"] ?? configuration["DeepLKey"];
            _serverUrl = !string.IsNullOrWhiteSpace(_apiKey) && _apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
                ? "https://api-free.deepl.com"
                : "https://api.deepl.com";
        }

        public async Task<string> NormalizeTargetLanguageCodeAsync(string? code)
        {
            var clean = LanguageCodeService.NormalizeLanguageCode(code);
            if (string.IsNullOrWhiteSpace(clean))
            {
                return clean;
            }

            var supportedTargetLanguageCodes = await TryGetSupportedTargetLanguageCodesAsync();
            if (supportedTargetLanguageCodes != null)
            {
                if (supportedTargetLanguageCodes.Contains(clean))
                {
                    return clean;
                }

                if (LegacyTargetLanguageFallbacks.TryGetValue(clean, out var mapped) &&
                    supportedTargetLanguageCodes.Contains(mapped))
                {
                    return mapped;
                }

                return clean;
            }

            if (LegacyTargetLanguageFallbacks.TryGetValue(clean, out var fallback))
            {
                return fallback;
            }

            return clean;
        }

        public async Task<IReadOnlyCollection<string>> GetSupportedTargetLanguageCodesAsync()
        {
            var supportedTargetLanguageCodes = await TryGetSupportedTargetLanguageCodesAsync();
            return supportedTargetLanguageCodes != null
                ? supportedTargetLanguageCodes
                : Array.Empty<string>();
        }

        private async Task<HashSet<string>?> TryGetSupportedTargetLanguageCodesAsync()
        {
            if (_supportedTargetLanguageCodes != null)
            {
                return _supportedTargetLanguageCodes;
            }

            await _supportedLanguagesLock.WaitAsync();
            try
            {
                if (_supportedTargetLanguageCodes != null)
                {
                    return _supportedTargetLanguageCodes;
                }

                if (string.IsNullOrWhiteSpace(_apiKey))
                {
                    return null;
                }

                // DeepL's v3 language metadata is the source of truth for targetability. We cache the
                // result in-memory so startup/translation flow does not repeatedly hit the endpoint.
                var requestUri = $"{_serverUrl}/v3/languages?resource=translate_text";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {_apiKey}");

                using var httpClient = _httpClientFactory.CreateClient(HttpClientNames.DeepLLanguageMetadata);
                using var response = await httpClient.SendAsync(request);
                // Read the body before checking status so load-balancer/CDN/proxy error payloads and
                // diagnostic headers are preserved in logs when DeepL metadata lookups fail.
                var responseBody = await response.ReadStringWithStatusCheckAsync(_logger, "retrieving DeepL target language metadata");

                using var jsonDocument = JsonDocument.Parse(responseBody);
                var supportedTargetLanguageCodes = new HashSet<string>(StringComparer.Ordinal);

                foreach (var item in jsonDocument.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("usable_as_target", out var usableAsTargetProperty) ||
                        !usableAsTargetProperty.GetBoolean())
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("lang", out var langProperty))
                    {
                        continue;
                    }

                    var languageCode = LanguageCodeService.NormalizeLanguageCode(langProperty.GetString());
                    if (!string.IsNullOrWhiteSpace(languageCode))
                    {
                        supportedTargetLanguageCodes.Add(languageCode);
                    }
                }

                _supportedTargetLanguageCodes = supportedTargetLanguageCodes;
                return _supportedTargetLanguageCodes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve supported DeepL target language codes. Falling back to legacy mappings.");
                return null;
            }
            finally
            {
                _supportedLanguagesLock.Release();
            }
        }
    }
}
