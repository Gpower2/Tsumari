using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class DeepLTranslationProviderTests
    {
        [Fact]
        public void IsActive_IsFalse_WhenApiKeyIsMissing()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["DeepL:ApiKey"]).Returns(string.Empty);

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var deepLLanguageService = new DeepLLanguageService(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<DeepLLanguageService>.Instance);

            var provider = new DeepLTranslationProvider(
                configMock.Object,
                deepLLanguageService,
                NullLogger<DeepLTranslationProvider>.Instance);

            Assert.False(provider.IsActive);
            Assert.True(provider.UsesCharacterQuota);
        }

        [Fact]
        public void GetConfigurationReport_ReportsFreePlanWhenFxKeyIsConfigured()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["DeepL:ApiKey"]).Returns("test-key:fx");

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var deepLLanguageService = new DeepLLanguageService(
                configMock.Object,
                httpClientFactory.Object,
                NullLogger<DeepLLanguageService>.Instance);

            var provider = new DeepLTranslationProvider(
                configMock.Object,
                deepLLanguageService,
                NullLogger<DeepLTranslationProvider>.Instance);

            var report = provider.GetConfigurationReport();

            Assert.Equal("DeepL", report.ProviderName);
            Assert.Equal("DeepLTranslationProvider", report.ImplementationName);
            Assert.True(report.UsesCharacterQuota);
            Assert.Contains(report.Details, detail => detail is TranslationProviderConfigurationItem { Label: "Plan", Value: "Free" });
            Assert.Contains(report.Details, detail => detail is TranslationProviderConfigurationItem { Label: "Endpoint", Value: "https://api-free.deepl.com" });
        }
    }
}
