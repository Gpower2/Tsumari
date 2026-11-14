namespace Tsumari.Bot.TranslationProviders.Formatting;

public static class PromptTemplateRenderer
{
    // Replace placeholders once; inserted text is never interpreted as another template.
    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
     System.Text.RegularExpressions.Regex.Replace(template, @"\{\{([A-Za-z]+)\}\}", m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : throw new InvalidOperationException($"Unknown placeholder '{m.Value}'."));
}
