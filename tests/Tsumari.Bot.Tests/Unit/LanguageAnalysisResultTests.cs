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
                    new DetectedLanguage("IT", 0.20)
                ],
                isMixed: true,
                hasClearDominantLanguage: true);

            Assert.Equal(2, result.DetectedLanguages.Count);
            Assert.Equal("EN-US", result.DetectedLanguages[0].LanguageCode);
            Assert.Equal(0.9, result.DetectedLanguages[0].Share);
            Assert.Equal("IT", result.DetectedLanguages[1].LanguageCode);
        }

        [Fact]
        public void Constructor_CollapsesTinySecondaryLanguageShares()
        {
            var result = new LanguageAnalysisResult(
                "EL",
                [
                    new DetectedLanguage("EL", 0.95),
                    new DetectedLanguage("EN", 0.05)
                ],
                isMixed: true,
                hasClearDominantLanguage: true);

            Assert.Single(result.DetectedLanguages);
            Assert.Equal("EL", result.DetectedLanguages[0].LanguageCode);
            Assert.Equal(1.0, result.DetectedLanguages[0].Share);
            Assert.False(result.IsMixed);
            Assert.True(result.HasClearDominantLanguage);
        }

        [Fact]
        public void Constructor_CollapsesBoundaryMinorSecondaryLanguageShares()
        {
            var result = new LanguageAnalysisResult(
                "EN",
                [
                    new DetectedLanguage("EN", 0.85),
                    new DetectedLanguage("IT", 0.15)
                ],
                isMixed: true,
                hasClearDominantLanguage: true);

            Assert.Single(result.DetectedLanguages);
            Assert.Equal("EN", result.DetectedLanguages[0].LanguageCode);
            Assert.Equal(1.0, result.DetectedLanguages[0].Share);
            Assert.False(result.IsMixed);
        }
    }
}
