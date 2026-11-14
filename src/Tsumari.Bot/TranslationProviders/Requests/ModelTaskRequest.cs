namespace Tsumari.Bot.TranslationProviders.Requests;

public sealed record ModelTaskRequest(string Text, bool IsAnalysis = false, string? TargetLanguageCode = null, string? SourceLanguageCode = null);
