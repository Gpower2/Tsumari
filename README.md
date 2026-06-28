# Tsumari — Discord Cross-Language Translation & Mirroring Bot

Tsumari is a .NET 10 Discord bot built on **Discord.Net**. It routes messages across master/localized channel clusters, translates them with a selectable backend, mirrors attachments, and cross-links generated messages with Discord jump buttons.

## Key Features

- **Cluster-based routing:** one master channel can fan out to multiple localized channels, and localized channels can route back into the cluster.
- **Bi-directional flows:** supports master-to-localized routing plus localized match/mismatch routing.
- **Pluggable translation backends:** `DeepL`, `Ollama`, and `OpenAI` (OpenAI-compatible chat-completions endpoint).
- **Automatic language detection:** every text message is detected before routing decisions are made.
- **Separate locale targets:** locale tags such as `pt` and `pt-br` are preserved as distinct translation targets and are not collapsed together during fan-out.
- **Clear translated headers:** translated messages use the format `**Author** (XX to YY):`.
- **Jump-link buttons:** generated bot messages are edited after send so they can include `Original` plus language-code buttons for other generated copies.
- **Edited-message synchronization:** when a user edits a recently cached text message, mirrored bot messages are updated in place.
- **Attachment mirroring:** attachments are downloaded once and re-uploaded as native Discord files during initial fan-out.
- **SQLite persistence:** channel mappings, mirrored message IDs, and usage tracking are stored in SQLite.
- **DeepL quota protection:** the monthly `500,000` character guard is enforced only when `Translation.Provider` is `DeepL`.
- **Built-in resiliency:** translation/detection calls are wrapped in a custom retry + circuit-breaker helper.

## Current Behavior Notes

- **Edit sync is text-only.** The `MessageUpdated` flow compares message content and only rewrites mirrored message text; attachment-only edits are not re-mirrored.
- **Edit sync depends on the socket cache.** The Discord client is configured with `MessageCacheSize = 50`, so edit detection is most reliable for recently cached messages.
- **Language buttons only exist for bot-generated copies.** The source user-authored message is always reached through the `Original` button.
- **Mismatch replies are tracked too.** When a localized channel receives the wrong language, the bot's in-channel translated reply is stored in `MessageLinks` and participates in cross-link buttons.
- **Stored locale tags are normalized.** Inputs such as `pt_BR` are normalized to `pt-br` for storage and display, while target-channel routing keeps locale variants separate.

## Repository Structure

```text
E:\Development\Tsumari\
├── Tsumari.slnx
├── README.md
├── docs/
│   ├── database.md
│   ├── examples.md
│   ├── media.md
│   ├── resiliency.md
│   └── routing.md
├── src/
│   └── Tsumari.Bot/
│       ├── Program.cs
│       ├── Worker.cs
│       ├── appsettings.json
│       ├── Modules/
│       │   └── InteractionModule.cs
│       └── Services/
│           ├── DatabaseService.cs
│           ├── DeepLTranslationProvider.cs
│           ├── DeepLLanguageService.cs
│           ├── HttpClientNames.cs
│           ├── ITranslationProvider.cs
│           ├── LanguageCodeService.cs
│           ├── OllamaTranslationProvider.cs
│           ├── OpenAITranslationProvider.cs
│           ├── ResiliencyHelper.cs
│           ├── TranslationProvider.cs
│           ├── TranslationProviderResolver.cs
│           └── TranslationService.cs
└── tests/
    └── Tsumari.Bot.Tests/
        ├── DatabaseServiceTests.cs
        ├── DeepLTranslationProviderTests.cs
        ├── DeepLLanguageServiceTests.cs
        ├── LanguageCodeServiceTests.cs
        ├── OllamaTranslationProviderTests.cs
        ├── OpenAITranslationProviderTests.cs
        ├── ResiliencyHelperTests.cs
        ├── TranslationServiceTests.cs
        ├── TranslationProviderResolverTests.cs
        └── WorkerEditTests.cs
```

## Configuration (`src/Tsumari.Bot/appsettings.json`)

```json
{
  "Discord": {
    "Token": ""
  },
  "Translation": {
    "Provider": "Ollama",
    "Ollama": {
      "ApiUrl": "http://localhost:11434/api/generate",
      "Model": "translategemma:12b"
    },
    "OpenAI": {
      "ApiUrl": "http://localhost:8080/v1/chat/completions",
      "ApiKey": "",
      "Model": "mistral-7b"
    }
  },
  "DeepL": {
    "ApiKey": ""
  },
  "Database": {
    "FilePath": "tsumari.db"
  }
}
```

### Translation Provider Notes

- `Translation.Provider` accepts `DeepL`, `Ollama`, or `OpenAI`.
- The checked-in default configuration currently uses **Ollama** with `translategemma:12b`.
- `DeepL.ApiKey` is only required when `Translation.Provider` is `DeepL`.
- If a DeepL key ends with `:fx`, Tsumari routes requests to `https://api-free.deepl.com`.
- If `Translation.Provider` is missing or invalid, startup falls back to **Ollama** instead of defaulting to paid DeepL, and that fallback is logged explicitly.
- DeepL target language handling is provider-specific: `DeepLLanguageService` queries DeepL's `GET /v3/languages?resource=translate_text` metadata and only falls back to legacy aliases when the provider metadata cannot be used.
- Translation providers are separated behind `ITranslationProvider`, and all `IHttpClientFactory` usage now goes through named clients.
- The `UsageTracker` quota guard is only enforced for DeepL; local/self-hosted LLM providers do not use the monthly character limit.

## Administrative Slash Commands

All configuration commands live under the `/tsumari` group and require **Administrator** permissions:

- `/tsumari add-master [channel]`
- `/tsumari register-local [local-channel] [master-channel] [language-code]`
- `/tsumari unregister [channel]`

`register-local` stores language codes in normalized lowercase form (`pt_BR` becomes `pt-br`), and re-registering an existing localized channel updates its mapping because the database operation uses `INSERT OR REPLACE`.

## Build, Test, and Publish

Run the full local verification suite:

```powershell
dotnet build
dotnet test
```

Publish a self-contained Linux build:

```powershell
dotnet publish src\Tsumari.Bot\Tsumari.Bot.csproj -c Release -r linux-x64 --self-contained
```

Publish output is written under:

`src\Tsumari.Bot\bin\Release\net10.0\linux-x64\publish\`

## Extended Documentation

- [Routing flows and edit synchronization](docs/routing.md)
- [SQLite schema and message-link usage](docs/database.md)
- [Attachment mirroring behavior](docs/media.md)
- [Resiliency helper details](docs/resiliency.md)
- [Discord message examples](docs/examples.md)
