using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using Tsumari.Bot.Models;
using Tsumari.Bot.TranslationProviders.Models;

namespace Tsumari.Bot.Configuration;

/// <summary>Deployment preflight only. Never constructs the bot or opens a database.</summary>
public static class DeploymentConfigurationValidator
{
    public static string Validate(IConfiguration configuration, string releaseDirectory)
    {
        var token = configuration["Discord:Token"] ?? configuration["DiscordToken"];
        if (string.IsNullOrWhiteSpace(token) || token.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)
            || token.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Discord:Token (or DiscordToken) must contain a real, nonempty bot token.");

        var providerValue = configuration["Translation:Provider"];
        if (!Enum.TryParse<TranslationProvider>(providerValue, true, out var provider) || !Enum.IsDefined(provider))
            throw new InvalidOperationException("Translation:Provider must be DeepL, Ollama or OpenAI (or its defined numeric value).");

        var factory = new LlmModelFactory(configuration, new NoNetworkClientFactory());
        foreach (var section in configuration.GetSection("Translation:Llm:Profiles").GetChildren())
        {
            foreach (var key in new[] { "Temperature", "TopP", "TopK", "ContextLength", "MaxOutputTokens", "TimeoutSeconds" })
            {
                var value = section[key];
                if (value is null) continue;
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number)
                    || (key == "Temperature" && number < 0)
                    || (key == "TopP" && (number < 0 || number > 1))
                    || (key is "TopK" or "ContextLength" or "MaxOutputTokens" && (number <= 0 || number != Math.Truncate(number)))
                    || (key == "TimeoutSeconds" && number <= 0))
                    throw new InvalidOperationException($"{section.Path}:{key} has an invalid numeric value.");
            }
            try { factory.Create(section.Key); }
            catch (Exception error) { throw new InvalidOperationException($"{section.Path}: {error.Message}"); }
        }

        if (provider == TranslationProvider.DeepL)
            Require(configuration["DeepL:ApiKey"] ?? configuration["DeepLKey"], "DeepL:ApiKey (or DeepLKey)");
        else
        {
            var transport = provider == TranslationProvider.Ollama ? "Ollama" : "OpenAI";
            var selected = factory.CreateSelected(transport);
            if (selected is null)
            {
                Require(configuration[$"Translation:{transport}:Model"], $"Translation:{transport}:Model");
                var endpoint = configuration[$"Translation:{transport}:ApiUrl"];
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new InvalidOperationException($"Translation:{transport}:ApiUrl must be an absolute HTTP(S) URL.");
            }
        }

        var rest = new DiscordRestOptions();
        configuration.GetSection("Discord:Rest").Bind(rest);
        if (rest.MessageTimeout <= TimeSpan.Zero) throw new InvalidOperationException("Discord:Rest:MessageTimeout must be positive.");
        if (rest.FileUploadTimeout <= TimeSpan.Zero) throw new InvalidOperationException("Discord:Rest:FileUploadTimeout must be positive.");
        if (rest.MaxRetries < 0) throw new InvalidOperationException("Discord:Rest:MaxRetries cannot be negative.");
        if (rest.RetryDelay < TimeSpan.Zero) throw new InvalidOperationException("Discord:Rest:RetryDelay cannot be negative.");
        var retry = new TranslationRetryOptions();
        retry.BindConfiguration(configuration.GetSection("Translation:Retry"));
        if (retry.Temperatures is null) throw new InvalidOperationException("Translation:Retry:Temperatures cannot be null.");
        if (retry.Temperatures.Length is < 1 or > 10) throw new InvalidOperationException("Translation:Retry:Temperatures must have between 1 and 10 entries.");
        for (var i = 0; i < retry.Temperatures.Length; i++)
            if (!double.IsFinite(retry.Temperatures[i]) || retry.Temperatures[i] < 0 || retry.Temperatures[i] > 0.60
                || (i > 0 && retry.Temperatures[i] < retry.Temperatures[i - 1]))
                throw new InvalidOperationException($"Translation:Retry:Temperatures:{i} must be finite, between 0 and 0.60, and nondecreasing.");
        if (!double.IsFinite(retry.TopP) || retry.TopP <= 0 || retry.TopP > 1) throw new InvalidOperationException("Translation:Retry:TopP must be finite, greater than 0 and at most 1.");
        if (retry.CooldownSeconds < 0) throw new InvalidOperationException("Translation:Retry:CooldownSeconds cannot be negative.");
        if (retry.QueueCapacity is < 1 or > 100) throw new InvalidOperationException("Translation:Retry:QueueCapacity must be between 1 and 100.");
        retry.Validate();

        var path = configuration["Database:FilePath"] ?? "tsumari.db";
        if (string.IsNullOrWhiteSpace(path) || path == ":memory:")
            throw new InvalidOperationException("Database:FilePath must identify a persistent SQLite file.");
        string fullPath;
        try { fullPath = Path.GetFullPath(path, Path.GetFullPath(releaseDirectory)); }
        catch (Exception) { throw new InvalidOperationException("Database:FilePath is not a valid file path."); }
        if (Directory.Exists(fullPath)) throw new InvalidOperationException("Database:FilePath points to a directory rather than a file.");
        return fullPath;
    }

    public static void ValidateArtifact(string releaseDirectory, string reportPath)
    {
        // Same environment/user-secret precedence as normal startup; final CWD will be releaseDirectory.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: false);
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: false);
        if (builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>(optional: true);
        builder.Configuration.AddEnvironmentVariables();
        var database = Validate(builder.Configuration, releaseDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (new[] { "", "-wal", "-shm", "-journal" }.Any(suffix => string.Equals(Path.GetFullPath(reportPath), database + suffix, comparison)))
            throw new InvalidOperationException("Validation report path must not be the database or a SQLite sidecar.");
        using var report = File.Open(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        System.Text.Json.JsonSerializer.Serialize(report, new { DatabaseFilePath = database, Environment = builder.Environment.EnvironmentName });
    }

    private static void Require(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{key} is required and cannot be empty.");
    }
    private sealed class NoNetworkClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Network access is forbidden during deployment validation.");
    }
}
