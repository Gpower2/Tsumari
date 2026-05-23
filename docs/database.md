# SQLite Database Design & Optimizations

To fulfill the low-memory (1GB boundary) footprint constraint of free hosting containers, Tsumari avoids heavy ORMs (like Entity Framework Core) and communicates with SQLite directly through raw, parameter-mapped SQL queries using `Microsoft.Data.Sqlite`.

---

## 📊 Relational Database Schema

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
    MirroredMessageId TEXT NOT NULL,
    ChannelId TEXT NOT NULL,
    LanguageCode TEXT NOT NULL,
    PRIMARY KEY (OriginalMessageId, ChannelId)
);

CREATE TABLE IF NOT EXISTS UsageTracker (
    YearMonth TEXT PRIMARY KEY,
    CharacterCount INTEGER DEFAULT 0
);
```

### Table Details:
1.  **`MasterChannels`**: Stores registered parent translation channels. Since Discord Snowflake IDs fit within a 64-bit space, they are stored securely as string (`TEXT`) rows to avoid any overflow or serialization precision loss across layers.
2.  **`LocalizedChannels`**: Stores language-specific child channels linked to their Master. Crucially, the foreign key constraint enforces an `ON DELETE CASCADE` rule. When a Master channel is unregistered, SQLite automatically cleans up all associated Localized channel links.
3.  **`MessageLinks`**: Maps original user posts to all generated mirrored versions across cluster sibling channels. This enables future updates or message deletion synchronization.
4.  **`UsageTracker`**: Enforces DeepL character limit protection. The `YearMonth` key is formatted as `YYYY-MM`. When a translation succeeds, the processed character weight is added here.

---

## ⚡ Concurrency & Performance Enhancements

To ensure thread-safety and smooth non-blocking execution inside a multi-threaded asynchronous environment, Tsumari implements two SQLite PRAGMA tuning settings on every database connection lifecycle:

### 1. Write-Ahead Logging (WAL) Mode
```sql
PRAGMA journal_mode = WAL;
```
By default, SQLite locks the entire database file during a write operation, causing other threads attempting reads to block or throw "Database is locked" exceptions. 
*   In WAL mode, SQLite writes changes to a separate `-wal` file instead of directly updating the main database file.
*   This enables **high concurrency**: multiple readers can read concurrently while a separate thread is writing, dramatically improving speed.

### 2. Cascading Delete Enforcement
```sql
PRAGMA foreign_keys = ON;
```
SQLite does not enforce foreign key relationships or cascading triggers by default. Tsumari explicitly sets this flag on every opened connection to guarantee that deleting a record from `MasterChannels` instantly purges corresponding elements in `LocalizedChannels`.
