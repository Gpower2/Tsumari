using Tsumari.Bot.TranslationProviders.Models;
using Tsumari.Bot.TranslationProviders.Requests;
namespace Tsumari.Bot.TranslationProviders.Formatting;

public interface IModelFormatter
{
    bool SupportsAnalysis { get; }
    PreparedModelRequest Prepare(ModelTaskRequest request, ModelProfile profile);
    string NormalizeTranslation(string result);
}
