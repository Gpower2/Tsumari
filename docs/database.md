# SQLite Schema and Message-Link Usage

Tsumari uses raw `Microsoft.Data.Sqlite` calls instead of an ORM. The database stores channel topology, mirrored message links, and optional DeepL usage tracking.

## Schema

```sql
CREATE TABLE IF NOT EXISTS MasterChannels (
    MasterChannelId TEXT PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS LocalizedChannels (
    ChannelId TEXT PRIMARY KEY,
    ParentMasterChannelId TEXT NOT NULL,
    TargetLanguageCode TEXT NOT NULL,
    FOREIGN KEY (ParentMasterChannelId) REFERENCES MasterChannels(MasterChannelId) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS MessageLinks (
    OriginalMessageId TEXT NOT NULL,
    OriginalChannelId TEXT NULL,
    MirroredMessageId TEXT NOT NULL,
    ChannelId TEXT NOT NULL,
    LanguageCode TEXT NOT NULL,
    PRIMARY KEY (OriginalMessageId, ChannelId)
);

CREATE INDEX IF NOT EXISTS IX_MessageLinks_MirroredMessageId
    ON MessageLinks (MirroredMessageId);

CREATE TABLE IF NOT EXISTS UsageTracker (
    YearMonth TEXT PRIMARY KEY,
    CharacterCount INTEGER DEFAULT 0
);
```

## Table Roles

### `MasterChannels`

Stores master channel IDs. These are the parent hubs for each translation cluster.

### `LocalizedChannels`

Stores localized channel IDs, their parent master channel, and the target language code used for routing and translation.

Notes:

- `TargetLanguageCode` is normalized to lowercase when saved.
- `RegisterLocalChannelAsync` uses `INSERT OR REPLACE`, so re-registering a localized channel updates its current mapping.
- The foreign key uses `ON DELETE CASCADE`, so removing a master automatically removes its localized children.

### `MessageLinks`

Stores one generated/mirrored bot message per `(OriginalMessageId, ChannelId)`, plus the source channel ID for that original message.

Current uses in code:

- generating jump-link buttons after a message fan-out completes
- resolving corresponding parent messages when reply mirroring needs a channel-local reply target
- looking up mirrored bot messages when a source message is edited later
- tracking the mismatch-flow translated reply created in the source localized channel
- deleting linked bot messages when the original source message is deleted
- resolving a full linked-message family from either an original message ID or a mirrored message ID during reaction mirroring

### `UsageTracker`

Stores a monthly character counter keyed by `YYYY-MM`.

Current behavior:

- used only for DeepL quota enforcement
- ignored for `Ollama` and `OpenAI` providers
- incremented after successful language analysis and successful translation when DeepL is active

## Why IDs Are Stored as `TEXT`

Discord Snowflake IDs are 64-bit values. Storing them as `TEXT` avoids cross-layer precision issues and keeps the SQL simple.

## Operational Flow in the Code

### Startup

`InitializeDatabaseAsync()` creates all four tables if they do not exist.

### Channel Registration

- `AddMasterChannelAsync()` inserts master channels
- `RegisterLocalChannelAsync()` links localized channels to a master
- `UnregisterChannelAsync()` removes either a master or localized channel configuration

### Message Linking

- `LinkMessagesAsync()` stores generated bot messages during routing
- `EnsureOriginalChannelIdAsync()` backfills the source channel for pre-existing link rows when the original message is observed again
- `DeleteMessageLinksAsync()` removes an original message's linked bot-message rows during delete sync
- `DeleteMessageLinkByMirroredMessageIdAsync()` prunes stale rows when a mirrored bot message is deleted independently
- `GetMirroredMessagesAsync()` returns all generated bot messages tied to an original user message
- `GetLinkedMessageFamilyAsync()` resolves the original message plus its linked bot-generated copies for reply and reaction mirroring

## Concurrency and Safety Settings

Every connection enables:

```sql
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;
```

### `journal_mode = WAL`

Write-ahead logging improves concurrent read/write behavior, which matters because message routing, translation, and admin commands are all asynchronous.

### `foreign_keys = ON`

SQLite does not enforce foreign keys by default. Tsumari enables them explicitly so cascade deletes work reliably.
