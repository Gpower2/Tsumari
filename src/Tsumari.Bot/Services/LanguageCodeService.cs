using System;

namespace Tsumari.Bot.Services
{
    public static class LanguageCodeService
    {
        private const int StackAllocThreshold = 32;

        public static string NormalizeLanguageCode(string? code)
        {
            return NormalizeLanguageCodeCore(code, useUpperCase: true);
        }

        public static string NormalizeStoredLanguageCode(string? code)
        {
            return NormalizeLanguageCodeCore(code, useUpperCase: false);
        }

        public static bool AreSameLanguageCode(string? leftLanguageCode, string? rightLanguageCode)
        {
            var left = TrimWhitespace(leftLanguageCode.AsSpan());
            var right = TrimWhitespace(rightLanguageCode.AsSpan());

            if (left.IsEmpty || right.IsEmpty || left.Length != right.Length)
            {
                return false;
            }

            return NormalizedEquals(left, right);
        }

        public static bool MatchesCurrentChannelLanguage(string? detectedLanguageCode, string? configuredLanguageCode)
        {
            var detected = TrimWhitespace(detectedLanguageCode.AsSpan());
            var configured = TrimWhitespace(configuredLanguageCode.AsSpan());

            if (detected.IsEmpty || configured.IsEmpty)
            {
                return false;
            }

            if (NormalizedEquals(detected, configured))
            {
                return true;
            }

            if (ContainsLocaleSeparator(detected))
            {
                return false;
            }

            return NormalizedEquals(detected, GetBaseLanguageCode(configured));
        }

        public static string ResolveSourceLanguageCode(string? detectedLanguageCode, string? currentChannelLanguageCode)
        {
            if (MatchesCurrentChannelLanguage(detectedLanguageCode, currentChannelLanguageCode))
            {
                return NormalizeLanguageCode(currentChannelLanguageCode);
            }

            return NormalizeLanguageCode(detectedLanguageCode);
        }

        private static string NormalizeLanguageCodeCore(string? code, bool useUpperCase)
        {
            var trimmed = TrimWhitespace(code.AsSpan());
            if (trimmed.IsEmpty)
            {
                return string.Empty;
            }

            Span<char> buffer = trimmed.Length <= StackAllocThreshold
                ? stackalloc char[trimmed.Length]
                : new char[trimmed.Length];

            for (var index = 0; index < trimmed.Length; index++)
            {
                buffer[index] = NormalizeLanguageCodeChar(trimmed[index], useUpperCase);
            }

            return new string(buffer);
        }

        private static bool NormalizedEquals(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (NormalizeLanguageCodeChar(left[index], useUpperCase: true) != NormalizeLanguageCodeChar(right[index], useUpperCase: true))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsLocaleSeparator(ReadOnlySpan<char> code)
        {
            return code.IndexOfAny('-', '_') >= 0;
        }

        private static ReadOnlySpan<char> GetBaseLanguageCode(ReadOnlySpan<char> code)
        {
            var separatorIndex = code.IndexOfAny('-', '_');
            return separatorIndex >= 0 ? code[..separatorIndex] : code;
        }

        private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> code)
        {
            var start = 0;
            while (start < code.Length && char.IsWhiteSpace(code[start]))
            {
                start++;
            }

            var end = code.Length - 1;
            while (end >= start && char.IsWhiteSpace(code[end]))
            {
                end--;
            }

            return code[start..(end + 1)];
        }

        private static char NormalizeLanguageCodeChar(char value, bool useUpperCase)
        {
            if (value == '_')
            {
                return '-';
            }

            return useUpperCase
                ? char.ToUpperInvariant(value)
                : char.ToLowerInvariant(value);
        }
    }
}
