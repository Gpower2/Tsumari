using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class LlmTranslationProviderBaseTests
    {
        [Fact]
        public async Task AnalyzeLanguageAsync_BuildsWholeMessageMixedLanguagePrompt()
        {
            var provider = new CapturingProvider("""{"dominantLanguageCode":"EN","languages":[{"languageCode":"EN","share":0.88},{"languageCode":"IT","share":0.12}],"isMixed":true,"hasClearDominantLanguage":true}""");
            const string input = "Thank you fratello! 🤗\nThings are already heating up, they are placing bets like crazy 😅";

            var result = await provider.AnalyzeLanguageAsync(input);

            Assert.Equal("EN", result.PrimaryLanguageCode);
            Assert.Contains("Analyze the full user message as one unit", provider.CapturedSystemPrompt);
            Assert.Contains("Do not let a single short foreign word", provider.CapturedSystemPrompt);
            Assert.Contains("Estimate each share from approximate word count and contiguous span length across the entire message", provider.CapturedSystemPrompt);
            Assert.Contains("A short foreign swear phrase before a much longer clause in another language stays minor", provider.CapturedSystemPrompt);
            Assert.Contains("Proper names are names, not evidence that the surrounding clause is written in the language commonly associated with that name", provider.CapturedSystemPrompt);
            Assert.Contains("opening unaccented \"si\" can still mean affirmative sì", provider.CapturedSystemPrompt);
            Assert.Contains("\"caralho, this is getting wild already\" -> dominantLanguageCode EN", provider.CapturedSystemPrompt);
            Assert.Contains("\"μαλακα, how are you doing tonight?\" -> dominantLanguageCode EN", provider.CapturedSystemPrompt);
            Assert.Contains("Return ONLY strict JSON", provider.CapturedSystemPrompt);
            Assert.Contains(input, provider.CapturedUserPrompt);
        }

        [Fact]
        public async Task TranslateTextAsync_BuildsUncensoredMixedLanguagePrompt()
        {
            var provider = new CapturingProvider(" \"vai-te foder\" ");
            const string input = "foda se, caralho, μαλακα, γαμήσου";

            var result = await provider.TranslateTextAsync(input, "pt");

            Assert.Equal("vai-te foder", result);
            Assert.Contains("multiple languages or code-switching within the same sentence", provider.CapturedSystemPrompt);
            Assert.Contains("Do NOT sanitize, censor, tone down, or filter profanity, insults, sexual profanity, taboo terms, or harsh slang.", provider.CapturedSystemPrompt);
            Assert.Contains(input, provider.CapturedUserPrompt);
            Assert.Contains("language: PT", provider.CapturedUserPrompt);
        }

        [Fact]
        public async Task TranslateTextAsync_WithItalianSourceHint_AddsDisambiguationPrompt()
        {
            var provider = new CapturingProvider(" \"Yes, Tasos, they can be fun.\" ");
            const string input = "Si Tasos , sanno essere divertenti";

            var result = await provider.TranslateTextAsync(input, "en", "it");

            Assert.Equal("Yes, Tasos, they can be fun.", result);
            Assert.Contains("dominant source language was already analyzed as IT", provider.CapturedSystemPrompt);
            Assert.Contains("For Italian source text, informal chat often omits the accent in sì", provider.CapturedSystemPrompt);
            Assert.Contains("Detected dominant source language: IT", provider.CapturedUserPrompt);
            Assert.Contains(input, provider.CapturedUserPrompt);
        }

        [Fact]
        public async Task TranslateTextAsync_WithItalianLocaleSourceHint_AddsDisambiguationPrompt()
        {
            var provider = new CapturingProvider(" \"Yes, Tasos, they can be fun.\" ");
            const string input = "Si Tasos , sanno essere divertenti";

            var result = await provider.TranslateTextAsync(input, "en", "it-it");

            Assert.Equal("Yes, Tasos, they can be fun.", result);
            Assert.Contains("dominant source language was already analyzed as IT-IT", provider.CapturedSystemPrompt);
            Assert.Contains("For Italian source text, informal chat often omits the accent in sì", provider.CapturedSystemPrompt);
            Assert.Contains("Detected dominant source language: IT-IT", provider.CapturedUserPrompt);
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_ThrowsMeaningfulError_WhenResponseIsInvalidJson()
        {
            var provider = new CapturingProvider("not-json");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.AnalyzeLanguageAsync("Ma cherie! How are you doing?"));

            Assert.Contains("was not valid JSON", exception.Message);
            Assert.Contains("not-json", exception.Message);
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_NormalizesStringLanguageEntries()
        {
            var provider = new CapturingProvider("""{"languages":["**en**","`it`"],"isMixed":true,"hasClearDominantLanguage":true}""");

            var result = await provider.AnalyzeLanguageAsync("Thank you fratello!");

            Assert.Equal("EN", result.PrimaryLanguageCode);
            Assert.Equal(2, result.DetectedLanguages.Count);
            Assert.Equal("EN", result.DetectedLanguages[0].LanguageCode);
            Assert.Equal("IT", result.DetectedLanguages[1].LanguageCode);
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_ParsesStringSharesWithInvariantCulture()
        {
            var provider = new CapturingProvider("""{"dominantLanguageCode":"EN","languages":[{"languageCode":"EN","share":"0.5"},{"languageCode":"IT","share":"0.25"}],"isMixed":true,"hasClearDominantLanguage":false}""");
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUICulture = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

                var result = await provider.AnalyzeLanguageAsync("Thank you fratello!");

                Assert.Equal(0.5, result.DetectedLanguages[0].Share);
                Assert.Equal(0.25, result.DetectedLanguages[1].Share);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUICulture;
            }
        }

        [Fact]
        public async Task AnalyzeLanguageAsync_ParsesCommaDecimalStringShares()
        {
            var provider = new CapturingProvider("""{"dominantLanguageCode":"EN","languages":[{"languageCode":"EN","share":"0,5"},{"languageCode":"IT","share":"0,25"}],"isMixed":true,"hasClearDominantLanguage":false}""");

            var result = await provider.AnalyzeLanguageAsync("Thank you fratello!");

            Assert.Equal(0.5, result.DetectedLanguages[0].Share);
            Assert.Equal(0.25, result.DetectedLanguages[1].Share);
        }

        private sealed class CapturingProvider : LlmTranslationProviderBase
        {
            private readonly string _response;

            public CapturingProvider(string response)
            {
                _response = response;
            }

            protected override string ProviderName => "TestLlm";

            protected override string? ConfiguredEndpoint => "http://localhost/test";

            protected override string? ConfiguredModel => "test-model";

            public override bool IsActive => true;

            public string CapturedSystemPrompt { get; private set; } = string.Empty;

            public string CapturedUserPrompt { get; private set; } = string.Empty;

            protected override Task<string> CallModelAsync(string systemPrompt, string userPrompt)
            {
                CapturedSystemPrompt = systemPrompt;
                CapturedUserPrompt = userPrompt;
                return Task.FromResult(_response);
            }
        }
    }
}
