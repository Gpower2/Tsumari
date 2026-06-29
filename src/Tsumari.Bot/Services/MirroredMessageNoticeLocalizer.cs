using System.Collections.Generic;

namespace Tsumari.Bot.Services
{
    public static class MirroredMessageNoticeLocalizer
    {
        private const string DefaultOversizedAttachmentNotice = "*(Attachment too large to mirror - use Original.)*";

        private static readonly IReadOnlyDictionary<string, string> OversizedAttachmentNoticeByLanguage =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DE"] = "*(Anhang zu gross zum Spiegeln - nutze Original.)*",
                ["EL"] = "*(Το συνημμένο είναι πολύ μεγάλο για αντιγραφή - χρησιμοποίησε Original.)*",
                ["EN"] = DefaultOversizedAttachmentNotice,
                ["ES"] = "*(Adjunto demasiado grande para reflejar - usa Original.)*",
                ["IT"] = "*(Allegato troppo grande da rispecchiare - usa Original.)*",
                ["PT"] = "*(Anexo grande demais para espelhar - use Original.)*"
            };

        public static string GetOversizedAttachmentNotice(string? languageCode)
        {
            var normalized = LanguageCodeService.NormalizeLanguageCode(languageCode);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "MASTER", StringComparison.Ordinal))
            {
                return DefaultOversizedAttachmentNotice;
            }

            if (OversizedAttachmentNoticeByLanguage.TryGetValue(normalized, out var exactMatch))
            {
                return exactMatch;
            }

            var separatorIndex = normalized.IndexOf('-');
            if (separatorIndex > 0)
            {
                var baseLanguageCode = normalized[..separatorIndex];
                if (OversizedAttachmentNoticeByLanguage.TryGetValue(baseLanguageCode, out var baseLanguageMatch))
                {
                    return baseLanguageMatch;
                }
            }

            return DefaultOversizedAttachmentNotice;
        }
    }
}
