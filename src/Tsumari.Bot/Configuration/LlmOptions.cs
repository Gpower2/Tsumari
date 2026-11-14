namespace Tsumari.Bot.Configuration;

public sealed class LlmOptions
{
    public string? AnalysisProfile { get; set; }
    public string? TranslationProfile { get; set; }
    public Dictionary<string, ModelProfileOptions> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
