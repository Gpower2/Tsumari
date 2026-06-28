using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            _logger = logger;
            
            // Allow setting from config or default to localized database
            var dbPath = configuration["Database:FilePath"] ?? "tsumari.db";
            
            // Build SQLite connection string with optimization flags
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };
            
            _connectionString = builder.ConnectionString;
        }

        /// <summary>
        /// Creates a connection and ensures foreign keys and WAL mode are enabled.
        /// </summary>
        private async Task<SqliteConnection> GetConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Enable WAL mode for high concurrency & performance
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
                await cmd.ExecuteNonQueryAsync();
            }

            return connection;
        }

        /// <summary>
        /// Initializes schemas and applies safety defaults.
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            _logger.LogInformation("Initializing database schemas...");
            
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
                
                await transaction.CommitAsync();
                _logger.LogInformation("Database tables initialized successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogCritical(ex, "Failed to initialize database tables.");
                throw;
            }
        }

        // ==========================================
        // MASTER CHANNELS MANAGEMENT
        // ==========================================

        public async Task<bool> AddMasterChannelAsync(ulong masterChannelId)
        {
            _logger.LogInformation("Registering master channel: {ChannelId}", masterChannelId);
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
            _logger.LogInformation("Registering local channel: {LocalId} for master: {MasterId} in language: {Lang}", 
                localChannelId, parentMasterChannelId, targetLanguageCode);
                
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
            _logger.LogInformation("Attempting to unregister channel {ChannelId}", channelId);
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
                _logger.LogInformation("Unregistered channel {ChannelId}. Rows affected: {Count}", channelId, rowsDeleted);
                return rowsDeleted > 0;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to unregister channel {ChannelId}", channelId);
                throw;
            }
        }

        // ==========================================
        // MESSAGE LINKS LOGGING
        // ==========================================

        public async Task LinkMessagesAsync(ulong originalMessageId, ulong mirroredMessageId, ulong channelId, string languageCode)
        {
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO MessageLinks (OriginalMessageId, MirroredMessageId, ChannelId, LanguageCode) 
                VALUES ($orig, $mirror, $chan, $lang);";
            
            cmd.Parameters.AddWithValue("$orig", originalMessageId.ToString());
            cmd.Parameters.AddWithValue("$mirror", mirroredMessageId.ToString());
            cmd.Parameters.AddWithValue("$chan", channelId.ToString());
            cmd.Parameters.AddWithValue("$lang", LanguageCodeService.NormalizeStoredLanguageCode(languageCode));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<(ulong MirroredMessageId, ulong ChannelId)>> GetMirroredMessagesAsync(ulong originalMessageId)
        {
            var results = new List<(ulong, ulong)>();
            using var connection = await GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MirroredMessageId, ChannelId FROM MessageLinks WHERE OriginalMessageId = $orig;";
            cmd.Parameters.AddWithValue("$orig", originalMessageId.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (ulong.TryParse(reader.GetString(0), out ulong mirId) && 
                    ulong.TryParse(reader.GetString(1), out ulong chanId))
                {
                    results.Add((mirId, chanId));
                }
            }
            return results;
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
            _logger.LogInformation("Incremented monthly translation usage by {Count} characters.", characters);
        }
    }
}
