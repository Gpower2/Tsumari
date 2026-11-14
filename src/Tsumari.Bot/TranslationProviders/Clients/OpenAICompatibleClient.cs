using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tsumari.Bot.TranslationProviders.Models;
using Tsumari.Bot.TranslationProviders.Requests;
namespace Tsumari.Bot.TranslationProviders.Clients;

public sealed class OpenAICompatibleClient(IHttpClientFactory clients) : ILlmClient
{
    public async Task<ModelResponse> GenerateAsync(ModelProfile p, PreparedModelRequest r, TranslationGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?> { ["model"] = p.Model, ["messages"] = new[] { new { role = "system", content = r.System }, new { role = "user", content = r.Prompt } }, ["temperature"] = options?.Temperature ?? p.Temperature };
        if (options is not null) payload["top_p"] = options.TopP;
        // Existing OpenAI requests had no extra decoding fields.
        if (p.MaxOutputTokens is not null) payload["max_tokens"] = p.MaxOutputTokens;
        if (!p.Stops.IsEmpty) payload["stop"] = p.Stops;
        if (!r.Legacy)
        {
            if (p.TopP is not null) payload["top_p"] = options?.TopP ?? p.TopP;
            if (r.Json) payload["response_format"] = new { type = "json_object" };
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, p.Endpoint) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        if (!string.IsNullOrWhiteSpace(p.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", p.ApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(p.TimeoutSeconds));
        using var client = clients.CreateClient(HttpClientNames.OpenAITranslation); client.Timeout = TimeSpan.FromSeconds(p.TimeoutSeconds);
        using var response = await client.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token); using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) throw new InvalidOperationException("OpenAI response contained no choices.");
        var choice = choices[0]; var reason = choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() : null;
        if (reason is "length" or "content_filter") throw new InvalidOperationException("OpenAI returned an incomplete generation.");
        if (!choice.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var text) || text.ValueKind != JsonValueKind.String) throw new InvalidOperationException("OpenAI response contained no message text.");
        return new(text.GetString() ?? "", reason);
    }
}
