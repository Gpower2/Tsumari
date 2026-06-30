using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class OpenAITranslationProviderTests
    {
        [Fact]
        public async Task TranslateTextAsync_ParsesChoiceMessageContent()
        {
            var provider = CreateProvider("""{ "choices": [ { "message": { "content": "\"Bonjour\"" } } ] }""");

            var result = await provider.TranslateTextAsync("hello", "fr");

            Assert.Equal("Bonjour", result);
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_ParsesStructuredChoiceMessageContent()
        {
            var provider = CreateProvider("""{ "choices": [ { "message": { "content": "{\"dominantLanguageCode\":\"EN\",\"languages\":[{\"languageCode\":\"EN\",\"share\":0.7},{\"languageCode\":\"FR\",\"share\":0.3}],\"isMixed\":true,\"hasClearDominantLanguage\":true}" } } ] }""");

            var result = await provider.AnalyzeLanguageAsync("Ma cherie! How are you doing?");

            Assert.Equal("EN", result.PrimaryLanguageCode);
            Assert.Equal(2, result.DetectedLanguages.Count);
            Assert.Equal("EN", result.DetectedLanguages[0].LanguageCode);
            Assert.Equal("FR", result.DetectedLanguages[1].LanguageCode);
            Assert.True(result.IsMixed);
            Assert.True(result.HasClearDominantLanguage);
        }

        [Fact]
        public async Task TranslateTextAsync_ThrowsMeaningfulError_WhenResponseIsNotJson()
        {
            var provider = CreateProvider("not-json");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateTextAsync("hello", "fr"));

            Assert.Contains("was not valid JSON", exception.Message);
            Assert.Contains("not-json", exception.Message);
            Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        }

        [Fact]
        public async Task TranslateTextAsync_ThrowsMeaningfulError_WhenChoicesArrayIsMissing()
        {
            var provider = CreateProvider("""{ "id": "abc123" }""");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateTextAsync("hello", "fr"));

            Assert.Contains("did not contain a 'choices' array", exception.Message);
            Assert.Contains("\"id\": \"abc123\"", exception.Message);
        }

        [Fact]
        public async Task TranslateTextAsync_ThrowsMeaningfulError_WhenChoicesArrayIsEmpty()
        {
            var provider = CreateProvider("""{ "choices": [] }""");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateTextAsync("hello", "fr"));

            Assert.Contains("contained an empty 'choices' array", exception.Message);
        }

        [Fact]
        public async Task TranslateTextAsync_ThrowsMeaningfulError_WhenMessageContentIsMissing()
        {
            var provider = CreateProvider("""{ "choices": [ { "message": {} } ] }""");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateTextAsync("hello", "fr"));

            Assert.Contains("did not contain a string 'content' value in the first choice message", exception.Message);
        }

        [Fact]
        public async Task TranslateTextAsync_TruncatesInvalidResponsePreview()
        {
            var provider = CreateProvider(new string('x', 1024) + "TAIL");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateTextAsync("hello", "fr"));

            Assert.Contains(new string('x', 1024) + "...", exception.Message);
            Assert.DoesNotContain("TAIL", exception.Message);
        }

        [Fact]
        public void GetConfigurationReport_ReturnsConfiguredModelAndEndpoint()
        {
            var provider = CreateProvider("""{ "choices": [ { "message": { "content": "\"Bonjour\"" } } ] }""");

            var report = provider.GetConfigurationReport();

            Assert.Equal("OpenAI", report.ProviderName);
            Assert.Contains(report.Details, detail => detail is TranslationProviderConfigurationItem { Label: "Model", Value: "mistral-7b" });
            Assert.Contains(report.Details, detail => detail is TranslationProviderConfigurationItem { Label: "Endpoint", Value: "http://localhost:8080/v1/chat/completions" });
        }

        private static OpenAITranslationProvider CreateProvider(string responseBody)
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:OpenAI:ApiUrl"]).Returns("http://localhost:8080/v1/chat/completions");
            configMock.Setup(c => c["Translation:OpenAI:Model"]).Returns("mistral-7b");
            configMock.Setup(c => c["Translation:OpenAI:ApiKey"]).Returns("dummy");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(f => f.CreateClient(HttpClientNames.OpenAITranslation))
                .Returns(new HttpClient(new StubHttpMessageHandler(responseBody)));

            return new OpenAITranslationProvider(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<OpenAITranslationProvider>.Instance);
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _responseBody;

            public StubHttpMessageHandler(string responseBody)
            {
                _responseBody = responseBody;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
