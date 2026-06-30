using System.Net;
using System.Net.Http;
using System.Text;
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
    public class OllamaTranslationProviderTests
    {
        [Fact]
        public async Task AnalyzeLanguageAsync_ParsesResponsePayload()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:Ollama:ApiUrl"]).Returns("http://localhost:11434/api/generate");
            configMock.Setup(c => c["Translation:Ollama:Model"]).Returns("aya:8b");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(f => f.CreateClient(HttpClientNames.OllamaTranslation))
                .Returns(new HttpClient(new StubHttpMessageHandler("""{ "response": "{ \"dominantLanguageCode\": \"EN\", \"languages\": [\"EN\"], \"isMixed\": false, \"hasClearDominantLanguage\": true }" }""")));

            var provider = new OllamaTranslationProvider(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<OllamaTranslationProvider>.Instance);

            var result = await provider.AnalyzeLanguageAsync("hello");

            Assert.Equal("EN", result.PrimaryLanguageCode);
            Assert.Single(result.DetectedLanguages);
            Assert.Equal("EN", result.DetectedLanguages[0].LanguageCode);
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_StripsMarkdownArtifactsAndWhitespace()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:Ollama:ApiUrl"]).Returns("http://localhost:11434/api/generate");
            configMock.Setup(c => c["Translation:Ollama:Model"]).Returns("aya:8b");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(f => f.CreateClient(HttpClientNames.OllamaTranslation))
                .Returns(new HttpClient(new StubHttpMessageHandler("""{ "response": "```json\n{ \"dominantLanguageCode\": \"en\", \"languages\": [{ \"languageCode\": \"it\", \"share\": 0.20 }, { \"languageCode\": \"en\", \"share\": 0.80 }], \"isMixed\": true, \"hasClearDominantLanguage\": true }\n```" }""")));

            var provider = new OllamaTranslationProvider(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<OllamaTranslationProvider>.Instance);

            var result = await provider.AnalyzeLanguageAsync("hello");

            Assert.Equal("EN", result.PrimaryLanguageCode);
            Assert.Equal(2, result.DetectedLanguages.Count);
            Assert.Equal("EN", result.DetectedLanguages[0].LanguageCode);
            Assert.Equal("IT", result.DetectedLanguages[1].LanguageCode);
        }

        [Fact]
        public void GetConfigurationReport_ReturnsConfiguredModelAndEndpoint()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:Ollama:ApiUrl"]).Returns("http://localhost:11434/api/generate");
            configMock.Setup(c => c["Translation:Ollama:Model"]).Returns("translategemma:12b");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var provider = new OllamaTranslationProvider(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<OllamaTranslationProvider>.Instance);

            var report = provider.GetConfigurationReport();

            Assert.Equal("Ollama", report.ProviderName);
            Assert.Contains(report.Details, detail => detail is TranslationProviderConfigurationItem { Label: "Model", Value: "translategemma:12b" });
            Assert.Contains(report.Details, detail => detail is TranslationProviderConfigurationItem { Label: "Endpoint", Value: "http://localhost:11434/api/generate" });
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
