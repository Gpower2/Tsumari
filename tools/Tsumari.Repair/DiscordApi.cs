using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Tsumari.Repair;

public sealed class DiscordApi : IDisposable
{
    private readonly HttpClient client;
    private readonly CancellationToken cancellation;
    private readonly bool allowEdits;
    public DiscordApi(string token, bool allowEdits, CancellationToken cancellation, HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = new Uri("https://discord.com/api/v10/");
        client.Timeout = TimeSpan.FromSeconds(40);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TsumariRepair/1.0");
        this.allowEdits = allowEdits;
        this.cancellation = cancellation;
    }
    public async Task<JsonNode?> Get(string path) => await Request(HttpMethod.Get, path, null);
    public async Task<JsonObject> Edit(string channel, string id, string content)
    {
        if (!allowEdits) throw new InvalidOperationException("Edits disabled in this command.");
        // Omit attachments/components so Discord retains them. Never notify mentions.
        return (JsonObject)(await Request(HttpMethod.Patch, $"channels/{channel}/messages/{id}",
            new { content, allowed_mentions = new { parse = Array.Empty<string>(), replied_user = false } }))!;
    }
    private async Task<JsonNode?> Request(HttpMethod method, string path, object? body)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(400, cancellation);
            using var request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, cancellation);
            var text = await response.Content.ReadAsStringAsync(cancellation);
            if (response.StatusCode == HttpStatusCode.NotFound && method == HttpMethod.Get) return null;
            if ((int)response.StatusCode == 429)
            {
                var seconds = JsonNode.Parse(text)?["retry_after"]?.GetValue<double>() ?? 5;
                Console.WriteLine($"Discord rate limit: waiting {seconds:F1}s");
                await Task.Delay(TimeSpan.FromSeconds(seconds + 1), cancellation);
                continue;
            }
            // Retry reads only. An uncertain PATCH is recovered by inspecting live content.
            if ((int)response.StatusCode >= 500 && method == HttpMethod.Get)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellation);
                continue;
            }
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Discord {method} {path}: HTTP {(int)response.StatusCode}", null, response.StatusCode);
            return JsonNode.Parse(text);
        }
        throw new HttpRequestException("Discord retry limit exceeded.");
    }
    public void Dispose() => client.Dispose();
}
