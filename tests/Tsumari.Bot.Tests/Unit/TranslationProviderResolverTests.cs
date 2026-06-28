using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class TranslationProviderResolverTests
    {
        [Fact]
        public void Resolve_FallsBackToOllama_WhenProviderConfigIsMissing()
        {
            var configMock = new Mock<IConfiguration>();
            var resolver = CreateResolver(configMock.Object);

            var result = resolver.Resolve();

            Assert.IsType<OllamaTranslationProvider>(result);
        }

        [Fact]
        public void Resolve_FallsBackToOllama_WhenProviderConfigIsInvalid()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:Provider"]).Returns("NotARealProvider");
            var resolver = CreateResolver(configMock.Object);

            var result = resolver.Resolve();

            Assert.IsType<OllamaTranslationProvider>(result);
        }

        [Fact]
        public void Resolve_ReturnsConfiguredProvider_WhenProviderConfigIsValid()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Translation:Provider"]).Returns("OpenAI");
            var resolver = CreateResolver(configMock.Object);

            var result = resolver.Resolve();

            Assert.IsType<OpenAITranslationProvider>(result);
        }

        private static TranslationProviderResolver CreateResolver(IConfiguration configuration)
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var deepLLanguageService = new DeepLLanguageService(
                configuration,
                httpClientFactory.Object,
                NullLogger<DeepLLanguageService>.Instance);

            var deepLProvider = new DeepLTranslationProvider(
                configuration,
                deepLLanguageService,
                NullLogger<DeepLTranslationProvider>.Instance);

            var ollamaProvider = new OllamaTranslationProvider(
                configuration,
                httpClientFactory.Object,
                NullLogger<OllamaTranslationProvider>.Instance);
            var openAIProvider = new OpenAITranslationProvider(
                configuration,
                httpClientFactory.Object,
                NullLogger<OpenAITranslationProvider>.Instance);

            return new TranslationProviderResolver(
                configuration,
                deepLProvider,
                ollamaProvider,
                openAIProvider,
                NullLogger<TranslationProviderResolver>.Instance);
        }
    }
}
