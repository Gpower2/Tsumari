using System.Threading.Tasks;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.TranslationProviders.Abstractions
{
    public abstract class LlmTranslationProviderBase : ITranslationProvider
    {
        public abstract bool IsActive { get; }

        public bool UsesCharacterQuota => false;

        public async Task<string> DetectLanguageAsync(string text)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            // We intentionally keep detection output coarse here (ISO 639-1) and let routing decide
            // whether a channel-local locale hint should refine it to EN-US, PT-BR, etc.
            var systemPrompt = "You are a precise language detection tool. Detect the primary language of the user text. Return ONLY the ISO 639-1 two-letter code (e.g. EN, EL, IT, FR, DE, ES) in uppercase, with absolutely no formatting, notes, or conversational filler.";
            
            var userPrompt = $"Text to detect:\n{text}";

            var rawResult = await CallModelAsync(systemPrompt, userPrompt);
            var cleanResult = rawResult.Trim().ToUpperInvariant();
            
            // Models occasionally wrap short answers in markdown or quotes; strip those artifacts
            // before the routing layer compares the detected language code.
            cleanResult = cleanResult.Replace("*", string.Empty).Replace("`", string.Empty).Trim();

            return cleanResult;
        }

        public async Task<string> TranslateTextAsync(string text, string targetLanguageCode)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

            var targetLanguage = LanguageCodeService.NormalizeLanguageCode(targetLanguageCode);

            // The system prompt is strict because we use LLMs as a translation backend, not a chat
            // endpoint: preserving tone/markdown matters, while explanations or safety rewrites do not.
            var systemPrompt = $"You are an elite machine translation backend for a Discord bot. Translate the requested text directly into the target language code: {targetLanguage}. Maintain the exact original tone, emojis, and markdown formatting. Do NOT sanitize, censor, tone down, or filter profanity, insults, or harsh slang. Preserve the exact raw intent, cultural meaning, slang equivalents, and emotional weight in the target language. Return ONLY the translated text, with absolutely no explanation, notes, conversational filler, commentary, or introductory text.";

            // Fence the text so the model focuses on the payload instead of treating user content
            // as extra instructions that can bleed into the output.
            var userPrompt =
                "Please read the isolated text below, identify the language, and execute the conversion rule.\n\n" +
                $"<text>\n{text}\n</text>\n\n" +
                $"Task: Output ONLY the direct translation of the above text into language: {targetLanguage}.";

            var result = await CallModelAsync(systemPrompt, userPrompt);
            var cleanResult = result.Trim();
            if (cleanResult.StartsWith('\"') && cleanResult.EndsWith('\"') && cleanResult.Length > 2)
            {
                // Some chat-style models still wrap the final answer in quotes even when instructed
                // not to; remove those so Discord messages stay clean.
                cleanResult = cleanResult.Substring(1, cleanResult.Length - 2).Trim();
            }

            return cleanResult;
        }

        protected abstract Task<string> CallModelAsync(string systemPrompt, string userPrompt);
    }
}
