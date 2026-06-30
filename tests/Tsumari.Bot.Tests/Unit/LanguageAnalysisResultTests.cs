using Tsumari.Bot.Models;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class LanguageAnalysisResultTests
    {
        [Fact]
        public void Constructor_DedupesPrimaryLanguageFamilyVariants()
        {
            var result = new LanguageAnalysisResult(
                "EN-US",
                [
                    new DetectedLanguage("EN", 0.9),
                    new DetectedLanguage("EN-US", 0.1),
                    new DetectedLanguage("IT", 0.05)
                ],
                isMixed: true,
                hasClearDominantLanguage: true);

            Assert.Equal(2, result.DetectedLanguages.Count);
            Assert.Equal("EN-US", result.DetectedLanguages[0].LanguageCode);
            Assert.Equal(0.9, result.DetectedLanguages[0].Share);
            Assert.Equal("IT", result.DetectedLanguages[1].LanguageCode);
        }
    }
}
