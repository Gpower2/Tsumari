using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class ReplyMirroringServiceTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;
        private readonly ReplyMirroringService _replyMirroringService;

        public ReplyMirroringServiceTests()
        {
            _testDbPath = $"test_tsumari_reply_{Guid.NewGuid():N}.db";

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Database:FilePath"]).Returns(_testDbPath);

            _dbService = new DatabaseService(configMock.Object, NullLogger<DatabaseService>.Instance);
            _replyMirroringService = new ReplyMirroringService(_dbService);
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
        public async Task ResolveReplyContextAsync_ReturnsNull_WhenMessageIsNotAReply()
        {
            await _dbService.InitializeDatabaseAsync();

            var result = await _replyMirroringService.ResolveReplyContextAsync(11111UL, null);

            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveReplyContextAsync_ReturnsNull_WhenReferencedMessageIsUnlinked()
        {
            await _dbService.InitializeDatabaseAsync();
            var messageReference = CreateMessageReference(55555UL, 11111UL);

            var result = await _replyMirroringService.ResolveReplyContextAsync(11111UL, messageReference);

            Assert.Null(result);
        }

        [Fact]
        public void ResolveReplyTargetMessageId_ReturnsOriginalMessage_ForOriginalReplyInOriginalChannel()
        {
            var family = CreateFamily(
                originalMessageId: 100UL,
                originalChannelId: 10UL,
                new MirroredMessageLink { MirroredMessageId = 110UL, ChannelId = 10UL, LanguageCode = "EL" },
                new MirroredMessageLink { MirroredMessageId = 200UL, ChannelId = 20UL, LanguageCode = "DE" });

            var result = family.ResolveReplyTargetMessageId(100UL, 10UL);

            Assert.Equal(100UL, result);
        }

        [Fact]
        public void ResolveReplyTargetMessageId_ReturnsSameChannelMirror_ForMirroredReplyInOriginalChannel()
        {
            var family = CreateFamily(
                originalMessageId: 100UL,
                originalChannelId: 10UL,
                new MirroredMessageLink { MirroredMessageId = 110UL, ChannelId = 10UL, LanguageCode = "EL" },
                new MirroredMessageLink { MirroredMessageId = 200UL, ChannelId = 20UL, LanguageCode = "DE" });

            var result = family.ResolveReplyTargetMessageId(200UL, 10UL);

            Assert.Equal(110UL, result);
        }

        [Fact]
        public void ResolveReplyTargetMessageId_FallsBackToOriginal_WhenSameChannelMirrorDoesNotExist()
        {
            var family = CreateFamily(
                originalMessageId: 100UL,
                originalChannelId: 10UL,
                new MirroredMessageLink { MirroredMessageId = 200UL, ChannelId = 20UL, LanguageCode = "DE" });

            var result = family.ResolveReplyTargetMessageId(200UL, 10UL);

            Assert.Equal(100UL, result);
        }

        [Fact]
        public void ResolveReplyTargetMessageId_ReturnsMirroredMessage_ForDestinationChannel()
        {
            var family = CreateFamily(
                originalMessageId: 100UL,
                originalChannelId: 10UL,
                new MirroredMessageLink { MirroredMessageId = 200UL, ChannelId = 20UL, LanguageCode = "DE" },
                new MirroredMessageLink { MirroredMessageId = 300UL, ChannelId = 30UL, LanguageCode = "IT" });

            var result = family.ResolveReplyTargetMessageId(100UL, 30UL);

            Assert.Equal(300UL, result);
        }

        [Fact]
        public void ResolveReplyTargetMessageId_ReturnsNull_WhenDestinationHasNoCorrespondingMessage()
        {
            var family = CreateFamily(
                originalMessageId: 100UL,
                originalChannelId: 10UL,
                new MirroredMessageLink { MirroredMessageId = 200UL, ChannelId = 20UL, LanguageCode = "DE" });

            var result = family.ResolveReplyTargetMessageId(100UL, 30UL);

            Assert.Null(result);
        }

        [Fact]
        public void ResolveReplyTargetMessageId_ReturnsNull_WhenRepliedMessageIsOutsideFamily()
        {
            var family = CreateFamily(
                originalMessageId: 100UL,
                originalChannelId: 10UL,
                new MirroredMessageLink { MirroredMessageId = 200UL, ChannelId = 20UL, LanguageCode = "DE" });

            var result = family.ResolveReplyTargetMessageId(999UL, 20UL);

            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveReplyContextAsync_CreatesReplyReference_ForTranslatedDestination()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(100UL, 10UL, 200UL, 20UL, "de");
            await _dbService.LinkMessagesAsync(100UL, 10UL, 300UL, 30UL, "it");

            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(10UL, CreateMessageReference(100UL, 10UL));
            var replyReference = ReplyMirroringService.CreateReplyReference(replyContext, 30UL);

            Assert.NotNull(replyContext);
            Assert.NotNull(replyReference);
            Assert.True(replyReference!.MessageId.IsSpecified);
            Assert.Equal(300UL, replyReference.MessageId.Value);
            Assert.Equal(30UL, replyReference.ChannelId);
            Assert.True(replyReference.FailIfNotExists.IsSpecified);
            Assert.False(replyReference.FailIfNotExists.Value);
        }

        [Fact]
        public async Task ResolveReplyContextAsync_UsesSameChannelMirror_ForMirroredRepliesInOriginalChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(100UL, 10UL, 110UL, 10UL, "el");
            await _dbService.LinkMessagesAsync(100UL, 10UL, 200UL, 20UL, "de");

            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(20UL, CreateMessageReference(200UL, 20UL));
            var replyReference = ReplyMirroringService.CreateReplyReference(replyContext, 10UL);

            Assert.NotNull(replyContext);
            Assert.NotNull(replyReference);
            Assert.Equal(110UL, replyReference!.MessageId.Value);
        }

        [Fact]
        public async Task ResolveReplyContextAsync_BackfillsLegacyOriginalReplies_AndCreatesDestinationReplyReference()
        {
            await CreateLegacyMessageLinksDatabaseAsync(100UL, 200UL, 20UL, "de");
            await _dbService.InitializeDatabaseAsync();

            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(10UL, CreateMessageReference(100UL, 10UL));
            var replyReference = ReplyMirroringService.CreateReplyReference(replyContext, 20UL);

            Assert.NotNull(replyContext);
            Assert.NotNull(replyReference);
            Assert.Equal(200UL, replyReference!.MessageId.Value);

            var links = await _dbService.GetMirroredMessagesAsync(100UL);
            Assert.Single(links);
            Assert.Equal(10UL, links[0].OriginalChannelId);
        }

        [Fact]
        public async Task ResolveReplyContextAsync_ReturnsNull_ForLegacyMirroredRepliesWithoutBackfill()
        {
            await CreateLegacyMessageLinksDatabaseAsync(100UL, 200UL, 20UL, "de");
            await _dbService.InitializeDatabaseAsync();

            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(20UL, CreateMessageReference(200UL, 20UL));

            Assert.Null(replyContext);
        }

        [Fact]
        public void CreateReplyReference_ReturnsNull_WhenDestinationHasNoCorrespondingParent()
        {
            var replyContext = new ReplyMirroringContext
            {
                RepliedMessageId = 100UL,
                ParentMessageFamily = CreateFamily(
                    originalMessageId: 100UL,
                    originalChannelId: 10UL,
                    new MirroredMessageLink { MirroredMessageId = 200UL, ChannelId = 20UL, LanguageCode = "DE" })
            };

            var replyReference = ReplyMirroringService.CreateReplyReference(replyContext, 30UL);

            Assert.Null(replyReference);
        }

        private static LinkedMessageFamily CreateFamily(ulong originalMessageId, ulong originalChannelId, params MirroredMessageLink[] mirroredMessages)
        {
            return new LinkedMessageFamily
            {
                OriginalMessageId = originalMessageId,
                OriginalChannelId = originalChannelId,
                MirroredMessages = [.. mirroredMessages]
            };
        }

        private static MessageReference CreateMessageReference(ulong messageId, ulong channelId)
        {
            return new MessageReference(messageId, channelId, null, false, default);
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
