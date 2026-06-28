using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests
{
    public class LanguageCodeServiceTests
    {
        [Theory]
        [InlineData("pt-br", "PT-BR")]
        [InlineData(" pt_BR ", "PT-BR")]
        [InlineData("en", "EN")]
        public void NormalizeLanguageCode_NormalizesCaseAndSeparators(string input, string expected)
        {
            Assert.Equal(expected, LanguageCodeService.NormalizeLanguageCode(input));
        }

        [Theory]
        [InlineData("pt-br", "pt-br")]
        [InlineData("pt_BR", "pt-br")]
        [InlineData("EN-US", "en-us")]
        public void NormalizeStoredLanguageCode_ReturnsLowercaseHyphenatedValue(string input, string expected)
        {
            Assert.Equal(expected, LanguageCodeService.NormalizeStoredLanguageCode(input));
        }

        [Theory]
        [InlineData("PT", "pt", true)]
        [InlineData("PT-BR", "pt-br", true)]
        [InlineData("PT", "pt-br", false)]
        [InlineData("EN", "en-us", false)]
        [InlineData("PT-BR", "pt-pt", false)]
        [InlineData("EN-US", "en-gb", false)]
        [InlineData("EN", "de", false)]
        public void AreSameLanguageCode_RequiresExactLocaleMatch(string left, string right, bool expected)
        {
            Assert.Equal(expected, LanguageCodeService.AreSameLanguageCode(left, right));
        }

        [Theory]
        [InlineData("PT", "pt-br", true)]
        [InlineData("PT", "pt", true)]
        [InlineData("EN", "en-us", true)]
        [InlineData("PT-BR", "pt", false)]
        [InlineData("PT-BR", "pt-br", true)]
        [InlineData("PT-BR", "pt-pt", false)]
        public void MatchesCurrentChannelLanguage_UsesGenericFallbackOnlyForSourceChannel(string detected, string configured, bool expected)
        {
            Assert.Equal(expected, LanguageCodeService.MatchesCurrentChannelLanguage(detected, configured));
        }

        [Theory]
        [InlineData("PT", "pt-br", "PT-BR")]
        [InlineData("PT", "pt", "PT")]
        [InlineData("PT-BR", "pt", "PT-BR")]
        [InlineData("EN", "el", "EN")]
        public void ResolveSourceLanguageCode_PreservesConfiguredSourceLocaleWhenCompatible(string detected, string configured, string expected)
        {
            Assert.Equal(expected, LanguageCodeService.ResolveSourceLanguageCode(detected, configured));
        }

    }
}
