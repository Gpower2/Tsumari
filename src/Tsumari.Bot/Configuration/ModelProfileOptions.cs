namespace Tsumari.Bot.Configuration;

public sealed class ModelProfileOptions
{
    public string Model { get; set; } = "";
    public string Formatter { get; set; } = "";
    public string Transport { get; set; } = "Ollama";
    public string Endpoint { get; set; } = "http://localhost:11434/api/generate";
    public string? ApiKey { get; set; }
    public string KeepAlive { get; set; } = "15m";
    public int ContextLength { get; set; } = 4096;
    public int? MaxOutputTokens { get; set; }
    public double Temperature { get; set; } = 0;
    public double? TopP { get; set; } = .1;
    public int? TopK { get; set; }
    public string[] Stops { get; set; } = [];
    public string[] IntendedTasks { get; set; } = ["Translation"];
    public bool SupportsRetry { get; set; }
    public string PromptRevision { get; set; } = "1";
    public string? TranslationSystemPrompt { get; set; }
    public double TimeoutSeconds { get; set; } = 180;
    public Dictionary<string, string> Languages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
