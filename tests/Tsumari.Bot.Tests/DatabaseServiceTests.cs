using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests
{
    public class DatabaseServiceTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;

        public DatabaseServiceTests()
        {
            _testDbPath = $"test_tsumari_{Guid.NewGuid():N}.db";
            
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Database:FilePath"]).Returns(_testDbPath);

            _dbService = new DatabaseService(configMock.Object, NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            // Clean up test database file and journal/wal files if they exist
            try
            {
                if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
                
                var walFile = $"{_testDbPath}-wal";
                if (File.Exists(walFile)) File.Delete(walFile);

                var shmFile = $"{_testDbPath}-shm";
                if (File.Exists(shmFile)) File.Delete(shmFile);
            }
            catch
            {
                // Silently ignore cleanup errors
            }
        }

        [Fact]
        public async Task InitializeDatabaseAsync_CreatesTablesSuccessfully()
        {
            // Act & Assert
            await _dbService.InitializeDatabaseAsync();
            
            // Check that the file was actually created
            Assert.True(File.Exists(_testDbPath));
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
        public async Task MessageLinks_CanLinkAndQueryCorrectly()
        {
            // Arrange
            await _dbService.InitializeDatabaseAsync();
            ulong originalMsgId = 55555;
            ulong mirroredMsgId1 = 66661;
            ulong mirroredMsgId2 = 66662;

            // Act
            await _dbService.LinkMessagesAsync(originalMsgId, mirroredMsgId1, 10001, "el");
            await _dbService.LinkMessagesAsync(originalMsgId, mirroredMsgId2, 10002, "it");

            var links = await _dbService.GetMirroredMessagesAsync(originalMsgId);

            // Assert
            Assert.Equal(2, links.Count);
            Assert.Contains(links, l => l.MirroredMessageId == mirroredMsgId1 && l.ChannelId == 10001 && l.LanguageCode == "EL");
            Assert.Contains(links, l => l.MirroredMessageId == mirroredMsgId2 && l.ChannelId == 10002 && l.LanguageCode == "IT");
        }
    }
}
