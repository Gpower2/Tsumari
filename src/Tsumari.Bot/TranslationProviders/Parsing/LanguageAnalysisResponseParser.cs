using System.Globalization;
using System.Text.Json;
namespace Tsumari.Bot.TranslationProviders.Parsing;

public static class LanguageAnalysisResponseParser
{
    private const int StackAllocThreshold = 128;
    private const int MaxLoggedResponsePreviewLength = 1024;
    public static LanguageAnalysisResult Parse(string rawResult)
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
