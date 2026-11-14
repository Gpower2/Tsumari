using Microsoft.Extensions.Configuration;
using Tsumari.Bot.Configuration;

namespace Tsumari.Bot.Tests.Unit;

public sealed class DeploymentConfigurationValidatorTests
{
    [Fact]
    public void ConfiguredRetryScheduleReplacesDefaultInsteadOfBeingAppended()
    {
        var section = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Temperatures:0"] = "0.2", ["Temperatures:1"] = "0.3"
        }).Build();
        var options = new TranslationRetryOptions();
        options.BindConfiguration(section);
        Assert.Equal(new[] { 0.2, 0.3 }, options.Temperatures);
        options.Validate();
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Discord:Token"] = "synthetic-test-token",
        ["Translation:Provider"] = "Ollama",
        ["Translation:Ollama:Model"] = "translategemma:12b",
        ["Translation:Ollama:ApiUrl"] = "http://127.0.0.1:9/api/generate",
        ["Database:FilePath"] = "data/custom-store"
    };

    [Fact]
    public void ResolvesDatabaseAtReleaseWithoutCreatingFilesOrCallingUnreachableEndpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var result = DeploymentConfigurationValidator.Validate(new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build(), root);
        Assert.Equal(Path.Combine(root, "data", "custom-store"), result);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("Discord:Token", "", "Discord:Token")]
    [InlineData("Translation:Provider", "typo", "Translation:Provider")]
    [InlineData("Translation:Ollama:Model", "", "Translation:Ollama:Model")]
    [InlineData("Translation:Ollama:ApiUrl", "relative/path", "Translation:Ollama:ApiUrl")]
    [InlineData("Database:FilePath", ":memory:", "Database:FilePath")]
    [InlineData("Discord:Rest:MessageTimeout", "00:00:00", "Discord:Rest:MessageTimeout")]
    [InlineData("Translation:Retry:TopP", "2", "Translation:Retry:TopP")]
    [InlineData("Translation:Retry:Temperatures:0", "0.9", "Translation:Retry:Temperatures:0")]
    public void ReportsInvalidSettingWithoutExposingToken(string key, string value, string expected)
    {
        var settings = Settings();
        settings[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() => DeploymentConfigurationValidator.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), Path.GetTempPath()));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("synthetic-test-token", error.Message);
    }

    [Fact]
    public void ReportsExactProfileNumericFieldBeforeAnyNetworkAccess()
    {
        var settings = Settings();
        settings["Translation:Llm:Profiles:test:Temperature"] = "NaN";
        var error = Assert.Throws<InvalidOperationException>(() => DeploymentConfigurationValidator.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), Path.GetTempPath()));
        Assert.Contains("Translation:Llm:Profiles:test:Temperature", error.Message);
    }

    [Fact]
    public void MissingSelectedProfileIsRejectedRatherThanSilentlyFallingBack()
    {
        var settings = Settings();
        settings["Translation:Llm:AnalysisProfile"] = "missing";
        settings["Translation:Llm:TranslationProfile"] = "missing";
        var error = Assert.Throws<InvalidOperationException>(() => DeploymentConfigurationValidator.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), Path.GetTempPath()));
        Assert.Contains("missing", error.Message);
    }
}
