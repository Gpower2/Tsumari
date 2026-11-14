using Microsoft.Extensions.Configuration;
using System.Net.Http;
using Tsumari.Bot.Configuration;
using Tsumari.Bot.TranslationProviders.Clients;
using Tsumari.Bot.TranslationProviders.Formatting;
namespace Tsumari.Bot.TranslationProviders.Models;

public sealed class LlmModelFactory(IConfiguration configuration, IHttpClientFactory clients)
{
    public void ValidateConfiguration()
    {
        foreach (var section in configuration.GetSection("Translation:Llm:Profiles").GetChildren())
            Create(section.Key);
    }

    public LlmModel Create(string profileName)
    {
        var section = configuration.GetSection($"Translation:Llm:Profiles:{profileName}");
        if (!section.Exists()) throw new InvalidOperationException($"Model profile '{profileName}' does not exist.");
        var options = section.Get<ModelProfileOptions>() ?? throw new InvalidOperationException($"Invalid profile '{profileName}'.");
        return Create(ModelProfile.FromOptions(profileName, options));
    }
    public LlmModel Create(ModelProfile profile)
    {
        LlmOptionsValidator.Validate(profile.Name, new ModelProfileOptions { Model = profile.Model, Formatter = profile.Formatter, Transport = profile.Transport, Endpoint = profile.Endpoint, ContextLength = profile.ContextLength, MaxOutputTokens = profile.MaxOutputTokens, Temperature = profile.Temperature, TopP = profile.TopP, TopK = profile.TopK, TimeoutSeconds = profile.TimeoutSeconds, IntendedTasks = profile.IntendedTasks.ToArray(), Stops = profile.Stops.ToArray(), Languages = profile.Languages.ToDictionary(x => x.Key, x => x.Value), TranslationSystemPrompt = profile.TranslationSystemPrompt, SupportsRetry = profile.SupportsRetry });
        IModelFormatter formatter = profile.Formatter switch { "translategemma" => new TranslateGemmaFormatter(), "salamandra" => new SalamandraFormatter(), "tiny-aya" => new TinyAyaFormatter(), _ => throw new InvalidOperationException($"Unknown formatter '{profile.Formatter}'.") };
        ILlmClient client = profile.Transport switch { "Ollama" => new OllamaClient(clients), "OpenAI" => new OpenAICompatibleClient(clients), _ => throw new InvalidOperationException($"Unknown transport '{profile.Transport}'.") };
        return new(profile, formatter, client);
    }
    public (LlmModel Analysis, LlmModel Translation)? CreateSelected(string transport)
    {
        var analysis = configuration["Translation:Llm:AnalysisProfile"]; var translation = configuration["Translation:Llm:TranslationProfile"];
        if (analysis is null && translation is null) return null;
        if (string.IsNullOrWhiteSpace(analysis) || string.IsNullOrWhiteSpace(translation)) throw new InvalidOperationException("Both analysis and translation profiles must be selected.");
        var a = Create(analysis); var t = analysis == translation ? a : Create(translation);
        if (a.Profile.Transport != transport || t.Profile.Transport != transport) throw new InvalidOperationException("Selected model profiles must match the selected provider transport.");
        if (!a.Profile.IntendedTasks.Contains("Analysis") || !t.Profile.IntendedTasks.Contains("Translation")) throw new InvalidOperationException("Selected profiles do not support their assigned tasks.");
        return (a, t);
    }
}
