using System.Net;
using System.Text.Json.Nodes;
using Tsumari.Repair;

public class DiscordApiTests
{
    [Fact]
    public async Task EditOnlyPatchesContentAndSuppressesMentions()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{\"id\":\"2\"}");
        using var api = new DiscordApi("test-token", true, CancellationToken.None, handler);
        await api.Edit("1", "2", "hello @everyone");
        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Equal("/api/v10/channels/1/messages/2", handler.Path);
        var body = JsonNode.Parse(handler.Body!)!.AsObject();
        Assert.Equal(2, body.Count);
        Assert.Equal("hello @everyone", body["content"]!.GetValue<string>());
        Assert.Empty(body["allowed_mentions"]!["parse"]!.AsArray());
        Assert.False(body["allowed_mentions"]!["replied_user"]!.GetValue<bool>());
        Assert.False(body.ContainsKey("attachments"));
        Assert.False(body.ContainsKey("components"));
    }

    [Fact]
    public async Task DoesNotBlindlyRetryUncertainEdits()
    {
        using var handler = new RecordingHandler(HttpStatusCode.BadGateway, "{}");
        using var api = new DiscordApi("test-token", true, CancellationToken.None, handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.Edit("1", "2", "hello"));
        Assert.Equal(1, handler.Requests);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }
}
