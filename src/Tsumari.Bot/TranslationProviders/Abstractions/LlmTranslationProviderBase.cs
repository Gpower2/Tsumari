using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.TranslationProviders.Abstractions
{
    public abstract class LlmTranslationProviderBase : ITranslationProvider
    {
        private const int StackAllocThreshold = 128;
        private const int MaxLoggedResponsePreviewLength = 1024;

        public abstract bool IsActive { get; }

        public bool UsesCharacterQuota => false;

        public async Task<LanguageAnalysisResult> AnalyzeLanguageAsync(string text)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

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

            var rawResult = await CallModelAsync(systemPrompt, userPrompt);
            return ParseLanguageAnalysisResult(rawResult);
        }

        public async Task<string> TranslateTextAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Translation provider is not active.");
            }

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

            var result = await CallModelAsync(systemPrompt, userPrompt);
            return NormalizeTranslationResult(result);
        }

        protected abstract Task<string> CallModelAsync(string systemPrompt, string userPrompt);

        private static LanguageAnalysisResult ParseLanguageAnalysisResult(string rawResult)
        {
            try
            {
                var normalizedJson = NormalizeJsonPayload(rawResult);
                using var jsonDoc = JsonDocument.Parse(normalizedJson);
                var rootElement = jsonDoc.RootElement;
                if (rootElement.ValueKind != JsonValueKind.Object)
                {
                    throw CreateInvalidAnalysisResponseException("did not contain a JSON object at the root.", rawResult);
                }

                var dominantLanguageCode = ReadStringProperty(
                    rootElement,
                    "dominantLanguageCode",
                    "dominant_language_code",
                    "primaryLanguageCode",
                    "primary_language_code");
                var languages = ReadDetectedLanguages(rootElement);
                if (string.IsNullOrWhiteSpace(dominantLanguageCode) && languages.Count == 0)
                {
                    throw CreateInvalidAnalysisResponseException(
                        "did not contain a dominant language code or any detected languages.",
                        rawResult);
                }

                return new LanguageAnalysisResult(
                    dominantLanguageCode,
                    languages,
                    ReadNullableBooleanProperty(rootElement, "isMixed", "is_mixed"),
                    ReadNullableBooleanProperty(rootElement, "hasClearDominantLanguage", "has_clear_dominant_language"));
            }
            catch (JsonException ex)
            {
                throw CreateInvalidAnalysisResponseException("was not valid JSON.", rawResult, ex);
            }
        }

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

        private static string NormalizeJsonPayload(string rawResult)
        {
            var trimmed = TrimWhitespace(rawResult.AsSpan());
            if (trimmed.IsEmpty)
            {
                return string.Empty;
            }

            var normalized = new string(trimmed);
            if (!normalized.StartsWith("```", StringComparison.Ordinal))
            {
                return normalized;
            }

            var firstLineEnd = normalized.IndexOf('\n');
            if (firstLineEnd < 0)
            {
                return normalized.Trim('`', ' ', '\r', '\n');
            }

            var lastFenceStart = normalized.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFenceStart <= firstLineEnd)
            {
                return normalized[(firstLineEnd + 1)..].Trim();
            }

            return normalized[(firstLineEnd + 1)..lastFenceStart].Trim();
        }

        private static List<DetectedLanguage> ReadDetectedLanguages(JsonElement rootElement)
        {
            if (!TryGetPropertyIgnoreCase(rootElement, out var languagesElement, "languages", "detectedLanguages", "detected_languages")
                || languagesElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var languages = new List<DetectedLanguage>(languagesElement.GetArrayLength());
            foreach (var languageElement in languagesElement.EnumerateArray())
            {
                switch (languageElement.ValueKind)
                {
                    case JsonValueKind.String:
                    {
                        var languageCode = NormalizeDetectedLanguageCode(languageElement.GetString());
                        if (!string.IsNullOrWhiteSpace(languageCode))
                        {
                            languages.Add(new DetectedLanguage(languageCode));
                        }

                        break;
                    }
                    case JsonValueKind.Object:
                    {
                        var languageCode = ReadStringProperty(languageElement, "languageCode", "language_code", "code");
                        if (string.IsNullOrWhiteSpace(languageCode))
                        {
                            continue;
                        }

                        languages.Add(new DetectedLanguage(
                            languageCode,
                            ReadNullableDoubleProperty(languageElement, "share", "weight", "confidence", "percentage")));
                        break;
                    }
                }
            }

            return languages;
        }

        private static string? ReadStringProperty(JsonElement element, params string[] propertyNames)
        {
            return TryGetPropertyIgnoreCase(element, out var propertyElement, propertyNames)
                && propertyElement.ValueKind == JsonValueKind.String
                ? NormalizeDetectedLanguageCode(propertyElement.GetString())
                : null;
        }

        private static bool? ReadNullableBooleanProperty(JsonElement element, params string[] propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, out var propertyElement, propertyNames))
            {
                return null;
            }

            return propertyElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(propertyElement.GetString(), out var parsedBoolean) => parsedBoolean,
                _ => null
            };
        }

        private static double? ReadNullableDoubleProperty(JsonElement element, params string[] propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, out var propertyElement, propertyNames))
            {
                return null;
            }

            return propertyElement.ValueKind switch
            {
                JsonValueKind.Number when propertyElement.TryGetDouble(out var parsedNumber) => parsedNumber,
                JsonValueKind.String when TryParseJsonDoubleString(propertyElement.GetString(), out var parsedStringNumber) => parsedStringNumber,
                _ => null
            };
        }

        private static bool TryParseJsonDoubleString(string? value, out double parsedNumber)
        {
            var trimmed = TrimWhitespace(value.AsSpan());
            if (trimmed.IsEmpty)
            {
                parsedNumber = default;
                return false;
            }

            var normalized = new string(trimmed);
            if (normalized.Contains(',') && !normalized.Contains('.'))
            {
                normalized = normalized.Replace(',', '.');
            }

            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsedNumber);
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            out JsonElement propertyElement,
            params string[] propertyNames)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var propertyName in propertyNames)
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        propertyElement = property.Value;
                        return true;
                    }
                }
            }

            propertyElement = default;
            return false;
        }

        private static string NormalizeDetectedLanguageCode(string? rawResult)
        {
            var trimmed = TrimWhitespace(rawResult.AsSpan());
            if (trimmed.IsEmpty)
            {
                return string.Empty;
            }

            Span<char> buffer = trimmed.Length <= StackAllocThreshold
                ? stackalloc char[trimmed.Length]
                : new char[trimmed.Length];

            var length = 0;
            for (var index = 0; index < trimmed.Length; index++)
            {
                var value = trimmed[index];
                if (value is '*' or '`' or '"')
                {
                    continue;
                }

                buffer[length++] = char.ToUpperInvariant(value);
            }

            var cleaned = TrimWhitespace(buffer[..length]);
            return cleaned.IsEmpty ? string.Empty : new string(cleaned);
        }

        private static InvalidOperationException CreateInvalidAnalysisResponseException(
            string detail,
            string responseBody,
            Exception? innerException = null)
        {
            return new InvalidOperationException(
                $"Language analysis response {detail} Response body: {TruncateResponseBody(responseBody)}",
                innerException);
        }

        private static string TruncateResponseBody(string responseBody)
        {
            if (TrimWhitespace(responseBody.AsSpan()).IsEmpty)
            {
                return "(empty)";
            }

            if (responseBody.Length <= MaxLoggedResponsePreviewLength)
            {
                return responseBody;
            }

            return string.Create(
                MaxLoggedResponsePreviewLength + 3,
                responseBody,
                static (destination, source) =>
                {
                    source.AsSpan(0, MaxLoggedResponsePreviewLength).CopyTo(destination);
                    "...".AsSpan().CopyTo(destination[MaxLoggedResponsePreviewLength..]);
                });
        }

        private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value)
        {
            var start = 0;
            while (start < value.Length && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            var end = value.Length - 1;
            while (end >= start && char.IsWhiteSpace(value[end]))
            {
                end--;
            }

            return value[start..(end + 1)];
        }
    }
}
