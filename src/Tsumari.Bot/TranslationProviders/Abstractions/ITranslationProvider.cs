using System.Threading.Tasks;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.TranslationProviders.Abstractions
{
    public interface ITranslationProvider
    {
        bool SupportsGenerationOptions => false;

        Task<string> TranslateWithOptionsAsync(string text, string targetLanguageCode, string? sourceLanguageCode, TranslationGenerationOptions options)
            => throw new NotSupportedException("This provider does not support retry generation settings.");

        bool IsActive { get; }

        bool UsesCharacterQuota { get; }

        TranslationProviderConfigurationReport GetConfigurationReport();

        Task<LanguageAnalysisResult> AnalyzeLanguageAsync(string text);

        Task<string> TranslateTextAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null);
    }
}
