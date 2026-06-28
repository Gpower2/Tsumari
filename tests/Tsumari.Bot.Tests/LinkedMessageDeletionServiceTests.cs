using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests
{
    public class LinkedMessageDeletionServiceTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;

        public LinkedMessageDeletionServiceTests()
        {
            _testDbPath = $"test_tsumari_delete_{Guid.NewGuid():N}.db";

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Database:FilePath"]).Returns(_testDbPath);

            _dbService = new DatabaseService(configMock.Object, NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }

                var walFile = $"{_testDbPath}-wal";
                if (File.Exists(walFile))
                {
                    File.Delete(walFile);
                }

                var shmFile = $"{_testDbPath}-shm";
                if (File.Exists(shmFile))
                {
                    File.Delete(shmFile);
                }
            }
            catch
            {
            }
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_DoesNothing_ForUnlinkedMessages()
        {
            await _dbService.InitializeDatabaseAsync();
            var discordMessageService = new Mock<IDiscordMessageService>(MockBehavior.Strict);
            var service = CreateService(discordMessageService);

            await service.HandleMessageDeletedAsync(12345UL);

            discordMessageService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_DeletesAllLinkedMirrors_AndCleansRows()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66662UL, 11111UL, "el");

            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(true);
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(11111UL, 66662UL))
                .ReturnsAsync(true);

            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessageDeletedAsync(55555UL);

            discordMessageService.Verify(service => service.DeleteMessageAsync(10001UL, 66661UL), Times.Once);
            discordMessageService.Verify(service => service.DeleteMessageAsync(11111UL, 66662UL), Times.Once);
            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_CleansRows_EvenWhenMirrorDeletionFails()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66662UL, 10002UL, "it");

            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(false);
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10002UL, 66662UL))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessageDeletedAsync(55555UL);

            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_RemovesOnlyTheDeletedMirrorRow_ForMirrorDeletes()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66662UL, 10002UL, "it");

            var discordMessageService = new Mock<IDiscordMessageService>(MockBehavior.Strict);
            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessageDeletedAsync(66661UL);

            var links = await _dbService.GetMirroredMessagesAsync(55555UL);

            Assert.Single(links);
            Assert.Equal(66662UL, links[0].MirroredMessageId);
            discordMessageService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task HandleMessagesDeletedAsync_ProcessesEachDeletedMessage()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");
            await _dbService.LinkMessagesAsync(77777UL, 22222UL, 88881UL, 20001UL, "it");

            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(true);
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(20001UL, 88881UL))
                .ReturnsAsync(true);

            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessagesDeletedAsync(new[] { 55555UL, 77777UL });

            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
            Assert.Empty(await _dbService.GetMirroredMessagesAsync(77777UL));
        }

        [Fact]
        public async Task HandleMessagesDeletedAsync_HandlesOriginalAndMirroredDeleteIds_FromSameFamily()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");

            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(true);

            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessagesDeletedAsync(new[] { 55555UL, 66661UL });

            discordMessageService.Verify(service => service.DeleteMessageAsync(10001UL, 66661UL), Times.Once);
            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_CanBeCalledTwice_ForOriginalDeletes()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");

            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(true);

            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessageDeletedAsync(55555UL);
            await deletionService.HandleMessageDeletedAsync(55555UL);

            discordMessageService.Verify(service => service.DeleteMessageAsync(10001UL, 66661UL), Times.Once);
            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_CanBeCalledTwice_ForMirroredDeletes()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");

            var discordMessageService = new Mock<IDiscordMessageService>(MockBehavior.Strict);
            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessageDeletedAsync(66661UL);
            await deletionService.HandleMessageDeletedAsync(66661UL);

            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
            discordMessageService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_DeletesLegacyOriginalFamilies_WithoutBackfill()
        {
            await CreateLegacyMessageLinksDatabaseAsync(55555UL, 66661UL, 10001UL, "de");
            await _dbService.InitializeDatabaseAsync();

            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(true);

            var deletionService = CreateService(discordMessageService);

            await deletionService.HandleMessageDeletedAsync(55555UL);

            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
        }

        private LinkedMessageDeletionService CreateService(Mock<IDiscordMessageService> discordMessageService)
        {
            return new LinkedMessageDeletionService(
                discordMessageService.Object,
                _dbService,
                NullLogger<LinkedMessageDeletionService>.Instance);
        }

        private async Task CreateLegacyMessageLinksDatabaseAsync(ulong originalMessageId, ulong mirroredMessageId, ulong channelId, string languageCode)
        {
            await using var connection = new SqliteConnection($"Data Source={_testDbPath}");
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
