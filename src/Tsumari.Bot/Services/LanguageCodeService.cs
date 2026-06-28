using System;

namespace Tsumari.Bot.Services
{
    public static class LanguageCodeService
    {
        public static string NormalizeLanguageCode(string? code)
        {
            return (code ?? string.Empty).Trim().Replace('_', '-').ToUpperInvariant();
        }

        public static string NormalizeStoredLanguageCode(string? code)
        {
            return NormalizeLanguageCode(code).ToLowerInvariant();
        }

        public static bool AreSameLanguageCode(string? leftLanguageCode, string? rightLanguageCode)
        {
            var left = NormalizeLanguageCode(leftLanguageCode);
            var right = NormalizeLanguageCode(rightLanguageCode);

            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(left, right, StringComparison.Ordinal);
        }

        public static bool MatchesCurrentChannelLanguage(string? detectedLanguageCode, string? configuredLanguageCode)
        {
            var detected = NormalizeLanguageCode(detectedLanguageCode);
            var configured = NormalizeLanguageCode(configuredLanguageCode);

            if (AreSameLanguageCode(detected, configured))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(detected) || string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            if (detected.Contains('-', StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(GetBaseLanguageCode(detected), GetBaseLanguageCode(configured), StringComparison.Ordinal);
        }

        public static string ResolveSourceLanguageCode(string? detectedLanguageCode, string? currentChannelLanguageCode)
        {
            if (MatchesCurrentChannelLanguage(detectedLanguageCode, currentChannelLanguageCode))
            {
                return NormalizeLanguageCode(currentChannelLanguageCode);
            }

            return NormalizeLanguageCode(detectedLanguageCode);
        }

        private static string GetBaseLanguageCode(string code)
        {
            var separatorIndex = code.IndexOf('-');
            return separatorIndex >= 0 ? code.Substring(0, separatorIndex) : code;
        }
    }
}
