using System.Threading.Tasks;

namespace Tsumari.Bot.Services
{
    public interface ITranslationProvider
    {
        bool IsActive { get; }

        bool UsesCharacterQuota { get; }

        Task<string> DetectLanguageAsync(string text);

        Task<string> TranslateTextAsync(string text, string targetLanguageCode);
    }
}
