using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class DatabaseServiceTests : IDisposable
    {
        private readonly TemporarySqliteDatabase _database;
        private readonly DatabaseService _dbService;

        public DatabaseServiceTests()
        {
            _database = new TemporarySqliteDatabase("database-service");
            _dbService = _database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            _database.Dispose();
        }

        [Fact]
        public async Task InitializeDatabaseAsync_CreatesTablesSuccessfully()
        {
            // Act & Assert
            await _dbService.InitializeDatabaseAsync();
            
            // Check that the file was actually created
            Assert.True(File.Exists(_database.DatabasePath));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_UpgradesLegacyMessageLinksSchemaWithoutLosingRows()
        {
            await CreateLegacyMessageLinksDatabaseAsync(12345UL, 67890UL, 54321UL, "el");

            await _dbService.InitializeDatabaseAsync();

            await using var connection = new SqliteConnection($"Data Source={_database.DatabasePath}");
            await connection.OpenAsync();

            await using var tableInfoCmd = connection.CreateCommand();
            tableInfoCmd.CommandText = "PRAGMA table_info(MessageLinks);";
            await using var tableInfoReader = await tableInfoCmd.ExecuteReaderAsync();

            bool hasOriginalChannelId = false;
            while (await tableInfoReader.ReadAsync())
            {
                if (string.Equals(tableInfoReader.GetString(1), "OriginalChannelId", StringComparison.OrdinalIgnoreCase))
                {
                    hasOriginalChannelId = true;
                    break;
                }
            }

            await using var indexCmd = connection.CreateCommand();
            indexCmd.CommandText = "PRAGMA index_list(MessageLinks);";
            await using var indexReader = await indexCmd.ExecuteReaderAsync();

            bool hasMirrorIndex = false;
            while (await indexReader.ReadAsync())
            {
                if (string.Equals(indexReader.GetString(1), "IX_MessageLinks_MirroredMessageId", StringComparison.Ordinal))
                {
                    hasMirrorIndex = true;
                    break;
                }
            }

            var links = await _dbService.GetMirroredMessagesAsync(12345UL);

            Assert.True(hasOriginalChannelId);
            Assert.True(hasMirrorIndex);
            Assert.Single(links);
            Assert.Equal(67890UL, links[0].MirroredMessageId);
            Assert.Equal(54321UL, links[0].ChannelId);
            Assert.Equal("EL", links[0].LanguageCode);
            Assert.Null(links[0].OriginalChannelId);
        }

        [Fact]
        public async Task GetChannelRoutingContextAsync_ReturnsMasterContext_ForRegisteredMasterChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);

            var context = await _dbService.GetChannelRoutingContextAsync(10UL);

            Assert.True(context.IsMaster);
            Assert.False(context.IsLocalized);
            Assert.Equal(10UL, context.LinkedGroupKey);
            Assert.Null(context.ParentMasterChannelId);
            Assert.Null(context.TargetLanguageCode);
        }

        [Fact]
        public async Task GetChannelRoutingContextAsync_ReturnsLocalizedContext_ForRegisteredLocalizedChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");

            var context = await _dbService.GetChannelRoutingContextAsync(20UL);

            Assert.False(context.IsMaster);
            Assert.True(context.IsLocalized);
            Assert.Equal(10UL, context.LinkedGroupKey);
            Assert.Equal(10UL, context.ParentMasterChannelId);
            Assert.Equal("de", context.TargetLanguageCode);
        }

        [Fact]
        public async Task RegisterMasterChannel_And_CheckIsMaster_Works()
        {
            // Arrange
            await _dbService.InitializeDatabaseAsync();
            ulong channelId = 123456789;

            // Act
            bool added = await _dbService.AddMasterChannelAsync(channelId);
            bool isMaster = await _dbService.IsMasterChannelAsync(channelId);
            bool isMasterFake = await _dbService.IsMasterChannelAsync(999999);

            // Assert
            Assert.True(added);
            Assert.True(isMaster);
            Assert.False(isMasterFake);
        }

        [Fact]
        public async Task RegisterLocalChannel_And_CheckCascadeDelete_Works()
        {
            // Arrange
            await _dbService.InitializeDatabaseAsync();
            ulong masterId = 111111111;
            ulong localId = 222222222;
            string langCode = "el";

            // Act: Register master and localized
            await _dbService.AddMasterChannelAsync(masterId);
            bool localRegistered = await _dbService.RegisterLocalChannelAsync(localId, masterId, langCode);

            // Assert registration
            Assert.True(localRegistered);
            Assert.True(await _dbService.IsLocalizedChannelAsync(localId));
            Assert.Equal(masterId, await _dbService.GetParentMasterChannelIdAsync(localId));
            Assert.Equal("el", await _dbService.GetTargetLanguageCodeAsync(localId));

            // Act: Unregister master, verifying cascade delete triggers on local
            bool deleted = await _dbService.UnregisterChannelAsync(masterId);
            
            // Assert cascading delete
            Assert.True(deleted);
            Assert.False(await _dbService.IsMasterChannelAsync(masterId));
            Assert.False(await _dbService.IsLocalizedChannelAsync(localId)); // Cascaded!
        }

        [Fact]
        public async Task RegisterLocalChannel_Normalizes_LocaleCode()
        {
            await _dbService.InitializeDatabaseAsync();
            ulong masterId = 222111333;
            ulong localId = 444555666;

            await _dbService.AddMasterChannelAsync(masterId);
            bool localRegistered = await _dbService.RegisterLocalChannelAsync(localId, masterId, "pt_BR");

            Assert.True(localRegistered);
            Assert.Equal("pt-br", await _dbService.GetTargetLanguageCodeAsync(localId));
        }

        [Fact]
        public async Task SiblingChannelsRetrieval_ReturnsOtherLocalChannelsOnly()
        {
            // Arrange
            await _dbService.InitializeDatabaseAsync();
            ulong masterId = 12345;
            ulong local1 = 10001;
            ulong local2 = 10002;
            ulong local3 = 10003;

            await _dbService.AddMasterChannelAsync(masterId);
            await _dbService.RegisterLocalChannelAsync(local1, masterId, "en");
            await _dbService.RegisterLocalChannelAsync(local2, masterId, "el");
            await _dbService.RegisterLocalChannelAsync(local3, masterId, "it");

            // Act
            var siblings = await _dbService.GetSiblingChannelsAsync(local1);

            // Assert (siblings of local1 should be local2 and local3)
            Assert.Equal(2, siblings.Count);
            Assert.Contains(siblings, s => s.ChannelId == local2 && s.TargetLanguageCode == "el");
            Assert.Contains(siblings, s => s.ChannelId == local3 && s.TargetLanguageCode == "it");
            Assert.DoesNotContain(siblings, s => s.ChannelId == local1);
        }

        [Fact]
        public async Task UsageTracker_TracksAndIncrementsUsageCorrectly()
        {
            // Arrange
            await _dbService.InitializeDatabaseAsync();

            // Act
            int initialUsage = await _dbService.GetCurrentMonthUsageAsync();
            await _dbService.IncrementUsageAsync(100);
            await _dbService.IncrementUsageAsync(250);
            int finalUsage = await _dbService.GetCurrentMonthUsageAsync();

            // Assert
            Assert.Equal(0, initialUsage);
            Assert.Equal(350, finalUsage);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_LogsDatabaseStatusSummary()
        {
            var logger = new Tsumari.Bot.Tests.ListLogger<DatabaseService>();
            var dbService = _database.CreateDatabaseService(logger);

            await dbService.InitializeDatabaseAsync();
            await dbService.AddMasterChannelAsync(111UL);
            await dbService.AddMasterChannelAsync(222UL);
            await dbService.RegisterLocalChannelAsync(333UL, 111UL, "de");
            await dbService.RegisterLocalChannelAsync(444UL, 111UL, "el");
            await dbService.LinkMessagesAsync(9000UL, 111UL, 9001UL, 111UL, "master");
            await dbService.LinkMessagesAsync(9000UL, 111UL, 9002UL, 333UL, "de");
            await dbService.LinkMessagesAsync(9003UL, 222UL, 9004UL, 444UL, "el");
            await dbService.IncrementUsageAsync(100);
            await dbService.IncrementUsageAsync(250);

            logger.Entries.Clear();

            await dbService.InitializeDatabaseAsync();

            var fileStatusLog = logger.Entries.Single(entry => entry.EventId.Id == 1009);
            var contentStatusLog = logger.Entries.Single(entry => entry.EventId.Id == 1010);

            Assert.Contains(Path.GetFullPath(_database.DatabasePath), fileStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("WAL size:", fileStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("Last activity (UTC):", fileStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("2 master channels", contentStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("2 localized channels", contentStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("4 configured channels", contentStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("2 linked message families", contentStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("3 linked bot messages", contentStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("2 localized message links", contentStatusLog.Message, StringComparison.Ordinal);
            Assert.Contains("350 quota-tracked characters this month", contentStatusLog.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GetDatabaseStatusSnapshotAsync_ReturnsExpectedCounts()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(111UL);
            await _dbService.AddMasterChannelAsync(222UL);
            await _dbService.RegisterLocalChannelAsync(333UL, 111UL, "de");
            await _dbService.RegisterLocalChannelAsync(444UL, 111UL, "el");
            await _dbService.LinkMessagesAsync(9000UL, 111UL, 9001UL, 111UL, "master");
            await _dbService.LinkMessagesAsync(9000UL, 111UL, 9002UL, 333UL, "de");
            await _dbService.LinkMessagesAsync(9003UL, 222UL, 9004UL, 444UL, "el");
            await _dbService.IncrementUsageAsync(100);
            await _dbService.IncrementUsageAsync(250);

            var status = await _dbService.GetDatabaseStatusSnapshotAsync();

            Assert.Equal(Path.GetFullPath(_database.DatabasePath), status.DatabaseFilePath);
            Assert.True(status.DatabaseFileSizeBytes > 0);
            Assert.True(status.DatabaseLastActivityUtc.HasValue);
            Assert.True(status.DatabaseStorageSizeBytes >= status.DatabaseFileSizeBytes);
            Assert.Equal(2, status.MasterChannelCount);
            Assert.Equal(2, status.LocalizedChannelCount);
            Assert.Equal(4, status.ConfiguredChannelCount);
            Assert.Equal(2, status.LinkedMessageFamilyCount);
            Assert.Equal(3, status.LinkedBotMessageCount);
            Assert.Equal(2, status.LocalizedMessageLinkCount);
            Assert.Equal(350, status.CurrentMonthCharacterCount);
        }

        [Fact]
        public async Task IsOriginalMessageTrackedAsync_ReturnsTrueForLinkedOriginalAndFalseOtherwise()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(10001UL, 111UL, 20001UL, 222UL, "de");

            var tracked = await _dbService.IsOriginalMessageTrackedAsync(10001UL);
            var notTracked = await _dbService.IsOriginalMessageTrackedAsync(10002UL);

            Assert.True(tracked);
            Assert.False(notTracked);
        }

        [Fact]
        public async Task LinkMessagesAsync_StoresNullTimestamp_WhenTimestampNotProvided()
        {
            await _dbService.InitializeDatabaseAsync();

            await _dbService.LinkMessagesAsync(10001UL, 111UL, 20001UL, 111UL, "master");

            var lastTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(111UL);
            Assert.Null(lastTimestamp);
        }

        [Fact]
        public async Task LinkMessagesAsync_StoresOriginalMessageTimestamp()
        {
            await _dbService.InitializeDatabaseAsync();
            var timestamp = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

            await _dbService.LinkMessagesAsync(10001UL, 111UL, 20001UL, 111UL, "master", timestamp);

            var lastTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(111UL);
            Assert.Equal(timestamp, lastTimestamp);
        }

        [Fact]
        public async Task GetLastTrackedMessageTimestampAsync_ReturnsMaxTimestamp()
        {
            await _dbService.InitializeDatabaseAsync();

            await _dbService.LinkMessagesAsync(10001UL, 111UL, 20001UL, 111UL, "master", new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero));
            await _dbService.LinkMessagesAsync(10002UL, 111UL, 20002UL, 111UL, "master", new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero));
            await _dbService.LinkMessagesAsync(10003UL, 111UL, 20003UL, 111UL, "master", new DateTimeOffset(2026, 7, 19, 11, 0, 0, TimeSpan.Zero));

            var lastTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(111UL);
            Assert.Equal(new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero), lastTimestamp);
        }

        [Fact]
        public async Task GetLastTrackedMessageTimestampAsync_ReturnsNullWhenNoMessages()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(111UL);

            var lastTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(111UL);

            Assert.Null(lastTimestamp);
        }

        [Fact]
        public async Task GetAllMasterChannelIdsAsync_ReturnsAllMasterChannels()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(111UL);
            await _dbService.AddMasterChannelAsync(222UL);

            var masterChannelIds = await _dbService.GetAllMasterChannelIdsAsync();

            Assert.Equal(2, masterChannelIds.Count);
            Assert.Contains(111UL, masterChannelIds);
            Assert.Contains(222UL, masterChannelIds);
        }

        [Fact]
        public async Task GetLastTrackedOriginalMessageIdAsync_ReturnsNewestOriginalMessageId()
        {
            await _dbService.InitializeDatabaseAsync();

            await _dbService.LinkMessagesAsync(10001UL, 111UL, 20001UL, 111UL, "master");
            await _dbService.LinkMessagesAsync(10003UL, 111UL, 20003UL, 111UL, "master");
            await _dbService.LinkMessagesAsync(10002UL, 111UL, 20002UL, 111UL, "master");

            var lastMessageId = await _dbService.GetLastTrackedOriginalMessageIdAsync(111UL);

            Assert.Equal(10003UL, lastMessageId);
        }

        [Fact]
        public async Task GetLastTrackedOriginalMessageIdAsync_OrdersLargeUnsignedSnowflakesCorrectly()
        {
            await _dbService.InitializeDatabaseAsync();

            // IDs around and above 2^63-1 to verify that CAST to INTEGER would overflow.
            var smallerLargeId = 9223372036854775806UL; // 2^63-2
            var largerLargeId = 18446744073709551614UL; // 2^64-2

            await _dbService.LinkMessagesAsync(smallerLargeId, 111UL, 20001UL, 111UL, "master");
            await _dbService.LinkMessagesAsync(largerLargeId, 111UL, 20002UL, 111UL, "master");

            var lastMessageId = await _dbService.GetLastTrackedOriginalMessageIdAsync(111UL);

            Assert.Equal(largerLargeId, lastMessageId);
        }

        [Fact]
        public async Task GetTrackedOriginalMessageIdsAsync_ReturnsTrackedIdsInBatch()
        {
            await _dbService.InitializeDatabaseAsync();

            await _dbService.LinkMessagesAsync(1UL, 111UL, 10UL, 111UL, "master");
            await _dbService.LinkMessagesAsync(2UL, 111UL, 20UL, 111UL, "master");
            await _dbService.LinkMessagesAsync(3UL, 111UL, 30UL, 222UL, "de");

            var trackedIds = await _dbService.GetTrackedOriginalMessageIdsAsync(new[] { 1UL, 2UL, 4UL });

            Assert.Equal(2, trackedIds.Count);
            Assert.Contains(1UL, trackedIds);
            Assert.Contains(2UL, trackedIds);
            Assert.DoesNotContain(4UL, trackedIds);
            Assert.DoesNotContain(3UL, trackedIds);
        }

        [Fact]
        public async Task SetOriginalMessageTimestampAsync_UpdatesTimestampForAllLinksOfOriginalMessage()
        {
            await _dbService.InitializeDatabaseAsync();
            var timestamp = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

            await _dbService.LinkMessagesAsync(10001UL, 111UL, 20001UL, 111UL, "master");
            await _dbService.LinkMessagesAsync(10001UL, 111UL, 20002UL, 222UL, "de");

            await _dbService.SetOriginalMessageTimestampAsync(10001UL, timestamp);

            var lastTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(111UL);
            Assert.Equal(timestamp, lastTimestamp);
        }

        [Fact]
        public async Task MessageLinks_CanLinkAndQueryCorrectly()
        {
            // Arrange
            await _dbService.InitializeDatabaseAsync();
            ulong originalMsgId = 55555;
            ulong originalChannelId = 44444;
            ulong mirroredMsgId1 = 66661;
            ulong mirroredMsgId2 = 66662;

            // Act
            await _dbService.LinkMessagesAsync(originalMsgId, originalChannelId, mirroredMsgId1, 10001, "el");
            await _dbService.LinkMessagesAsync(originalMsgId, originalChannelId, mirroredMsgId2, 10002, "it");

            var links = await _dbService.GetMirroredMessagesAsync(originalMsgId);

            // Assert
            Assert.Equal(2, links.Count);
            Assert.Contains(links, l => l.MirroredMessageId == mirroredMsgId1 && l.ChannelId == 10001 && l.LanguageCode == "EL" && l.OriginalChannelId == originalChannelId);
            Assert.Contains(links, l => l.MirroredMessageId == mirroredMsgId2 && l.ChannelId == 10002 && l.LanguageCode == "IT" && l.OriginalChannelId == originalChannelId);
        }

        [Fact]
        public async Task EnsureOriginalChannelIdAsync_DoesNotOverwriteExistingValue()
        {
            await _dbService.InitializeDatabaseAsync();

            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "el");
            await _dbService.EnsureOriginalChannelIdAsync(55555UL, 22222UL);

            var links = await _dbService.GetMirroredMessagesAsync(55555UL);

            Assert.Single(links);
            Assert.Equal(11111UL, links[0].OriginalChannelId);
        }

        [Fact]
        public async Task DeleteMessageLinksAsync_RemovesAllLinksForOriginalMessage_WithoutTouchingOthers()
        {
            await _dbService.InitializeDatabaseAsync();

            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "el");
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66662UL, 10002UL, "it");
            await _dbService.LinkMessagesAsync(77777UL, 22222UL, 88881UL, 20001UL, "de");

            await _dbService.DeleteMessageLinksAsync(55555UL);

            var deletedFamily = await _dbService.GetMirroredMessagesAsync(55555UL);
            var remainingFamily = await _dbService.GetMirroredMessagesAsync(77777UL);

            Assert.Empty(deletedFamily);
            Assert.Single(remainingFamily);
            Assert.Equal(88881UL, remainingFamily[0].MirroredMessageId);
        }

        [Fact]
        public async Task DeleteMessageLinkByMirroredMessageIdAsync_RemovesOnlyTheTargetLink()
        {
            await _dbService.InitializeDatabaseAsync();

            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "el");
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66662UL, 10002UL, "it");

            await _dbService.DeleteMessageLinkByMirroredMessageIdAsync(66661UL);

            var links = await _dbService.GetMirroredMessagesAsync(55555UL);

            Assert.Single(links);
            Assert.Equal(66662UL, links[0].MirroredMessageId);
        }

        [Fact]
        public async Task GetLinkedMessageFamilyAsync_ReturnsNull_ForUnlinkedMessage()
        {
            await _dbService.InitializeDatabaseAsync();

            var family = await _dbService.GetLinkedMessageFamilyAsync(999999UL, 12345UL);

            Assert.Null(family);
        }

        [Fact]
        public async Task LinkedMessageFamily_CanResolveFromOriginalMessage()
        {
            await _dbService.InitializeDatabaseAsync();
            ulong originalMsgId = 77777;
            ulong originalChannelId = 12345;
            ulong mirroredMsgId = 88888;

            await _dbService.LinkMessagesAsync(originalMsgId, originalChannelId, mirroredMsgId, 54321, "de");

            var family = await _dbService.GetLinkedMessageFamilyAsync(originalMsgId, originalChannelId);

            Assert.NotNull(family);
            Assert.Equal(originalMsgId, family!.OriginalMessageId);
            Assert.Equal(originalChannelId, family.OriginalChannelId);
            Assert.Single(family.MirroredMessages);
            Assert.Equal(mirroredMsgId, family.MirroredMessages[0].MirroredMessageId);
        }

        [Fact]
        public async Task LinkedMessageFamily_ReturnsNull_ForLegacyMirroredMessageWithoutBackfill()
        {
            await CreateLegacyMessageLinksDatabaseAsync(99991UL, 99992UL, 22222UL, "it");
            await _dbService.InitializeDatabaseAsync();

            var family = await _dbService.GetLinkedMessageFamilyAsync(99992UL);

            Assert.Null(family);
        }

        [Fact]
        public async Task LinkedMessageFamily_BackfillsLegacyOriginalLookup_AndThenResolvesFromMirroredMessage()
        {
            await CreateLegacyMessageLinksDatabaseAsync(99991UL, 99992UL, 22222UL, "it");
            await _dbService.InitializeDatabaseAsync();

            var familyFromOriginal = await _dbService.GetLinkedMessageFamilyAsync(99991UL, 11111UL);

            Assert.NotNull(familyFromOriginal);
            Assert.Equal(11111UL, familyFromOriginal!.OriginalChannelId);

            var links = await _dbService.GetMirroredMessagesAsync(99991UL);
            Assert.Single(links);
            Assert.Equal(11111UL, links[0].OriginalChannelId);

            var familyFromMirrored = await _dbService.GetLinkedMessageFamilyAsync(99992UL);

            Assert.NotNull(familyFromMirrored);
            Assert.Equal(99991UL, familyFromMirrored!.OriginalMessageId);
            Assert.Equal(11111UL, familyFromMirrored.OriginalChannelId);
        }

        [Fact]
        public async Task LinkedMessageFamily_CanResolveFromMirroredMessage()
        {
            await _dbService.InitializeDatabaseAsync();
            ulong originalMsgId = 99991;
            ulong originalChannelId = 11111;
            ulong mirroredMsgId = 99992;

            await _dbService.LinkMessagesAsync(originalMsgId, originalChannelId, mirroredMsgId, 22222, "it");

            var family = await _dbService.GetLinkedMessageFamilyAsync(mirroredMsgId);

            Assert.NotNull(family);
            Assert.Equal(originalMsgId, family!.OriginalMessageId);
            Assert.Equal(originalChannelId, family.OriginalChannelId);
            Assert.Single(family.MirroredMessages);
            Assert.Equal(mirroredMsgId, family.MirroredMessages[0].MirroredMessageId);
        }

        private async Task CreateLegacyMessageLinksDatabaseAsync(ulong originalMessageId, ulong mirroredMessageId, ulong channelId, string languageCode)
        {
            await using var connection = new SqliteConnection($"Data Source={_database.DatabasePath}");
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE MessageLinks (
                    OriginalMessageId TEXT NOT NULL,
                    MirroredMessageId TEXT NOT NULL,
                    ChannelId TEXT NOT NULL,
                    LanguageCode TEXT NOT NULL,
                    PRIMARY KEY (OriginalMessageId, ChannelId)
                );

                INSERT INTO MessageLinks (OriginalMessageId, MirroredMessageId, ChannelId, LanguageCode)
                VALUES ($orig, $mirror, $chan, $lang);";
            cmd.Parameters.AddWithValue("$orig", originalMessageId.ToString());
            cmd.Parameters.AddWithValue("$mirror", mirroredMessageId.ToString());
            cmd.Parameters.AddWithValue("$chan", channelId.ToString());
            cmd.Parameters.AddWithValue("$lang", languageCode);

            await cmd.ExecuteNonQueryAsync();
        }

    }
}
