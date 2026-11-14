using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tsumari.Bot.TranslationProviders.Models;
using Tsumari.Bot.TranslationProviders.Requests;
namespace Tsumari.Bot.TranslationProviders.Clients;

public sealed class OllamaClient(IHttpClientFactory clients) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public async Task<ModelResponse> GenerateAsync(ModelProfile p, PreparedModelRequest r, TranslationGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Legacy omits system and preserves the exact historic concatenation.
        var payload = new { model = p.Model, prompt = r.Legacy ? $"{r.System}\n\n{r.Prompt}" : r.Prompt, system = r.Legacy ? null : r.System, stream = false, format = r.Json ? "json" : null, keep_alive = string.IsNullOrWhiteSpace(p.KeepAlive) ? null : p.KeepAlive, options = new { temperature = options?.Temperature ?? p.Temperature, top_p = options?.TopP ?? p.TopP, top_k = p.TopK, num_ctx = p.ContextLength, num_predict = p.MaxOutputTokens, stop = p.Stops.IsEmpty ? null : p.Stops.ToArray() } };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(p.TimeoutSeconds));
        using var client = clients.CreateClient(HttpClientNames.OllamaTranslation);
        client.Timeout = TimeSpan.FromSeconds(p.TimeoutSeconds);
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(p.Endpoint, content, timeout.Token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
        if (!root.TryGetProperty("response", out var value) || value.ValueKind != JsonValueKind.String) throw new InvalidOperationException("Ollama response did not contain text.");
        string? reason = root.TryGetProperty("done_reason", out var done) ? done.GetString() : null;
        if (reason == "length" || (root.TryGetProperty("done", out var complete) && complete.ValueKind == JsonValueKind.False)) throw new InvalidOperationException("Ollama returned an incomplete generation.");
        return new(value.GetString() ?? "", reason);
    }
}
