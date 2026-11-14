# Configuration and private settings

Requires .NET 10 and PowerShell 7. See [README](../README.md) for a minimal local override and [deployment](DEPLOYMENT.md) for updating a running installation.

## Files and build precedence

Build/publish runs `build/LocalSettings.targets` and `build/Merge-AppSettings.ps1`, merging in increasing precedence:

1. `src/Tsumari.Bot/appsettings.json`, if present (ignored).
2. `src/Tsumari.Bot/appsettings.models.json` (tracked model catalogue, no credentials).
3. `.local/appsettings.secrets.json`, if present (ignored complete local override, including credentials).

The resulting `appsettings.json` is written to build/publish output. Source and existing release settings are not rewritten by an isolated build. `LocalSecretsPath` overrides the private input location, for example `dotnet build src/Tsumari.Bot/Tsumari.Bot.csproj --artifacts-path .artifacts/build -p:LocalSecretsPath=/path/to/private-settings.json`.

Builds can omit private settings; deployment requires the file and validates the final effective configuration. Never commit generated settings or distribute a publish folder without checking its credentials. Local settings can include all existing runtime values, not only secrets.

## Runtime precedence and working directory

Startup loads the host's normal settings, then the merged output `appsettings.json`, source-content-root environment settings, output environment settings, development user secrets when applicable, environment variables, and command-line overrides. Later providers take precedence. Build injection prepares base settings; it does not suppress runtime environment overrides.

The program keeps its content root. `Database:FilePath` is resolved by `Path.GetFullPath` from the **process working directory**. Deployment deliberately starts in the release folder. For development use a separate explicit absolute database path, or run from the appropriate output folder; avoid accidentally creating a second relative `tsumari.db` elsewhere.

`dotnet run` reads merged build output settings too, but it commonly runs with the project directory as working directory. Do not use it casually against a live production database. Builds/deployment preflight alone do not start the bot.

## Backend settings

`Translation:Provider` accepts `DeepL`, `Ollama`, or `OpenAI` (also defined enum numeric values). Normal startup falls back to Ollama if the selector is missing/invalid; deployment validation rejects an invalid/missing explicit selector instead of deploying that mistake.

- Ollama legacy settings: `Translation:Ollama:ApiUrl`, `Model`, optional `KeepAlive`. Code fallbacks are `http://localhost:11434/api/generate`, **`aya:8b`**, and `15m`. The recommended local override and evaluated baseline explicitly select **`translategemma:12b`**; it is not a hard-coded model default.
- OpenAI-compatible legacy settings: `Translation:OpenAI:ApiUrl`, `Model`, `ApiKey`; code fallbacks are `http://localhost:8080/v1/chat/completions`, `mistral-7b`, and `dummy`.
- DeepL uses `DeepL:ApiKey` (legacy `DeepLKey` accepted). A key ending in `:fx` selects the free API host. Only DeepL enables the monthly character quota.
- Discord uses `Discord:Token` (legacy `DiscordToken` accepted). Preflight checks presence/placeholder values, not remote login validity.

The tracked catalogue intentionally supplies **no active profile selections**. Absent selections preserve legacy provider behavior. See [model profiles](models.md) for the factory path.

## REST and retry options

`Discord:Rest` defaults: `MessageTimeout` 10 seconds, `FileUploadTimeout` 60 seconds, `MaxRetries` 2, and `RetryDelay` 500 milliseconds. Use TimeSpan strings for the time values, e.g. `00:00:10`.

`Translation:Retry` and its limits are documented in [translation retries](translation-retries.md). Its configured temperature array **replaces** the default array rather than being appended. Detection and normal translation keep their configured deterministic generation policy.

## Diagnostics

`/tsumari status` and startup logging show backend/model/profile configuration without intentionally printing API keys. Gateway receipt logs are Trace; service skip decisions are generally Debug. Default log levels are provided by local settings, not a checked-in secret-bearing configuration.

For investigation, set categories such as `Tsumari.Bot.DiscordGatewayHostedService` to `Trace` and `Tsumari.Bot.Services.MirroredMessageRoutingService` to `Debug` in private settings. Be mindful that diagnostics and previews can contain message context. Bot output from deployment goes to the terminal only; the scripts do not create bot log files.
