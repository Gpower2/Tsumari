using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tsumari.Bot.Tests.Component;

public sealed class RetryProviderIntegrationTests
{
    [Fact]
    public async Task SharedProvider_ConcurrentRetryNormalAndAnalysis_UseIsolatedHttpSettings()
    {
        using var db = new TemporarySqliteDatabase("retry-provider");
        var database = db.CreateDatabaseService();
        await database.InitializeDatabaseAsync();
        using var handler = new Handler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(HttpClientNames.OllamaTranslation)).Returns(() => new HttpClient(handler, disposeHandler: false));
        var provider = new OllamaTranslationProvider(new ConfigurationBuilder().Build(), factory.Object, NullLogger<OllamaTranslationProvider>.Instance);
        var service = new TranslationService(database, provider, NullLogger<TranslationService>.Instance, NullLoggerFactory.Instance);
        await Task.WhenAll(
            service.RetryTranslationAsync("hello", "fr", "en", new(.45, .8)),
            service.TranslateTextAsync("hello", "fr", "en"),
            service.AnalyzeLanguageAsync("hello"));
        var requests = handler.Requests.ToArray();
        Assert.Equal(3, requests.Length);
        Assert.Single(requests, r => r.GetProperty("options").GetProperty("temperature").GetDouble() == .45 && r.GetProperty("options").GetProperty("top_p").GetDouble() == .8);
        Assert.Equal(2, requests.Count(r => r.GetProperty("options").GetProperty("temperature").GetDouble() == 0 && r.GetProperty("options").GetProperty("top_p").GetDouble() == .1));
        Assert.Single(requests, r => r.TryGetProperty("format", out var f) && f.GetString() == "json");
    }

    private sealed class Handler : HttpMessageHandler
    {
        public ConcurrentBag<JsonElement> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement.Clone(); Requests.Add(root);
            await Task.Yield();
            var response = root.TryGetProperty("format", out _) ? "{\"dominantLanguageCode\":\"EN\",\"languages\":[\"EN\"],\"hasClearDominantLanguage\":true}" : "Bonjour";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { response }), Encoding.UTF8, "application/json") };
        }
    }
}
