# Tsumari — Discord Cross-Language Translation & Mirroring Bot

Tsumari is a high-performance, production-ready Discord translation and media mirroring bot. Built using a **.NET 10 Worker Service** and the **Discord.Net (v3.19+)** library, Tsumari is architected specifically for low-memory execution boundaries (such as a 1GB RAM always-free Linux container on HidenCloud). 

It dynamically tracks and routes messages within independent language "clusters" utilizing a parent-child relational SQLite schema and the DeepL translation engine.

---

## 🚀 Key Features

*   **Multi-Master Clusters:** Map a single "Master" channel (e.g., `#general`) to multiple independent "Localized" channels (e.g., `#general-english`, `#general-greek`, `#general-italian`) with full bi-directional, cross-language synchronization.
*   **Automatic Language Detection:** Seamlessly identifies the source language of incoming messages via the DeepL translation engine.
*   **Intelligent Routing Engine:** Handles complex routing scenarios (such as localized matching and mismatching inputs) to ensure native and non-native readers see appropriate text.
*   **Expiring CDN Re-upload Layer:** Automatically downloads and re-uploads expiring Discord attachments as native files to target rooms simultaneously, completely bypassing the 24-hour CDN link limit.
*   **DeepL Credit Protection:** Strictly enforces a rolling calendar month limit of **500,000 characters**, safe-locking translation activities instantly to prevent billing overages.
*   **Zero-Dependency Resiliency:** Employs a custom, thread-safe exponential backoff retry policy and a **Circuit Breaker** (Closed, Open, Half-Open states) to block transient HTTP failures without extra memory footprint.
*   **Low-RAM Optimized SQLite WAL Mode:** Uses raw parameter-mapped ADO.NET SQLite queries (avoiding EF Core overheads) and connection pools set to Write-Ahead Logging (WAL) for maximum speed.

---

## 📁 Repository Structure

```
E:\Development\Tsumari\
├── Tsumari.slnx            # .NET 10 solution file
├── README.md               # Main documentation
├── docs/                   # Extended guides & diagrams
│   ├── database.md         # Database schema & WAL mode guide
│   ├── routing.md          # Multi-master routing engine logic
│   ├── resiliency.md       # Circuit breaker & retry implementation
│   └── media.md            # Expiring CDN re-upload layer details
├── src/
│   └── Tsumari.Bot/        # Bot application project (.NET 10 Worker)
│       ├── Program.cs      # Host bootstrap & DI setup
│       ├── Worker.cs       # Discord Gateway handler & routing execution
│       ├── appsettings.json
│       ├── Services/
│       │   ├── DatabaseService.cs
│       │   ├── TranslationService.cs
│       │   └── ResiliencyHelper.cs
│       └── Modules/
│           └── InteractionModule.cs  # Administrative Slash commands
└── tests/
    └── Tsumari.Bot.Tests/  # Automated xUnit test suite
```

---

## ⚙️ Configuration (`appsettings.json`)

Populate your credentials in the `src/Tsumari.Bot/appsettings.json` slot:

```json
{
  "Discord": {
    "Token": "YOUR_DISCORD_BOT_TOKEN"
  },
  "DeepL": {
    "ApiKey": "YOUR_DEEPL_API_KEY"
  },
  "Database": {
    "FilePath": "tsumari.db"
  }
}
```

> [!IMPORTANT]
> **DeepL API Free Tier Auto-Routing:**
> If your DeepL API key ends with `:fx` (the Free tier suffix), Tsumari's connection engine will explicitly override default endpoints and route traffic to the free portal: `https://api-free.deepl.com`.

---

## 🛠️ Administrative Slash Commands

All configuration operations are built using the Discord.Net Interaction Framework and restricted to users with **Administrator** permissions:

*   `/tsumari add-master [channel]`: Registers a text channel as an independent **Master Channel** in the `MasterChannels` table.
*   `/tsumari register-local [localChannel] [masterChannel] [languageCode]`: Links a target text channel to a specific parent Master Channel, setting its target language (e.g., `el` for Greek, `it` for Italian) in `LocalizedChannels`.
*   `/tsumari unregister [channel]`: Deletes the channel configuration from the database. Deleting a Master channel automatically purges all connected localized channels via SQL cascading deletes (`ON DELETE CASCADE`).

---

## 🏗️ Building and Deploying

### Running Tests Locally
Verify all 12 unit and integration tests compile and execute cleanly:
```bash
dotnet test
```

### Publishing Standalone Linux Binary
To compile a fully self-contained release package for your HidenCloud server (does not require the .NET SDK to be pre-installed on the host environment), run:
```bash
dotnet publish src/Tsumari.Bot/Tsumari.Bot.csproj -c Release -r linux-x64 --self-contained
```

The output standalone executables and target assets will be located in:
`src/Tsumari.Bot/bin/Release/net10.0/linux-x64/publish/`

---

## 📚 Extended Documentation

For in-depth guides on Tsumari's architecture, review the files in the `docs` directory:
1.  [SQLite Database Guide](docs/database.md): Tables structure, WAL configurations, and usage trackers.
2.  [Routing Engine Logic](docs/routing.md): Master-to-local and local-to-master (match and mismatch) flow diagrams.
3.  [Resiliency & Circuit Breakers](docs/resiliency.md): Retry state machine and exponential backoff configuration.
4.  [Media Mirroring Layer](docs/media.md): CDN downloads and stream memory footprint optimizations.
