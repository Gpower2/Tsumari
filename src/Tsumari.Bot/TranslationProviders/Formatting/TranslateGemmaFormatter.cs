using Tsumari.Bot.Services;
using Tsumari.Bot.TranslationProviders.Requests;
using Tsumari.Bot.TranslationProviders.Models;
namespace Tsumari.Bot.TranslationProviders.Formatting;

public sealed class TranslateGemmaFormatter : IModelFormatter
{
    public bool SupportsAnalysis => true;
    public PreparedModelRequest Prepare(ModelTaskRequest request, ModelProfile profile) => request.IsAnalysis ? PrepareAnalysis(request.Text) : PrepareTranslation(request.Text, request.TargetLanguageCode!, request.SourceLanguageCode);
    public static PreparedModelRequest PrepareAnalysis(string text)
    {
        var systemPrompt =
            "You are a precise multilingual language analysis engine for a Discord translation bot. " +
            "Analyze the full user message as one unit, even when multiple languages are interleaved inside the same sentence. " +
            "Identify every meaningful human language present, estimate each language's relative share of the message, and choose the dominant source language. " +
            "Do not let a single short foreign word, salutation, interjection, meme phrase, or borrowed slang override an otherwise dominant language unless it materially changes the overall language balance. " +
            "Estimate each share from approximate word count and contiguous span length across the entire message. Never overweight taboo words, insults, or the opening tokens just because they are distinctive. " +
            "A short foreign swear phrase before a much longer clause in another language stays minor. " +
            "Proper names are names, not evidence that the surrounding clause is written in the language commonly associated with that name. " +
            "In informal Italian chat, an opening unaccented \"si\" can still mean affirmative sì when the surrounding clause is otherwise Italian. " +
            "Ignore emojis, URLs, mentions, hashtags, and punctuation when deciding language shares. " +
            "Examples: \"caralho, this is getting wild already\" -> dominantLanguageCode EN. " +
            "\"foda se, caralho. Things are already heating up and people are placing bets like crazy\" -> dominantLanguageCode EN. " +
            "\"μαλακα, this queue is moving fast tonight\" -> dominantLanguageCode EN. " +
            "\"μαλακα, how are you doing tonight?\" -> dominantLanguageCode EN. " +
            "\"Thank you fratello! Things are already heating up\" -> dominantLanguageCode EN. " +
            "Return ONLY strict JSON with this exact shape: " +
            "{\"dominantLanguageCode\":\"EN\",\"languages\":[{\"languageCode\":\"EN\",\"share\":0.85},{\"languageCode\":\"IT\",\"share\":0.15}],\"isMixed\":true,\"hasClearDominantLanguage\":true}. " +
            "Use uppercase ISO 639-1 language codes for every languageCode. Keep the languages array ordered from most dominant to least dominant. " +
            "Do not include markdown fences, explanations, commentary, or extra keys.";

        var userPrompt =
            "Analyze the isolated text below as a single message and return ONLY the JSON object.\n\n" +
            $"<text>\n{text}\n</text>";

        return new(systemPrompt, userPrompt, true, true);
    }
    public static PreparedModelRequest PrepareTranslation(string text, string targetLanguageCode, string? sourceLanguageCode)
    {
        var targetLanguage = LanguageCodeService.NormalizeLanguageCode(targetLanguageCode);
        var sourceLanguage = LanguageCodeService.NormalizeLanguageCode(sourceLanguageCode);

        var systemPrompt =
            $"You are an elite machine translation backend for a Discord bot. " +
            $"The input may contain multiple languages or code-switching within the same sentence. " +
            $"Translate the entire message directly into the target language code: {targetLanguage}. " +
            $"Maintain the exact original tone, emojis, and markdown formatting. " +
            $"Do NOT sanitize, censor, tone down, or filter profanity, insults, sexual profanity, taboo terms, or harsh slang. " +
            $"Preserve the exact raw intent, cultural meaning, slang equivalents, and emotional weight in the target language. " +
            $"Return ONLY the translated text, with absolutely no explanation, notes, conversational filler, commentary, or introductory text.";

        if (!string.IsNullOrWhiteSpace(sourceLanguage))
        {
            systemPrompt +=
                $" The dominant source language was already analyzed as {sourceLanguage}. " +
                $"Use that as a strong disambiguation signal when a token could belong to multiple languages or when informal chat omits accents or diacritics. " +
                $"Resolve short ambiguous discourse markers from the meaning of the full clause, not from isolated dictionary matches. " +
                $"Proper names are names, not evidence that the surrounding clause is a different language.";

            if (LanguageCodeService.AreSameLanguageFamily(sourceLanguage, "IT"))
            {
                systemPrompt +=
                    $" For Italian source text, informal chat often omits the accent in sì. " +
                    $"A sentence-opening \"si\" before a name or comma can therefore mean affirmative \"yes\" rather than conditional \"if\" when the rest of the clause is Italian.";
            }
        }

        // Fence the text so the model focuses on the payload instead of treating user content
        // as extra instructions that can bleed into the output.
        var userPrompt =
            (!string.IsNullOrWhiteSpace(sourceLanguage)
                ? $"Detected dominant source language: {sourceLanguage}. Use it to resolve ambiguous tokens.\n\n"
                : string.Empty) +
            "Please read the isolated text below, identify the language, and execute the conversion rule.\n\n" +
            $"<text>\n{text}\n</text>\n\n" +
            $"Task: Output ONLY the direct translation of the above text into language: {targetLanguage}.";

        return new(systemPrompt, userPrompt, false, true);
    }
    public string NormalizeTranslation(string result) => NormalizeTranslationResult(result);
    private static string NormalizeTranslationResult(string result)
    {
        var trimmed = TrimWhitespace(result.AsSpan());
        if (trimmed.Length > 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            // Some chat-style models still wrap the final answer in quotes even when instructed
            // not to; remove those so Discord messages stay clean.
            trimmed = TrimWhitespace(trimmed[1..^1]);
        }

        return trimmed.IsEmpty ? string.Empty : new string(trimmed);
    }

    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value) => value.Trim();
}
