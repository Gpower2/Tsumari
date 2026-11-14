namespace Tsumari.Bot.TranslationProviders.Requests;

public sealed record PreparedModelRequest(string System, string Prompt, bool Json = false, bool Legacy = false);
