namespace Tsumari.Bot.Configuration;

public static class LlmOptionsValidator
{
    public static void Validate(string name, ModelProfileOptions p)
    {
        if (p.IntendedTasks is null || p.Stops is null || p.Languages is null) throw new InvalidOperationException($"Profile collections cannot be null: {name}.");
        if (string.IsNullOrWhiteSpace(p.Model)) throw new InvalidOperationException($"Profile '{name}' needs a model.");
        if (p.Formatter is not ("translategemma" or "salamandra" or "tiny-aya")) throw new InvalidOperationException($"Unknown formatter '{p.Formatter}'.");
        if (p.Transport is not ("Ollama" or "OpenAI")) throw new InvalidOperationException($"Unknown transport '{p.Transport}'.");
        if (!Uri.TryCreate(p.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidOperationException($"Invalid endpoint for '{name}'.");
        if (p.ContextLength <= 0 || p.MaxOutputTokens <= 0 || !double.IsFinite(p.Temperature) || p.Temperature < 0 || (p.TopP is double topP && (!double.IsFinite(topP) || topP < 0 || topP > 1)) || p.TopK is <= 0 || !double.IsFinite(p.TimeoutSeconds) || p.TimeoutSeconds <= 0) throw new InvalidOperationException($"Invalid generation settings for '{name}'.");
        if (p.IntendedTasks.Length == 0 || p.IntendedTasks.Any(x => x is not ("Analysis" or "Translation"))) throw new InvalidOperationException($"Invalid intended tasks for '{name}'.");
        if (p.Transport == "OpenAI" && p.TopK is not null) throw new InvalidOperationException("OpenAI-compatible transport does not support top_k.");
        if (p.SupportsRetry && p.TopK == 1) throw new InvalidOperationException("Retry settings cannot vary generation while top_k=1 forces greedy decoding.");
        if (p.Formatter == "salamandra" && p.IntendedTasks.Contains("Analysis")) throw new InvalidOperationException("Salamandra is configured for translation only.");
        if (p.Formatter == "translategemma" && p.TranslationSystemPrompt is not null) throw new InvalidOperationException("Existing TranslateGemma prompts cannot be overridden.");
    }
}
