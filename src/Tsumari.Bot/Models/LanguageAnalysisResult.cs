using System;
using System.Collections.Generic;
using System.Linq;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Models
{
    public sealed record DetectedLanguage(
        string LanguageCode,
        double? Share = null);

    public sealed record SourceLanguageInfo(
        string PrimaryLanguageCode,
        IReadOnlyList<string> LabelLanguageCodes);

    public sealed class LanguageAnalysisResult
    {
        private const double MinimalSecondaryLanguageShareThreshold = 0.15;
        private const double StrongPrimaryLanguageShareThreshold = 0.85;

        public LanguageAnalysisResult(
            string? primaryLanguageCode,
            IReadOnlyCollection<DetectedLanguage>? detectedLanguages = null,
            bool? isMixed = null,
            bool? hasClearDominantLanguage = null)
        {
            var normalizedDetectedLanguages = NormalizeDetectedLanguages(detectedLanguages);
            var normalizedPrimaryLanguageCode = LanguageCodeService.NormalizeLanguageCode(primaryLanguageCode);
            if (string.IsNullOrWhiteSpace(normalizedPrimaryLanguageCode))
            {
                normalizedPrimaryLanguageCode = normalizedDetectedLanguages.Count > 0
                    ? normalizedDetectedLanguages[0].LanguageCode
                    : "EN";
            }

            PrimaryLanguageCode = normalizedPrimaryLanguageCode;
            DetectedLanguages = CollapseMinimalSecondaryLanguages(
                normalizedPrimaryLanguageCode,
                BuildDetectedLanguages(normalizedPrimaryLanguageCode, normalizedDetectedLanguages));
            IsMixed = DetectedLanguages.Count <= 1 ? false : isMixed;
            HasClearDominantLanguage = hasClearDominantLanguage;
        }

        public string PrimaryLanguageCode { get; }

        public IReadOnlyList<DetectedLanguage> DetectedLanguages { get; }

        public bool? IsMixed { get; }

        public bool? HasClearDominantLanguage { get; }

        public static LanguageAnalysisResult SingleLanguage(
            string languageCode,
            bool? isMixed = false,
            bool? hasClearDominantLanguage = true)
        {
            return new LanguageAnalysisResult(
                languageCode,
                [new DetectedLanguage(languageCode, 1.0)],
                isMixed,
                hasClearDominantLanguage);
        }

        private static List<DetectedLanguage> NormalizeDetectedLanguages(IReadOnlyCollection<DetectedLanguage>? detectedLanguages)
        {
            if (detectedLanguages == null || detectedLanguages.Count == 0)
            {
                return [];
            }

            return detectedLanguages
                .Select((detectedLanguage, index) => new
                {
                    Index = index,
                    LanguageCode = LanguageCodeService.NormalizeLanguageCode(detectedLanguage.LanguageCode),
                    Share = NormalizeShare(detectedLanguage.Share)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.LanguageCode))
                .GroupBy(item => item.LanguageCode, StringComparer.Ordinal)
                .Select(group =>
                {
                    var bestMatch = group
                        .OrderByDescending(item => item.Share.HasValue)
                        .ThenByDescending(item => item.Share ?? double.MinValue)
                        .ThenBy(item => item.Index)
                        .First();
                    return new
                    {
                        bestMatch.Index,
                        LanguageCode = bestMatch.LanguageCode!,
                        bestMatch.Share
                    };
                })
                .OrderByDescending(item => item.Share.HasValue)
                .ThenByDescending(item => item.Share ?? double.MinValue)
                .ThenBy(item => item.Index)
                .Select(item => new DetectedLanguage(item.LanguageCode, item.Share))
                .ToList();
        }

        private static IReadOnlyList<DetectedLanguage> BuildDetectedLanguages(
            string primaryLanguageCode,
            IReadOnlyList<DetectedLanguage> detectedLanguages)
        {
            if (detectedLanguages.Count == 0)
            {
                return [new DetectedLanguage(primaryLanguageCode)];
            }

            var orderedLanguages = detectedLanguages.ToList();
            var primaryLanguage = orderedLanguages.FirstOrDefault(language =>
                LanguageCodeService.AreSameLanguageFamily(language.LanguageCode, primaryLanguageCode));
            if (primaryLanguage != null)
            {
                orderedLanguages.RemoveAll(language =>
                    LanguageCodeService.AreSameLanguageFamily(language.LanguageCode, primaryLanguageCode));
                orderedLanguages.Insert(0, primaryLanguage with { LanguageCode = primaryLanguageCode });
                return orderedLanguages;
            }

            orderedLanguages.Insert(0, new DetectedLanguage(primaryLanguageCode));
            return orderedLanguages;
        }

        private static IReadOnlyList<DetectedLanguage> CollapseMinimalSecondaryLanguages(
            string primaryLanguageCode,
            IReadOnlyList<DetectedLanguage> detectedLanguages)
        {
            if (detectedLanguages.Count != 2)
            {
                return detectedLanguages;
            }

            var primaryLanguage = detectedLanguages[0];
            var secondaryLanguage = detectedLanguages[1];
            if (!primaryLanguage.Share.HasValue
                || !secondaryLanguage.Share.HasValue
                || primaryLanguage.Share.Value < StrongPrimaryLanguageShareThreshold
                || secondaryLanguage.Share.Value > MinimalSecondaryLanguageShareThreshold)
            {
                return detectedLanguages;
            }

            return [new DetectedLanguage(primaryLanguageCode, 1.0)];
        }

        private static double? NormalizeShare(double? share)
        {
            if (!share.HasValue)
            {
                return null;
            }

            var normalizedShare = share.Value;
            if (normalizedShare > 1.0 && normalizedShare <= 100.0)
            {
                normalizedShare /= 100.0;
            }

            return Math.Clamp(normalizedShare, 0.0, 1.0);
        }
    }
}
