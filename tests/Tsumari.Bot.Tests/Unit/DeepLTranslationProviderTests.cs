using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
    }
}
