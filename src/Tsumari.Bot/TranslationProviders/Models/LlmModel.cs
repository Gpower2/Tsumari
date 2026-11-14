using Tsumari.Bot.TranslationProviders.Clients;
using Tsumari.Bot.TranslationProviders.Formatting;
using Tsumari.Bot.TranslationProviders.Parsing;
using Tsumari.Bot.TranslationProviders.Requests;
namespace Tsumari.Bot.TranslationProviders.Models;

public sealed class LlmModel(ModelProfile profile, IModelFormatter formatter, ILlmClient client)
{
    public ModelProfile Profile { get; } = profile;
    public async Task<LanguageAnalysisResult> AnalyzeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!Profile.IntendedTasks.Contains("Analysis") || !formatter.SupportsAnalysis) throw new NotSupportedException($"Profile '{Profile.Name}' cannot analyze languages.");
        var response = await client.GenerateAsync(Profile, formatter.Prepare(new(text, true), Profile), cancellationToken: cancellationToken);
        return LanguageAnalysisResponseParser.Parse(response.Text);
    }
    public async Task<string> TranslateAsync(string text, string targetLanguageCode, string? sourceLanguageCode = null, TranslationGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!Profile.IntendedTasks.Contains("Translation")) throw new NotSupportedException($"Profile '{Profile.Name}' cannot translate.");
        if (string.IsNullOrWhiteSpace(targetLanguageCode)) throw new ArgumentException("A target language is required.", nameof(targetLanguageCode));
        if (options is not null && !Profile.SupportsRetry) throw new NotSupportedException($"Profile '{Profile.Name}' has no qualified retry settings.");
        var prepared = formatter.Prepare(new(text, false, targetLanguageCode, sourceLanguageCode), Profile);
        var response = await client.GenerateAsync(Profile, prepared, options, cancellationToken);
        var result = formatter.NormalizeTranslation(response.Text);
        if (!prepared.Legacy && string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException($"Profile '{Profile.Name}' returned an empty translation.");
        return result;
    }
}
