# Tsumari — Discord Cross-Language Translation & Mirroring Bot

Tsumari is a .NET 10 Discord bot built on **Discord.Net**. It routes messages across master/localized channel clusters, translates them with a selectable backend, mirrors attachments, and cross-links generated messages with Discord jump buttons.

## Key Features

- **Cluster-based routing:** one master channel can fan out to multiple localized channels, and localized channels can route back into the cluster.
- **Bi-directional flows:** supports master-to-localized routing plus localized match/mismatch routing.
- **Pluggable translation backends:** `DeepL`, `Ollama`, and `OpenAI` (OpenAI-compatible chat-completions endpoint).
- **Best-effort language analysis:** every text message is analyzed before routing decisions are made, including mixed-language/code-switched messages when the selected provider can infer them.
- **Conservative mixed-language labels:** tiny secondary-language traces are collapsed back to the dominant language so isolated loanwords, slang, or names do not usually produce `XX,YY => ZZ` headers on their own.
- **Separate locale targets:** locale tags such as `pt` and `pt-br` are preserved as distinct translation targets and are not collapsed together during fan-out.
- **Clear translated headers:** both single-language and mixed-language translations use `=>`, with mixed-language sources surfacing the detected source list as `XX,YY => ZZ`.
- **Jump-link buttons:** generated bot messages are edited after send so they can include `Original` plus language-code buttons for other generated copies.
- **Reply mirroring:** when a user replies to a tracked message, mirrored bot messages reply to the corresponding linked message in each destination channel.
- **Edited-message synchronization:** when a user edits a text message, mirrored bot messages are updated in place.
- **Delete synchronization:** when a source message is deleted, existing linked bot messages are deleted too.
- **Reaction mirroring:** standard reactions added to one linked message are reconciled across the rest of the linked message family.
- **Attachment mirroring:** attachments are downloaded once and re-uploaded as native Discord files during initial fan-out.
- **Gateway-safe dispatching:** Discord gateway callbacks enqueue work immediately, then a dispatcher routes events into per-linked-group FIFO workers so local-LLM latency does not block the gateway task.
- **SQLite persistence:** channel mappings, mirrored message IDs, and usage tracking are stored in SQLite.
- **DeepL quota protection:** the monthly `500,000` character guard is enforced only when `Translation.Provider` is `DeepL`.
- **Built-in resiliency:** translation/language-analysis calls are wrapped in a custom retry + circuit-breaker helper.

## Current Behavior Notes

- The canonical end-to-end routing, edit-sync, reply, reaction, and delete behavior lives in `docs/routing.md`. The notes below are the short operational summary.
- **Edit sync is text-only.** The `MessageUpdated` flow compares message content and only rewrites mirrored message text; attachment-only edits are not re-mirrored.
- **Edit sync uses cache when available.** The Discord client keeps `MessageCacheSize = 50` messages cached so unchanged edits can be skipped cheaply, but cache misses are still re-synchronized instead of being ignored.
- **Oversized attachment warnings survive text edits.** If a mirrored message had to warn about files above the guild upload limit, later text edits keep that warning while the oversized attachment still exists on the source message.
- **Reaction mirroring currently tracks standard reactions only.** Burst reactions are ignored because the bot can only mirror normal reactions reliably.
- **Gateway work is ordered per linked group.** Events for the same linked channel cluster are processed sequentially, while unrelated clusters can continue in parallel.
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
│       ├── Constants/
│       │   └── HttpClientNames.cs
│       ├── DiscordGatewayHostedService.cs
│       ├── Extensions/
│       │   └── HttpResponseExtensions.cs
│       ├── GlobalUsings.cs
│       ├── Logging/
│       │   ├── DatabaseServiceLog.cs
│       │   ├── DeepLLanguageServiceLog.cs
│       │   ├── DeepLTranslationProviderLog.cs
│       │   ├── DiscordGatewayEventDispatcherServiceLog.cs
│       │   ├── DiscordGatewayHostedServiceLog.cs
│       │   ├── DiscordMessagePublisherServiceLog.cs
│       │   ├── EditedMessageSyncServiceLog.cs
│       │   ├── GatewayEventGroupResolverLog.cs
│       │   ├── HttpResponseLog.cs
│       │   ├── InteractionModuleLog.cs
│       │   ├── LinkedMessageDeletionServiceLog.cs
│       │   ├── MirroredMessageRoutingServiceLog.cs
│       │   ├── ReactionMirroringServiceLog.cs
│       │   ├── ResiliencyHelperLog.cs
│       │   ├── TranslationProviderResolverLog.cs
│       │   └── TranslationServiceLog.cs
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Models/
│       │   ├── ChannelRoutingContext.cs
│       │   ├── DiscordReactionEvent.cs
│       │   ├── GatewayIngressEvent.cs
│       │   ├── JumpLinkTarget.cs
│       │   ├── LanguageAnalysisResult.cs
│       │   ├── LinkedMessageFamily.cs
│       │   ├── MediaAsset.cs
│       │   ├── ReplyMirroringContext.cs
│       │   ├── TranslationProvider.cs
│       │   └── TranslationProviderConfigurationReport.cs
│       ├── Modules/
│       │   └── InteractionModule.cs
│       ├── Properties/
│       │   └── AssemblyInfo.cs
│       ├── TranslationProviders/
│       │   ├── Abstractions/
│       │   │   ├── ITranslationProvider.cs
│       │   │   └── LlmTranslationProviderBase.cs
│       │   ├── DeepLLanguageService.cs
│       │   ├── DeepLTranslationProvider.cs
│       │   ├── OllamaTranslationProvider.cs
│       │   ├── OpenAITranslationProvider.cs
│       │   └── TranslationProviderResolver.cs
│       └── Services/
│           ├── Abstractions/
│           │   ├── IDiscordGatewayEventDispatcher.cs
│           │   ├── IDiscordGatewayEventProcessor.cs
│           │   ├── IDiscordMessageService.cs
│           │   └── IGatewayEventGroupResolver.cs
│           ├── DiscordMessagePublisherService.cs
│           ├── DiscordMessageService.cs
│           ├── DiscordGatewayEventDispatcherService.cs
│           ├── DiscordGatewayEventProcessorService.cs
│           ├── AttachmentMirroringPlanner.cs
│           ├── DatabaseService.cs
│           ├── EditedMessageSyncService.cs
│           ├── GatewayEventGroupResolver.cs
│           ├── LanguageCodeService.cs
│           ├── LinkedMessageDeletionService.cs
│           ├── MirroredMessageFormatter.cs
│           ├── MirroredMessageNoticeLocalizer.cs
│           ├── MirroredMessageRoutingService.cs
│           ├── ReplyMirroringService.cs
│           ├── ReactionMirroringService.cs
│           ├── ResiliencyHelper.cs
│           └── TranslationService.cs
└── tests/
    └── Tsumari.Bot.Tests/
        ├── Component/
        │   ├── DatabaseServiceTests.cs
        │   ├── DiscordGatewayHostedServiceComponentTests.cs
        │   ├── DiscordGatewayHostedServiceDeleteTests.cs
        │   ├── GatewayEventGroupResolverTests.cs
        │   ├── LinkedMessageDeletionServiceTests.cs
        │   ├── ReactionMirroringServiceTests.cs
        │   ├── ReplyMirroringServiceTests.cs
        │   └── TranslationServiceTests.cs
        ├── GlobalUsings.cs
        ├── ListLogger.cs
        ├── TemporarySqliteDatabase.cs
        └── Unit/
            ├── DeepLTranslationProviderTests.cs
            ├── DeepLLanguageServiceTests.cs
            ├── DiscordGatewayEventDispatcherServiceTests.cs
            ├── DiscordGatewayHostedServiceLifecycleTests.cs
            ├── DiscordGatewayHostedServiceLogTests.cs
            ├── DiscordMessagePublisherServiceTests.cs
            ├── EditedMessageSyncServiceTests.cs
            ├── HttpResponseExtensionsTests.cs
            ├── LanguageCodeServiceTests.cs
            ├── MirroredMessageFormatterTests.cs
            ├── MirroredMessageNoticeLocalizerTests.cs
            ├── MirroredMessageRoutingServiceTests.cs
            ├── OllamaTranslationProviderTests.cs
            ├── OpenAITranslationProviderTests.cs
            ├── ResiliencyHelperTests.cs
            └── TranslationProviderResolverTests.cs
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
      "Model": "translategemma:12b",
      "KeepAlive": "15m"
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
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

### Translation Provider Notes

- `Translation.Provider` accepts `DeepL`, `Ollama`, or `OpenAI`.
- The checked-in default configuration currently uses **Ollama** with `translategemma:12b`.
- `Translation.Ollama.KeepAlive` controls how long Ollama keeps the selected model resident between requests. The checked-in `15m` value reduces cold-start latency after short idle periods at the cost of holding model RAM longer.
- `DeepL.ApiKey` is only required when `Translation.Provider` is `DeepL`.
- If a DeepL key ends with `:fx`, Tsumari routes requests to `https://api-free.deepl.com`.
- If `Translation.Provider` is missing or invalid, startup falls back to **Ollama** instead of defaulting to paid DeepL, and that fallback is logged explicitly.
- During bootstrap, Tsumari logs the selected provider's active state and provider-specific configuration details (for example model + endpoint for LLM providers, or free/paid routing for DeepL).
- DeepL target language handling is provider-specific: `DeepLLanguageService` queries DeepL's `GET /v3/languages?resource=translate_text` metadata and only falls back to legacy aliases when the provider metadata cannot be used.
- Translation providers are separated behind `ITranslationProvider`, and all `IHttpClientFactory` usage now goes through named clients.

### Gateway Diagnostics Logging

Gateway receipt logs are emitted at **Trace** level, and intentional service no-op / skip decisions are emitted at **Debug** level. The checked-in config stays at `Information`, so raise category levels only when you need to troubleshoot event flow:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Tsumari.Bot": "Debug",
    "Tsumari.Bot.DiscordGatewayHostedService": "Trace"
  }
}
```

Useful categories when narrowing production diagnostics:

- `Tsumari.Bot.DiscordGatewayHostedService` - trace-level receipt logs for Discord events
- `Tsumari.Bot.Services.MirroredMessageRoutingService` - message routing skip decisions
- `Tsumari.Bot.Services.EditedMessageSyncService` - edit-sync skip decisions
- `Tsumari.Bot.Services.LinkedMessageDeletionService` - delete-sync skip decisions
- `Tsumari.Bot.Services.ReactionMirroringService` - reaction-mirroring skip decisions
- The `UsageTracker` quota guard is only enforced for DeepL; local/self-hosted LLM providers do not use the monthly character limit.

## Administrative Slash Commands

All `/tsumari` commands require **Administrator** permissions in guilds. The two language-probe commands can also be used in DMs for ad-hoc testing, while the configuration commands and status command reject DM use at runtime. Discord applies availability/permissions at the grouped-command level, so the guild Administrator check is enforced at runtime instead of via a hidden Discord-side default permission:

- `/tsumari add-master [channel]`
- `/tsumari register-local [local-channel] [master-channel] [language-code]`
- `/tsumari unregister [channel]`
- `/tsumari status`
- `/tsumari detect-language [text]`
- `/tsumari translate [target-language] [text]`

`register-local` stores language codes in normalized lowercase form (`pt_BR` becomes `pt-br`), and re-registering an existing localized channel updates its mapping because the database operation uses `INSERT OR REPLACE`.

`status` responds **ephemerally** with the current bot/database counts plus the selected translation provider's active/configuration details (such as provider name, model/endpoint, or DeepL free/paid routing). The two language-probe commands also respond **ephemerally** so they do not clutter the channel. `detect-language` runs the provider-backed analysis directly, and `translate` intentionally uses the same analysis + trusted source-hint flow as live message routing when analysis succeeds, while still attempting a translation without a hint if analysis itself fails. Both commands count against the same provider usage/quota rules as normal translation work.

## Build, Test, and Publish

Run the full local verification suite:

```powershell
dotnet build src\Tsumari.Bot\Tsumari.Bot.csproj
dotnet test tests\Tsumari.Bot.Tests\Tsumari.Bot.Tests.csproj --nologo
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
