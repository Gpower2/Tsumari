using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using System.Text.Json;
using Tsumari.Bot.Configuration;
using Tsumari.Bot.TranslationProviders.Clients;
using Tsumari.Bot.TranslationProviders.Models;
using Tsumari.Bot.TranslationProviders.Requests;

namespace Tsumari.Bot.Tests.Unit;

public sealed class LlmModelFactoryTests
{
    [Theory]
    [InlineData("tiny-aya", false)]
    [InlineData("translategemma", true)]
    public async Task EmptyAlternativeOutputFailsWhileBaselineBehaviourIsPreserved(string formatterName, bool legacy)
    {
        var handler = new Handler("{\"response\":\"   \",\"done\":true,\"done_reason\":\"stop\"}");
        var profile = ModelProfile.FromOptions("test", new() { Model = "test", Formatter = formatterName });
        var model = new LlmModelFactory(new ConfigurationBuilder().Build(), ClientFactory(handler)).Create(profile);
        if (legacy)
            Assert.Equal(string.Empty, await model.TranslateAsync("hello", "DE", "EN"));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => model.TranslateAsync("hello", "DE", "EN"));
    }

    [Fact]
    public void ShippedProfilesValidateWithoutActivatingAlternativesOrChangingBaselineDecoding()
    {
        using var stream = typeof(LlmModelFactoryTests).Assembly.GetManifestResourceStream("ShippedModelProfiles.json")!;
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var factory = new LlmModelFactory(configuration, Mock.Of<IHttpClientFactory>());
        factory.ValidateConfiguration();
        Assert.Null(factory.CreateSelected("Ollama"));
        var baseline = factory.Create("translategemma-12b").Profile;
        Assert.Equal("translategemma:12b", baseline.Model);
        Assert.Null(baseline.MaxOutputTokens);
        Assert.Equal(0, baseline.Temperature);
        Assert.Equal(.1, baseline.TopP);
        Assert.Equal(4096, baseline.ContextLength);
        Assert.False(factory.Create("salamandra-v3-q4").Profile.SupportsRetry);
        Assert.False(factory.Create("tiny-aya-global-q8").Profile.SupportsRetry);
    }

    [Fact]
    public void MissingProfileOrPartialSelectionFailsRatherThanFallingBack()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Translation:Llm:AnalysisProfile"] = "missing" }).Build();
        var factory = new LlmModelFactory(config, Mock.Of<IHttpClientFactory>());
        Assert.Throws<InvalidOperationException>(() => factory.Create("missing"));
        Assert.Throws<InvalidOperationException>(() => factory.CreateSelected("Ollama"));
    }

    [Fact]
    public void NoExplicitSelectionsKeepsLegacyProviderPath()
    {
        var factory = new LlmModelFactory(new ConfigurationBuilder().Build(), Mock.Of<IHttpClientFactory>());
        Assert.Null(factory.CreateSelected("Ollama"));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidSamplingFailsStartupValidation(double topP)
    {
        Assert.Throws<InvalidOperationException>(() => ModelProfile.FromOptions("bad", new() { Model = "test", Formatter = "tiny-aya", TopP = topP }));
    }

    [Fact]
    public async Task UnqualifiedRetriesFailBeforeContactingEndpoint()
    {
        var factory = new LlmModelFactory(new ConfigurationBuilder().Build(), Mock.Of<IHttpClientFactory>(MockBehavior.Strict));
        var model = factory.Create(ModelProfile.FromOptions("candidate", new() { Model = "test", Formatter = "tiny-aya" }));
        await Assert.ThrowsAsync<NotSupportedException>(() => model.TranslateAsync("hello", "DE", "EN", new(0.5, 0.9)));
    }

    [Theory]
    [InlineData("{\"response\":\"partial\",\"done\":true,\"done_reason\":\"length\"}")]
    [InlineData("{\"response\":\"partial\",\"done\":false}")]
    [InlineData("{\"done\":true}")]
    public async Task OllamaRejectsIncompleteOrMissingText(string response)
    {
        var handler = new Handler(response);
        var factory = ClientFactory(handler);
        var client = new OllamaClient(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync(Profile(), new("system", "text")));
    }

    [Theory]
    [InlineData("length")]
    [InlineData("content_filter")]
    public async Task OpenAIRejectsIncompleteCompletions(string reason)
    {
        var handler = new Handler(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = reason, message = new { content = "partial" } } } }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAICompatibleClient(ClientFactory(handler)).GenerateAsync(Profile() with { Transport = "OpenAI" }, new("system", "text")));
    }

    [Fact]
    public void OpenAIUnsupportedTopKIsRejectedRatherThanIgnored()
    {
        Assert.Throws<InvalidOperationException>(() => ModelProfile.FromOptions("bad", new() { Model = "test", Formatter = "tiny-aya", Transport = "OpenAI", TopK = 4 }));
    }

    [Fact]
    public async Task ExplicitOpenAIRetryForwardsBothSamplingOverrides()
    {
        var handler = new Handler("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Hallo\"}}]}");
        await new OpenAICompatibleClient(ClientFactory(handler)).GenerateAsync(Profile() with { Transport = "OpenAI" }, new("system", "text", false, true), new(.5, .8));
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(.5, json.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(.8, json.RootElement.GetProperty("top_p").GetDouble());
        Assert.False(json.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task ExplicitOpenAIProfileOutputLimitsAndStopsApplyToLegacyFormatter()
    {
        var handler = new Handler("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Hallo\"}}]}");
        await new OpenAICompatibleClient(ClientFactory(handler)).GenerateAsync(Profile() with { Transport = "OpenAI", MaxOutputTokens = 123, Stops = ["END"] }, new("system", "text", false, true));
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(123, json.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("END", json.RootElement.GetProperty("stop")[0].GetString());
        Assert.False(json.RootElement.TryGetProperty("top_p", out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("DeepL")]
    [InlineData("2")]
    [InlineData("OpenAI")]
    public void NonOllamaProviderDoesNotActivateOllamaProfiles(string provider)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Translation:Provider"] = provider,
            ["Translation:Llm:AnalysisProfile"] = "missing",
            ["Translation:Llm:TranslationProfile"] = "missing"
        }).Build();
        var instance = new OllamaTranslationProvider(config, Mock.Of<IHttpClientFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaTranslationProvider>.Instance);
        Assert.True(instance.IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OllamaKeepsLegacyConcatenationButSeparatesAlternativeRoles(bool legacy)
    {
        var handler = new Handler("{\"response\":\"Hallo\",\"done\":true,\"done_reason\":\"stop\"}");
        await new OllamaClient(ClientFactory(handler)).GenerateAsync(Profile(), new("system", "text", true, legacy));
        using var json = JsonDocument.Parse(handler.Body!);
        var payload = json.RootElement;
        Assert.Equal(legacy ? "system\n\ntext" : "text", payload.GetProperty("prompt").GetString());
        Assert.Equal(!legacy, payload.TryGetProperty("system", out var system));
        if (!legacy) Assert.Equal("system", system.GetString());
        Assert.Equal("json", payload.GetProperty("format").GetString());
        Assert.Equal(4096, payload.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(0, payload.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal(.1, payload.GetProperty("options").GetProperty("top_p").GetDouble());
        Assert.False(payload.GetProperty("stream").GetBoolean());
    }

    private static ModelProfile Profile() => ModelProfile.FromOptions("test", new() { Model = "test", Formatter = "tiny-aya" });
    private static IHttpClientFactory ClientFactory(Handler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        return factory.Object;
    }
    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
