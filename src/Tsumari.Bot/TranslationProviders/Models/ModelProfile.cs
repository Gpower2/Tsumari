using System.Collections.Immutable;
using Tsumari.Bot.Configuration;
namespace Tsumari.Bot.TranslationProviders.Models;

public sealed record ModelProfile
{
    public required string Name { get; init; }
    public required string Model { get; init; }
    public required string Formatter { get; init; }
    public required string Transport { get; init; }
    public required string Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string KeepAlive { get; init; } = "15m";
    public int ContextLength { get; init; } = 4096;
    public int? MaxOutputTokens { get; init; }
    public double Temperature { get; init; }
    public double? TopP { get; init; }
    public int? TopK { get; init; }
    public ImmutableArray<string> Stops { get; init; } = [];
    public ImmutableArray<string> IntendedTasks { get; init; } = [];
    public bool SupportsRetry { get; init; }
    public string PromptRevision { get; init; } = "1";
    public string? TranslationSystemPrompt { get; init; }
    public double TimeoutSeconds { get; init; } = 180;
    public ImmutableDictionary<string, string> Languages { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
    public static ModelProfile FromOptions(string name, ModelProfileOptions p)
    {
        LlmOptionsValidator.Validate(name, p);
        return new() { Name = name, Model = p.Model, Formatter = p.Formatter, Transport = p.Transport, Endpoint = p.Endpoint, ApiKey = p.ApiKey, KeepAlive = p.KeepAlive, ContextLength = p.ContextLength, MaxOutputTokens = p.MaxOutputTokens, Temperature = p.Temperature, TopP = p.TopP, TopK = p.TopK, Stops = p.Stops.ToImmutableArray(), IntendedTasks = p.IntendedTasks.ToImmutableArray(), SupportsRetry = p.SupportsRetry, PromptRevision = p.PromptRevision, TranslationSystemPrompt = p.TranslationSystemPrompt, TimeoutSeconds = p.TimeoutSeconds, Languages = p.Languages.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase) };
    }
}
