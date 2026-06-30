using System.Threading.Tasks;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.TranslationProviders.Abstractions
{
    public interface ITranslationProvider
    {
        bool IsActive { get; }

        bool UsesCharacterQuota { get; }

        Task<LanguageAnalysisResult> AnalyzeLanguageAsync(string text);

        Task<string> TranslateTextAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null);
    }
}
