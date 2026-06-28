using System;
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

namespace Tsumari.Bot.Tests
{
    public class DeepLLanguageServiceTests
    {
        [Fact]
        public async Task NormalizeTargetLanguageCodeAsync_ReturnsExactSupportedGenericCode()
        {
            var service = CreateService(
                """
                [
                  { "lang": "PT", "usable_as_target": true },
                  { "lang": "PT-BR", "usable_as_target": true },
                  { "lang": "PT-PT", "usable_as_target": true }
                ]
                """);

            var result = await service.NormalizeTargetLanguageCodeAsync("pt");

            Assert.Equal("PT", result);
        }

        [Fact]
        public async Task NormalizeTargetLanguageCodeAsync_UsesLegacyFallbackWhenGenericCodeIsNotTargetable()
        {
            var service = CreateService(
                """
                [
                  { "lang": "EN-US", "usable_as_target": true },
                  { "lang": "EN-GB", "usable_as_target": true },
                  { "lang": "EN", "usable_as_target": false },
                  { "lang": "PT-BR", "usable_as_target": true },
                  { "lang": "PT-PT", "usable_as_target": true },
                  { "lang": "PT", "usable_as_target": false }
                ]
                """);

            Assert.Equal("EN-US", await service.NormalizeTargetLanguageCodeAsync("en"));
            Assert.Equal("PT-PT", await service.NormalizeTargetLanguageCodeAsync("pt"));
            Assert.Equal("PT-BR", await service.NormalizeTargetLanguageCodeAsync("pt-br"));
        }

        [Fact]
        public async Task NormalizeTargetLanguageCodeAsync_FallsBackToLegacyMappingWhenApiLookupFails()
        {
            var service = CreateService(null, HttpStatusCode.InternalServerError);

            Assert.Equal("EN-US", await service.NormalizeTargetLanguageCodeAsync("en"));
            Assert.Equal("PT-PT", await service.NormalizeTargetLanguageCodeAsync("pt"));
            Assert.Equal("FR", await service.NormalizeTargetLanguageCodeAsync("fr"));
        }

        private static DeepLLanguageService CreateService(string? jsonResponse, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["DeepL:ApiKey"]).Returns("dummy-api-key");

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock
                .Setup(f => f.CreateClient(HttpClientNames.DeepLLanguageMetadata))
                .Returns(new HttpClient(new StubHttpMessageHandler(jsonResponse, statusCode)));

            return new DeepLLanguageService(
                configMock.Object,
                httpClientFactoryMock.Object,
                NullLogger<DeepLLanguageService>.Instance);
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly string? _jsonResponse;
            private readonly HttpStatusCode _statusCode;

            public StubHttpMessageHandler(string? jsonResponse, HttpStatusCode statusCode)
            {
                _jsonResponse = jsonResponse;
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode);
                if (_jsonResponse != null)
                {
                    response.Content = new StringContent(_jsonResponse, Encoding.UTF8, "application/json");
                }

                return Task.FromResult(response);
            }
        }
    }
}
