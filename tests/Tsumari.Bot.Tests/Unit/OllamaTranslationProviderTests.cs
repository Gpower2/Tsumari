using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class OllamaTranslationProviderTests
    {
        [Fact]
        public async Task DetectLanguageAsync_ParsesResponsePayload()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:Ollama:ApiUrl"]).Returns("http://localhost:11434/api/generate");
            configMock.Setup(c => c["Translation:Ollama:Model"]).Returns("aya:8b");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(f => f.CreateClient(HttpClientNames.OllamaTranslation))
                .Returns(new HttpClient(new StubHttpMessageHandler("""{ "response": "**EN**" }""")));

            var provider = new OllamaTranslationProvider(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<OllamaTranslationProvider>.Instance);

            var result = await provider.DetectLanguageAsync("hello");

            Assert.Equal("EN", result);
        }

        [Fact]
        public async Task DetectLanguageAsync_StripsMarkdownArtifactsAndWhitespace()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:Ollama:ApiUrl"]).Returns("http://localhost:11434/api/generate");
            configMock.Setup(c => c["Translation:Ollama:Model"]).Returns("aya:8b");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(f => f.CreateClient(HttpClientNames.OllamaTranslation))
                .Returns(new HttpClient(new StubHttpMessageHandler("""{ "response": "  *`en`*  " }""")));

            var provider = new OllamaTranslationProvider(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<OllamaTranslationProvider>.Instance);

            var result = await provider.DetectLanguageAsync("hello");

            Assert.Equal("EN", result);
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
