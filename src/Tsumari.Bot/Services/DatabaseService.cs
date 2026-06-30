using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _databaseFilePath;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            _logger = logger;
            
            // Allow setting from config or default to localized database
            var dbPath = configuration["Database:FilePath"] ?? "tsumari.db";
            _databaseFilePath = Path.GetFullPath(dbPath);
            
            // Build SQLite connection string with optimization flags
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _databaseFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                ForeignKeys = true
            };
            
            _connectionString = builder.ConnectionString;
        }

        private async Task<SqliteConnection> GetConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        /// <summary>
        /// Initializes schemas and applies safety defaults.
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            _logger.LogInitializingDatabaseSchemas();
            
            const string createMasterChannelsSql = @"
                CREATE TABLE IF NOT EXISTS MasterChannels (
                    MasterChannelId TEXT PRIMARY KEY
                );";

            const string createLocalizedChannelsSql = @"
                CREATE TABLE IF NOT EXISTS LocalizedChannels (
                    ChannelId TEXT PRIMARY KEY,
                    ParentMasterChannelId TEXT NOT NULL,
                    TargetLanguageCode TEXT NOT NULL,
                    FOREIGN KEY (ParentMasterChannelId) REFERENCES MasterChannels(MasterChannelId) ON DELETE CASCADE
                );";

            const string createMessageLinksSql = @"
                CREATE TABLE IF NOT EXISTS MessageLinks (
                    OriginalMessageId TEXT NOT NULL,
                    OriginalChannelId TEXT NULL,
                    MirroredMessageId TEXT NOT NULL,
                    ChannelId TEXT NOT NULL,
                    LanguageCode TEXT NOT NULL,
                    PRIMARY KEY (OriginalMessageId, ChannelId)
                );";

            const string createUsageTrackerSql = @"
                CREATE TABLE IF NOT EXISTS UsageTracker (
                    YearMonth TEXT PRIMARY KEY,
                    CharacterCount INTEGER DEFAULT 0
                );";

            using var connection = await GetConnectionAsync();
            await EnsureWriteAheadLoggingAsync(connection);
            using var transaction = connection.BeginTransaction();
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    
                    cmd.CommandText = createMasterChannelsSql;
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = createLocalizedChannelsSql;
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = createMessageLinksSql;
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = createUsageTrackerSql;
                    await cmd.ExecuteNonQueryAsync();
                }

                if (!await ColumnExistsAsync(connection, transaction, "MessageLinks", "OriginalChannelId"))
                {
                    using var alterMessageLinksCmd = connection.CreateCommand();
                    alterMessageLinksCmd.Transaction = transaction;
                    alterMessageLinksCmd.CommandText = "ALTER TABLE MessageLinks ADD COLUMN OriginalChannelId TEXT;";
                    await alterMessageLinksCmd.ExecuteNonQueryAsync();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_MessageLinks_MirroredMessageId ON MessageLinks (MirroredMessageId);";
                    await cmd.ExecuteNonQueryAsync();
                }
                 
                await transaction.CommitAsync();
                _logger.LogDatabaseTablesInitialized();

                try
                {
                    await LogDatabaseStatusAsync(connection);
                }
                catch (Exception ex)
                {
                    _logger.LogDatabaseStatusCaptureFailed(ex);
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogDatabaseTablesInitializationFailed(ex);
                throw;
            }
        }

        private static async Task EnsureWriteAheadLoggingAsync(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = WAL;";
            await cmd.ExecuteScalarAsync();
        }

        private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, string columnName)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"PRAGMA table_info({tableName});";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task LogDatabaseStatusAsync(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    (SELECT COUNT(*) FROM MasterChannels) AS MasterChannelCount,
                    (SELECT COUNT(*) FROM LocalizedChannels) AS LocalizedChannelCount,
                    (SELECT COUNT(DISTINCT OriginalMessageId) FROM MessageLinks) AS LinkedMessageFamilyCount,
                    (SELECT COUNT(*) FROM MessageLinks) AS MirroredMessageCount,
                    (SELECT COUNT(*) FROM MessageLinks WHERE UPPER(LanguageCode) != 'MASTER') AS LocalizedMessageLinkCount,
                    COALESCE((SELECT CharacterCount FROM UsageTracker WHERE YearMonth = $ym LIMIT 1), 0) AS CurrentMonthCharacterCount;";
            cmd.Parameters.AddWithValue("$ym", GetCurrentYearMonth());

            using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();

            var masterChannelCount = reader.GetInt64(0);
            var localizedChannelCount = reader.GetInt64(1);
            var linkedMessageFamilyCount = reader.GetInt64(2);
            var mirroredMessageCount = reader.GetInt64(3);
            var localizedMessageLinkCount = reader.GetInt64(4);
            var currentMonthCharacterCount = reader.GetInt64(5);
            var configuredChannelCount = masterChannelCount + localizedChannelCount;

            // We only log metrics that are stored explicitly. The schema does not currently
            // distinguish true translations from same-language localized pass-through mirrors.
            var databaseFileInfo = new FileInfo(_databaseFilePath);
            var databaseFileSizeBytes = databaseFileInfo.Exists ? databaseFileInfo.Length : 0;
            var databaseFileLastWriteUtc = databaseFileInfo.Exists
                ? databaseFileInfo.LastWriteTimeUtc.ToString("O")
                : "unknown";

            _logger.LogDatabaseFileStatus(_databaseFilePath, databaseFileSizeBytes, databaseFileLastWriteUtc);
            _logger.LogDatabaseContentStatus(
                masterChannelCount,
                localizedChannelCount,
                configuredChannelCount,
                linkedMessageFamilyCount,
                mirroredMessageCount,
                localizedMessageLinkCount,
                currentMonthCharacterCount);
        }

        // ==========================================
        // MASTER CHANNELS MANAGEMENT
        // ==========================================

        public async Task<bool> AddMasterChannelAsync(ulong masterChannelId)
        {
            _logger.LogRegisteringMasterChannel(masterChannelId);
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO MasterChannels (MasterChannelId) VALUES ($id);";
            cmd.Parameters.AddWithValue("$id", masterChannelId.ToString());
            
            int rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> IsMasterChannelAsync(ulong channelId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM MasterChannels WHERE MasterChannelId = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", channelId.ToString());
            
            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }

        // ==========================================
        // LOCALIZED CHANNELS MANAGEMENT
        // ==========================================

        public async Task<bool> RegisterLocalChannelAsync(ulong localChannelId, ulong parentMasterChannelId, string targetLanguageCode)
        {
            _logger.LogRegisteringLocalChannel(localChannelId, parentMasterChannelId, targetLanguageCode);
                 
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            
            // We use INSERT OR REPLACE so that if it was linked elsewhere, we update it dynamically
            cmd.CommandText = @"
                INSERT OR REPLACE INTO LocalizedChannels (ChannelId, ParentMasterChannelId, TargetLanguageCode) 
                VALUES ($localId, $parentMasterId, $langCode);";
            
            cmd.Parameters.AddWithValue("$localId", localChannelId.ToString());
            cmd.Parameters.AddWithValue("$parentMasterId", parentMasterChannelId.ToString());
            cmd.Parameters.AddWithValue("$langCode", LanguageCodeService.NormalizeStoredLanguageCode(targetLanguageCode));

            int rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> IsLocalizedChannelAsync(ulong channelId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM LocalizedChannels WHERE ChannelId = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", channelId.ToString());
            
            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }

        public async Task<ulong?> GetParentMasterChannelIdAsync(ulong localizedChannelId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ParentMasterChannelId FROM LocalizedChannels WHERE ChannelId = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", localizedChannelId.ToString());
            
            var result = await cmd.ExecuteScalarAsync() as string;
            return result != null && ulong.TryParse(result, out ulong id) ? id : null;
        }

        public async Task<ChannelRoutingContext> GetChannelRoutingContextAsync(ulong channelId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    EXISTS(SELECT 1 FROM MasterChannels WHERE MasterChannelId = $id) AS IsMaster,
                    (SELECT ParentMasterChannelId FROM LocalizedChannels WHERE ChannelId = $id LIMIT 1) AS ParentMasterChannelId,
                    (SELECT TargetLanguageCode FROM LocalizedChannels WHERE ChannelId = $id LIMIT 1) AS TargetLanguageCode;";
            cmd.Parameters.AddWithValue("$id", channelId.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return new ChannelRoutingContext { ChannelId = channelId };
            }

            ulong? parentMasterChannelId = null;
            if (!reader.IsDBNull(1) && ulong.TryParse(reader.GetString(1), out var parsedParentMasterChannelId))
            {
                parentMasterChannelId = parsedParentMasterChannelId;
            }

            return new ChannelRoutingContext
            {
                ChannelId = channelId,
                IsMaster = reader.GetInt64(0) != 0,
                ParentMasterChannelId = parentMasterChannelId,
                TargetLanguageCode = reader.IsDBNull(2) ? null : reader.GetString(2)
            };
        }

        public async Task<ulong?> GetLinkedGroupKeyForChannelAsync(ulong channelId)
        {
            var routingContext = await GetChannelRoutingContextAsync(channelId);
            return routingContext.LinkedGroupKey;
        }

        public async Task<string?> GetTargetLanguageCodeAsync(ulong localizedChannelId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT TargetLanguageCode FROM LocalizedChannels WHERE ChannelId = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", localizedChannelId.ToString());
            
            return await cmd.ExecuteScalarAsync() as string;
        }

        public async Task<List<(ulong ChannelId, string TargetLanguageCode)>> GetLocalizedChannelsForMasterAsync(ulong masterChannelId)
        {
            var results = new List<(ulong, string)>();
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ChannelId, TargetLanguageCode FROM LocalizedChannels WHERE ParentMasterChannelId = $masterId;";
            cmd.Parameters.AddWithValue("$masterId", masterChannelId.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var chIdStr = reader.GetString(0);
                var lang = reader.GetString(1);
                if (ulong.TryParse(chIdStr, out ulong chId))
                {
                    results.Add((chId, lang));
                }
            }
            return results;
        }

        public async Task<List<(ulong ChannelId, string TargetLanguageCode)>> GetSiblingChannelsAsync(ulong localizedChannelId)
        {
            var results = new List<(ulong, string)>();
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            
            // Sibling channels are other channels that share the same parent master channel
            cmd.CommandText = @"
                SELECT ChannelId, TargetLanguageCode 
                FROM LocalizedChannels 
                WHERE ParentMasterChannelId = (
                    SELECT ParentMasterChannelId FROM LocalizedChannels WHERE ChannelId = $id
                ) AND ChannelId != $id;";
                
            cmd.Parameters.AddWithValue("$id", localizedChannelId.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var chIdStr = reader.GetString(0);
                var lang = reader.GetString(1);
                if (ulong.TryParse(chIdStr, out ulong chId))
                {
                    results.Add((chId, lang));
                }
            }
            return results;
        }

        // ==========================================
        // DYNAMIC REMOVAL (UNREGISTER)
        // ==========================================

        public async Task<bool> UnregisterChannelAsync(ulong channelId)
        {
            _logger.LogAttemptingToUnregisterChannel(channelId);
            var idStr = channelId.ToString();
            using var connection = await GetConnectionAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                int rowsDeleted = 0;
                
                // 1. Delete from MasterChannels (cascading deletes will handle child localized channels)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM MasterChannels WHERE MasterChannelId = $id;";
                    cmd.Parameters.AddWithValue("$id", idStr);
                    rowsDeleted += await cmd.ExecuteNonQueryAsync();
                }

                // 2. Delete from LocalizedChannels directly (if it wasn't a Master Channel)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM LocalizedChannels WHERE ChannelId = $id;";
                    cmd.Parameters.AddWithValue("$id", idStr);
                    rowsDeleted += await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                _logger.LogChannelUnregistered(channelId, rowsDeleted);
                return rowsDeleted > 0;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogChannelUnregisterFailed(ex, channelId);
                throw;
            }
        }

        // ==========================================
        // MESSAGE LINKS LOGGING
        // ==========================================

        public async Task LinkMessagesAsync(ulong originalMessageId, ulong originalChannelId, ulong mirroredMessageId, ulong channelId, string languageCode)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO MessageLinks (OriginalMessageId, OriginalChannelId, MirroredMessageId, ChannelId, LanguageCode) 
                VALUES ($orig, $origChan, $mirror, $chan, $lang);";
             
            cmd.Parameters.AddWithValue("$orig", originalMessageId.ToString());
            cmd.Parameters.AddWithValue("$origChan", originalChannelId.ToString());
            cmd.Parameters.AddWithValue("$mirror", mirroredMessageId.ToString());
            cmd.Parameters.AddWithValue("$chan", channelId.ToString());
            cmd.Parameters.AddWithValue("$lang", LanguageCodeService.NormalizeStoredLanguageCode(languageCode));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EnsureOriginalChannelIdAsync(ulong originalMessageId, ulong originalChannelId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE MessageLinks
                SET OriginalChannelId = $origChan
                WHERE OriginalMessageId = $orig
                  AND (OriginalChannelId IS NULL OR OriginalChannelId = '');";
            cmd.Parameters.AddWithValue("$orig", originalMessageId.ToString());
            cmd.Parameters.AddWithValue("$origChan", originalChannelId.ToString());

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteMessageLinksAsync(ulong originalMessageId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM MessageLinks WHERE OriginalMessageId = $orig;";
            cmd.Parameters.AddWithValue("$orig", originalMessageId.ToString());

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteMessageLinkByMirroredMessageIdAsync(ulong mirroredMessageId)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM MessageLinks WHERE MirroredMessageId = $mirror;";
            cmd.Parameters.AddWithValue("$mirror", mirroredMessageId.ToString());

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<MirroredMessageLink>> GetMirroredMessagesAsync(ulong originalMessageId)
        {
            var results = new List<MirroredMessageLink>();
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MirroredMessageId, ChannelId, LanguageCode, OriginalChannelId FROM MessageLinks WHERE OriginalMessageId = $orig;";
            cmd.Parameters.AddWithValue("$orig", originalMessageId.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (ulong.TryParse(reader.GetString(0), out ulong mirId) && 
                    ulong.TryParse(reader.GetString(1), out ulong chanId))
                {
                    ulong? originalChannelId = null;
                    if (!reader.IsDBNull(3) && ulong.TryParse(reader.GetString(3), out ulong originalChanId))
                    {
                        originalChannelId = originalChanId;
                    }

                    results.Add(new MirroredMessageLink
                    {
                        MirroredMessageId = mirId,
                        ChannelId = chanId,
                        LanguageCode = LanguageCodeService.NormalizeLanguageCode(reader.GetString(2)),
                        OriginalChannelId = originalChannelId
                    });
                }
            }
            return results;
        }

        public async Task<LinkedMessageFamily?> GetLinkedMessageFamilyAsync(ulong messageId, ulong? knownChannelId = null)
        {
            ulong originalMessageId;
            bool messageIsOriginal;

            using (var connection = await GetConnectionAsync())
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT 1 FROM MessageLinks WHERE OriginalMessageId = $id LIMIT 1;";
                cmd.Parameters.AddWithValue("$id", messageId.ToString());
                messageIsOriginal = await cmd.ExecuteScalarAsync() != null;
            }

            if (messageIsOriginal)
            {
                originalMessageId = messageId;

                if (knownChannelId.HasValue)
                {
                    await EnsureOriginalChannelIdAsync(originalMessageId, knownChannelId.Value);
                }
            }
            else
            {
                using var connection = await GetConnectionAsync();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT OriginalMessageId FROM MessageLinks WHERE MirroredMessageId = $id LIMIT 1;";
                cmd.Parameters.AddWithValue("$id", messageId.ToString());

                var result = await cmd.ExecuteScalarAsync() as string;
                if (result == null || !ulong.TryParse(result, out originalMessageId))
                {
                    return null;
                }
            }

            var mirroredMessages = await GetMirroredMessagesAsync(originalMessageId);
            if (mirroredMessages.Count == 0)
            {
                return null;
            }

            var originalChannelId = mirroredMessages
                .Where(link => link.OriginalChannelId.HasValue)
                .Select(link => link.OriginalChannelId!.Value)
                .FirstOrDefault();

            if (originalChannelId == 0 && messageIsOriginal && knownChannelId.HasValue)
            {
                originalChannelId = knownChannelId.Value;
            }

            if (originalChannelId == 0)
            {
                return null;
            }

            return new LinkedMessageFamily
            {
                OriginalMessageId = originalMessageId,
                OriginalChannelId = originalChannelId,
                MirroredMessages = mirroredMessages
            };
        }

        public async Task<ulong?> GetLinkedGroupKeyForMessageAsync(ulong messageId, ulong? knownChannelId = null)
        {
            var family = await GetLinkedMessageFamilyAsync(messageId, knownChannelId);
            if (family == null)
            {
                return null;
            }

            var groupKey = await GetLinkedGroupKeyForChannelAsync(family.OriginalChannelId);
            return groupKey ?? family.OriginalChannelId;
        }

        // ==========================================
        // CHARACTER USAGE TRACKER
        // ==========================================

        private static string GetCurrentYearMonth()
        {
            return DateTime.UtcNow.ToString("yyyy-MM");
        }

        public async Task<int> GetCurrentMonthUsageAsync()
        {
            var currentMonth = GetCurrentYearMonth();
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT CharacterCount FROM UsageTracker WHERE YearMonth = $ym LIMIT 1;";
            cmd.Parameters.AddWithValue("$ym", currentMonth);

            var result = await cmd.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task IncrementUsageAsync(int characters)
        {
            if (characters <= 0) return;

            var currentMonth = GetCurrentYearMonth();
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            
            // Insert month record if missing, otherwise increment
            cmd.CommandText = @"
                INSERT INTO UsageTracker (YearMonth, CharacterCount) 
                VALUES ($ym, $chars) 
                ON CONFLICT(YearMonth) DO UPDATE SET CharacterCount = CharacterCount + $chars;";
            
            cmd.Parameters.AddWithValue("$ym", currentMonth);
            cmd.Parameters.AddWithValue("$chars", characters);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogTranslationUsageIncremented(characters);
        }
    }
}
