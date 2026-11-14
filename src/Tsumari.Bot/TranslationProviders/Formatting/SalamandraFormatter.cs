using Tsumari.Bot.Services;
using Tsumari.Bot.TranslationProviders.Models;
using Tsumari.Bot.TranslationProviders.Requests;
namespace Tsumari.Bot.TranslationProviders.Formatting;

public sealed class SalamandraFormatter : IModelFormatter
{
    public bool SupportsAnalysis => false;
    internal static readonly IReadOnlyDictionary<string, string> DefaultLanguages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DE"] = "German", ["FR"] = "French", ["EL"] = "Greek", ["EN"] = "English", ["IT"] = "Italian", ["PT"] = "Portuguese", ["PT-BR"] = "Brazilian Portuguese", ["PT-PT"] = "European Portuguese", ["ES"] = "Spanish", ["SR"] = "Serbian", ["SR-LATN"] = "Serbian (Latin script)", ["SR-CYRL"] = "Serbian (Cyrillic script)", ["ZH"] = "Chinese (Simplified)", ["ZH-HANS"] = "Chinese (Simplified)", ["ZH-CN"] = "Chinese (Simplified)", ["ZH-HANT"] = "Chinese (Traditional)", ["ZH-TW"] = "Chinese (Traditional)" };
    internal static string Language(string? code, ModelProfile profile)
    {
        var key = LanguageCodeService.NormalizeLanguageCode(code);
        if (profile.Languages.TryGetValue(key, out var name)) return name;
        if (DefaultLanguages.TryGetValue(key, out name)) return name;
        throw new NotSupportedException($"Profile '{profile.Name}' has no explicit language mapping for '{key}'.");
    }
    public PreparedModelRequest Prepare(ModelTaskRequest request, ModelProfile profile)
    {
        RejectDelimiters(request.Text, "<|im_start|>", "<|im_end|>");
        if (request.IsAnalysis) throw new NotSupportedException("Salamandra has not been qualified for analysis.");
        var source = Language(request.SourceLanguageCode, profile); var target = Language(request.TargetLanguageCode, profile);
        var instruction = profile.TranslationSystemPrompt ?? "Translate the following text from {{source}} into {{target}}.";
        var rendered = PromptTemplateRenderer.Render(instruction, new Dictionary<string, string> { { "source", source }, { "target", target } });
        return new("", $"{rendered}\n{source}: {request.Text}\n{target}:");
    }
    internal static void RejectDelimiters(string text, params string[] delimiters)
    {
        if (delimiters.Any(text.Contains)) throw new NotSupportedException("Source text contains native model control tokens and cannot safely use this formatter.");
    }
    public string NormalizeTranslation(string result) => result.Trim();
}
