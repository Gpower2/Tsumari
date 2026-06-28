using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Tsumari.Bot.Tests;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class ReactionMirroringServiceTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;

        public ReactionMirroringServiceTests()
        {
            _testDbPath = $"test_tsumari_reactions_{Guid.NewGuid():N}.db";

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
        public void ShouldIgnoreReactionEvent_ReturnsTrue_ForBotNormalReaction()
        {
            var result = ReactionMirroringService.ShouldIgnoreReactionEvent(42UL, 42UL, ReactionType.Normal);

            Assert.True(result);
        }

        [Fact]
        public void ShouldIgnoreReactionEvent_ReturnsTrue_ForBurstReaction()
        {
            var result = ReactionMirroringService.ShouldIgnoreReactionEvent(100UL, 42UL, ReactionType.Burst);

            Assert.True(result);
        }

        [Fact]
        public void GetReactionKey_ReturnsStableUnicodeKey()
        {
            var result = ReactionMirroringService.GetReactionKey(new Emoji("👍"));

            Assert.Equal("unicode:👍", result);
        }

        [Fact]
        public void GetReactionKey_ReturnsStableCustomEmojiKey()
        {
            var result = ReactionMirroringService.GetReactionKey(Emote.Parse("<:party:123456789012345678>"));

            Assert.Equal("custom:123456789012345678", result);
        }

        [Fact]
        public void ContainsNonBotUsers_ReturnsFalse_WhenOnlyBotsExist()
        {
            var botUser = CreateUser(isBot: true);

            var result = ReactionMirroringService.ContainsNonBotUsers(new List<IUser> { botUser });

            Assert.False(result);
        }

        [Fact]
        public void ContainsNonBotUsers_ReturnsTrue_WhenAtLeastOneHumanExists()
        {
            var botUser = CreateUser(isBot: true);
            var humanUser = CreateUser(isBot: false);

            var result = ReactionMirroringService.ContainsNonBotUsers(new List<IUser> { botUser, humanUser });

            Assert.True(result);
        }

        [Fact]
        public async Task MirrorReactionAddedAsync_DoesNothing_WhenMessageIsUnlinked()
        {
            await _dbService.InitializeDatabaseAsync();
            var discordMessageService = new TestDiscordMessageService();
            var logger = new ListLogger<ReactionMirroringService>();
            var service = CreateService(discordMessageService, logger);

            await service.MirrorReactionAddedAsync(12345UL, 54321UL, new Emoji("👍"), ReactionType.Normal, 999UL);

            Assert.Equal(0, discordMessageService.GetChannelAsyncCalls);
            Assert.Empty(discordMessageService.AddedReactions);
            Assert.Empty(discordMessageService.RemovedReactions);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Information
                    && entry.EventId.Id == 1307
                    && entry.Message.Contains("12345")
                    && entry.Message.Contains("54321")
                    && entry.Message.Contains("👍"));
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1305
                    && entry.Message.Contains("12345")
                    && entry.Message.Contains("54321"));
        }

        [Fact]
        public async Task MirrorReactionAddedAsync_DoesNothing_ForBotReactionEvents()
        {
            await _dbService.InitializeDatabaseAsync();
            var discordMessageService = new TestDiscordMessageService { CurrentUserId = 42UL };
            var logger = new ListLogger<ReactionMirroringService>();
            var service = CreateService(discordMessageService, logger);

            await service.MirrorReactionAddedAsync(12345UL, 54321UL, new Emoji("👍"), ReactionType.Normal, 42UL);

            Assert.Equal(0, discordMessageService.GetChannelAsyncCalls);
            Assert.Empty(discordMessageService.AddedReactions);
            Assert.Empty(discordMessageService.RemovedReactions);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1304
                    && entry.Message.Contains("12345")
                    && entry.Message.Contains("👍")
                    && entry.Message.Contains("Normal"));
        }

        [Fact]
        public async Task MirrorReactionRemovedAsync_DoesNothing_ForBurstReactionEvents()
        {
            await _dbService.InitializeDatabaseAsync();
            var discordMessageService = new TestDiscordMessageService();
            var service = CreateService(discordMessageService);

            await service.MirrorReactionRemovedAsync(12345UL, 54321UL, new Emoji("👍"), ReactionType.Burst, 999UL);

            Assert.Equal(0, discordMessageService.GetChannelAsyncCalls);
            Assert.Empty(discordMessageService.AddedReactions);
            Assert.Empty(discordMessageService.RemovedReactions);
        }

        [Fact]
        public async Task MirrorReactionAddedAsync_AddsBotReaction_ToMessagesMissingTheEmoji()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1000UL,
                originalChannelId: 10UL,
                (2000UL, 20UL, "de"),
                (3000UL, 30UL, "it"));

            var thumbsUp = new Emoji("👍");
            var originalState = CreateMessageState((thumbsUp, 1, false));
            var firstMirrorState = CreateMessageState();
            var secondMirrorState = CreateMessageState((thumbsUp, 1, true));

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(10UL, 1000UL, originalState);
            discordMessageService.RegisterMessage(20UL, 2000UL, firstMirrorState);
            discordMessageService.RegisterMessage(30UL, 3000UL, secondMirrorState);
            discordMessageService.SetReactionUsers(originalState.Message, thumbsUp, new[] { CreateUser(isBot: false) });

            var service = CreateService(discordMessageService);

            await service.MirrorReactionAddedAsync(1000UL, 10UL, thumbsUp, ReactionType.Normal, 777UL);

            Assert.Single(discordMessageService.AddedReactions);
            Assert.Same(firstMirrorState.Message, discordMessageService.AddedReactions[0].Message);
            Assert.Equal("unicode:👍", discordMessageService.AddedReactions[0].EmoteKey);
            Assert.Empty(discordMessageService.RemovedReactions);
        }

        [Fact]
        public async Task MirrorReactionAddedAsync_MirrorsCustomEmojiFamilies()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1100UL,
                originalChannelId: 11UL,
                (2100UL, 21UL, "de"));

            var partyEmote = Emote.Parse("<:party:123456789012345678>");
            var originalState = CreateMessageState((partyEmote, 1, false));
            var mirrorState = CreateMessageState();

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(11UL, 1100UL, originalState);
            discordMessageService.RegisterMessage(21UL, 2100UL, mirrorState);
            discordMessageService.SetReactionUsers(originalState.Message, partyEmote, new[] { CreateUser(isBot: false) });

            var service = CreateService(discordMessageService);

            await service.MirrorReactionAddedAsync(1100UL, 11UL, partyEmote, ReactionType.Normal, 888UL);

            Assert.Single(discordMessageService.AddedReactions);
            Assert.Same(mirrorState.Message, discordMessageService.AddedReactions[0].Message);
            Assert.Equal("custom:123456789012345678", discordMessageService.AddedReactions[0].EmoteKey);
        }

        [Fact]
        public async Task MirrorReactionAddedAsync_UsesTriggerEvent_WhenMasterCopyMetadataIsStale()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1150UL,
                originalChannelId: 15UL,
                (2150UL, 25UL, "en"),
                (3150UL, 35UL, "master"),
                (4150UL, 45UL, "it"));

            var thumbsUp = new Emoji("👍");
            var originalState = CreateMessageState();
            var nativeReplyState = CreateMessageState();
            var masterState = CreateMessageState();
            var siblingState = CreateMessageState();

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(15UL, 1150UL, originalState);
            discordMessageService.RegisterMessage(25UL, 2150UL, nativeReplyState);
            discordMessageService.RegisterMessage(35UL, 3150UL, masterState);
            discordMessageService.RegisterMessage(45UL, 4150UL, siblingState);

            var service = CreateService(discordMessageService);

            await service.MirrorReactionAddedAsync(3150UL, 35UL, thumbsUp, ReactionType.Normal, 777UL);

            Assert.Equal(3, discordMessageService.AddedReactions.Count);
            Assert.DoesNotContain(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, masterState.Message));
            Assert.Contains(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, originalState.Message));
            Assert.Contains(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, nativeReplyState.Message));
            Assert.Contains(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, siblingState.Message));
        }

        [Fact]
        public async Task MirrorReactionAddedAsync_UsesTriggerEvent_WhenOriginalLocalizedMetadataIsStale()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1160UL,
                originalChannelId: 16UL,
                (2160UL, 16UL, "en"),
                (3160UL, 36UL, "master"),
                (4160UL, 46UL, "de"));

            var thumbsUp = new Emoji("👍");
            var originalState = CreateMessageState();
            var nativeReplyState = CreateMessageState();
            var masterState = CreateMessageState();
            var siblingState = CreateMessageState();

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(16UL, 1160UL, originalState);
            discordMessageService.RegisterMessage(16UL, 2160UL, nativeReplyState);
            discordMessageService.RegisterMessage(36UL, 3160UL, masterState);
            discordMessageService.RegisterMessage(46UL, 4160UL, siblingState);

            var service = CreateService(discordMessageService);

            await service.MirrorReactionAddedAsync(1160UL, 16UL, thumbsUp, ReactionType.Normal, 888UL);

            Assert.Equal(3, discordMessageService.AddedReactions.Count);
            Assert.DoesNotContain(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, originalState.Message));
            Assert.Contains(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, nativeReplyState.Message));
            Assert.Contains(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, masterState.Message));
            Assert.Contains(discordMessageService.AddedReactions, item => ReferenceEquals(item.Message, siblingState.Message));
        }

        [Fact]
        public async Task MirrorReactionRemovedAsync_RemovesBotReaction_FromAllMessages_WhenNoHumanReactionRemains()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1200UL,
                originalChannelId: 12UL,
                (2200UL, 22UL, "de"),
                (3200UL, 32UL, "it"));

            var thumbsUp = new Emoji("👍");
            var originalState = CreateMessageState((thumbsUp, 1, true));
            var firstMirrorState = CreateMessageState((thumbsUp, 1, true));
            var secondMirrorState = CreateMessageState((thumbsUp, 1, true));

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(12UL, 1200UL, originalState);
            discordMessageService.RegisterMessage(22UL, 2200UL, firstMirrorState);
            discordMessageService.RegisterMessage(32UL, 3200UL, secondMirrorState);
            discordMessageService.SetReactionUsers(originalState.Message, thumbsUp, new[] { CreateUser(isBot: true) });
            discordMessageService.SetReactionUsers(firstMirrorState.Message, thumbsUp, new[] { CreateUser(isBot: true) });
            discordMessageService.SetReactionUsers(secondMirrorState.Message, thumbsUp, new[] { CreateUser(isBot: true) });

            var service = CreateService(discordMessageService);

            await service.MirrorReactionRemovedAsync(2200UL, 22UL, thumbsUp, ReactionType.Normal, 777UL);

            Assert.Equal(3, discordMessageService.RemovedReactions.Count);
            Assert.Contains(discordMessageService.RemovedReactions, item => ReferenceEquals(item.Message, originalState.Message));
            Assert.Contains(discordMessageService.RemovedReactions, item => ReferenceEquals(item.Message, firstMirrorState.Message));
            Assert.Contains(discordMessageService.RemovedReactions, item => ReferenceEquals(item.Message, secondMirrorState.Message));
            Assert.Empty(discordMessageService.AddedReactions);
        }

        [Fact]
        public async Task MirrorReactionRemovedAsync_KeepsMirrors_WhenHumanReactionStillExistsElsewhere()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1300UL,
                originalChannelId: 13UL,
                (2300UL, 23UL, "de"),
                (3300UL, 33UL, "it"));

            var thumbsUp = new Emoji("👍");
            var originalState = CreateMessageState((thumbsUp, 1, false));
            var firstMirrorState = CreateMessageState((thumbsUp, 1, true));
            var secondMirrorState = CreateMessageState();

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(13UL, 1300UL, originalState);
            discordMessageService.RegisterMessage(23UL, 2300UL, firstMirrorState);
            discordMessageService.RegisterMessage(33UL, 3300UL, secondMirrorState);
            discordMessageService.SetReactionUsers(originalState.Message, thumbsUp, new[] { CreateUser(isBot: false) });

            var service = CreateService(discordMessageService);

            await service.MirrorReactionRemovedAsync(2300UL, 23UL, thumbsUp, ReactionType.Normal, 999UL);

            Assert.Single(discordMessageService.AddedReactions);
            Assert.Same(secondMirrorState.Message, discordMessageService.AddedReactions[0].Message);
            Assert.Empty(discordMessageService.RemovedReactions);
        }

        [Fact]
        public async Task MirrorReactionsClearedAsync_RemovesTrackedMirroredEmotes()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1400UL,
                originalChannelId: 14UL,
                (2400UL, 24UL, "de"),
                (3400UL, 34UL, "it"));

            var thumbsUp = new Emoji("👍");
            var originalState = CreateMessageState();
            var firstMirrorState = CreateMessageState((thumbsUp, 1, true));
            var secondMirrorState = CreateMessageState((thumbsUp, 1, true));

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(14UL, 1400UL, originalState);
            discordMessageService.RegisterMessage(24UL, 2400UL, firstMirrorState);
            discordMessageService.RegisterMessage(34UL, 3400UL, secondMirrorState);
            discordMessageService.SetReactionUsers(firstMirrorState.Message, thumbsUp, new[] { CreateUser(isBot: true) });
            discordMessageService.SetReactionUsers(secondMirrorState.Message, thumbsUp, new[] { CreateUser(isBot: true) });

            var service = CreateService(discordMessageService);

            await service.MirrorReactionsClearedAsync(1400UL, 14UL);

            Assert.Equal(2, discordMessageService.RemovedReactions.Count);
            Assert.Contains(discordMessageService.RemovedReactions, item => ReferenceEquals(item.Message, firstMirrorState.Message));
            Assert.Contains(discordMessageService.RemovedReactions, item => ReferenceEquals(item.Message, secondMirrorState.Message));
        }

        [Fact]
        public async Task MirrorReactionAddedAsync_SkipsMissingLinkedMessages_AndContinuesProcessingOtherTargets()
        {
            await _dbService.InitializeDatabaseAsync();
            await SeedLinkedFamilyAsync(
                originalMessageId: 1500UL,
                originalChannelId: 15UL,
                (2500UL, 25UL, "de"),
                (3500UL, 35UL, "it"));

            var thumbsUp = new Emoji("👍");
            var originalState = CreateMessageState((thumbsUp, 1, false));
            var secondMirrorState = CreateMessageState();

            var discordMessageService = new TestDiscordMessageService();
            discordMessageService.RegisterMessage(15UL, 1500UL, originalState);
            discordMessageService.RegisterChannel(25UL, new Dictionary<ulong, IMessage?>());
            discordMessageService.RegisterMessage(35UL, 3500UL, secondMirrorState);
            discordMessageService.SetReactionUsers(originalState.Message, thumbsUp, new[] { CreateUser(isBot: false) });

            var service = CreateService(discordMessageService);

            await service.MirrorReactionAddedAsync(1500UL, 15UL, thumbsUp, ReactionType.Normal, 555UL);

            Assert.Single(discordMessageService.AddedReactions);
            Assert.Same(secondMirrorState.Message, discordMessageService.AddedReactions[0].Message);
        }

        [Fact]
        public async Task MirrorReactionsRemovedForEmoteAsync_DoesNothing_ForLegacyMirroredMessagesWithoutBackfill()
        {
            await CreateLegacyMessageLinksDatabaseAsync(1600UL, 2600UL, 26UL, "de");
            await _dbService.InitializeDatabaseAsync();

            var discordMessageService = new TestDiscordMessageService();
            var logger = new ListLogger<ReactionMirroringService>();
            var service = CreateService(discordMessageService, logger);

            await service.MirrorReactionsRemovedForEmoteAsync(2600UL, 26UL, new Emoji("👍"));

            Assert.Equal(0, discordMessageService.GetChannelAsyncCalls);
            Assert.Empty(discordMessageService.AddedReactions);
            Assert.Empty(discordMessageService.RemovedReactions);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1305
                    && entry.Message.Contains("2600")
                    && entry.Message.Contains("26"));
        }

        private ReactionMirroringService CreateService(
            TestDiscordMessageService discordMessageService,
            ILogger<ReactionMirroringService>? logger = null)
        {
            return new ReactionMirroringService(
                discordMessageService,
                _dbService,
                logger ?? NullLogger<ReactionMirroringService>.Instance);
        }

        private async Task SeedLinkedFamilyAsync(
            ulong originalMessageId,
            ulong originalChannelId,
            params (ulong MirroredMessageId, ulong ChannelId, string LanguageCode)[] mirroredMessages)
        {
            foreach (var mirroredMessage in mirroredMessages)
            {
                await _dbService.LinkMessagesAsync(
                    originalMessageId,
                    originalChannelId,
                    mirroredMessage.MirroredMessageId,
                    mirroredMessage.ChannelId,
                    mirroredMessage.LanguageCode);
            }
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

        private static TestMessageState CreateMessageState(params (IEmote Emote, int NormalCount, bool IsMe)[] reactions)
        {
            var reactionMap = new Dictionary<IEmote, ReactionMetadata>();
            foreach (var reaction in reactions)
            {
                reactionMap[reaction.Emote] = CreateReactionMetadata(reaction.NormalCount, reaction.IsMe);
            }

            var messageMock = new Mock<IMessage>();
            messageMock
                .SetupGet(message => message.Reactions)
                .Returns(() => reactionMap);

            return new TestMessageState(messageMock.Object, reactionMap);
        }

        private static ReactionMetadata CreateReactionMetadata(int normalCount, bool isMe)
        {
            object boxed = default(ReactionMetadata);
            SetReactionMetadataField(boxed, "<ReactionCount>k__BackingField", normalCount);
            SetReactionMetadataField(boxed, "<IsMe>k__BackingField", isMe);
            SetReactionMetadataField(boxed, "<BurstCount>k__BackingField", 0);
            SetReactionMetadataField(boxed, "<NormalCount>k__BackingField", normalCount);
            SetReactionMetadataField(boxed, "<BurstColors>k__BackingField", Array.Empty<Color>());
            return (ReactionMetadata)boxed;
        }

        private static void SetReactionMetadataField(object boxedReactionMetadata, string fieldName, object value)
        {
            var field = typeof(ReactionMetadata).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Could not find ReactionMetadata field '{fieldName}'.");
            field.SetValue(boxedReactionMetadata, value);
        }

        private static IUser CreateUser(bool isBot)
        {
            var userMock = new Mock<IUser>();
            userMock.SetupGet(user => user.IsBot).Returns(isBot);
            return userMock.Object;
        }

        private sealed class TestDiscordMessageService : IDiscordMessageService
        {
            private readonly Dictionary<ulong, IMessageChannel?> _channels = [];
            private readonly Dictionary<IMessage, Dictionary<IEmote, ReactionMetadata>> _messageStates = [];
            private readonly Dictionary<(IMessage Message, string EmoteKey), List<IReadOnlyCollection<IUser>>> _reactionUsers = [];
            private readonly IUser _botUser = CreateUser(isBot: true);

            public ulong CurrentUserId { get; set; } = 424242UL;

            public int GetChannelAsyncCalls { get; private set; }

            public List<ReactionOperation> AddedReactions { get; } = [];

            public List<ReactionOperation> RemovedReactions { get; } = [];

            public Task<IMessageChannel?> GetChannelAsync(ulong channelId)
            {
                GetChannelAsyncCalls++;
                _channels.TryGetValue(channelId, out var channel);
                return Task.FromResult(channel);
            }

            public Task AddReactionAsync(IMessage message, IEmote emote)
            {
                AddedReactions.Add(new ReactionOperation(message, ReactionMirroringService.GetReactionKey(emote)));
                if (_messageStates.TryGetValue(message, out var reactions))
                {
                    var existingKey = reactions.Keys.FirstOrDefault(key => ReactionMirroringService.GetReactionKey(key) == ReactionMirroringService.GetReactionKey(emote));
                    if (existingKey == null)
                    {
                        reactions[emote] = CreateReactionMetadata(1, true);
                    }
                }

                return Task.CompletedTask;
            }

            public Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId)
            {
                throw new NotSupportedException("DeleteMessageAsync is not used by reaction mirroring tests.");
            }

            public Task RemoveOwnReactionAsync(IMessage message, IEmote emote)
            {
                RemovedReactions.Add(new ReactionOperation(message, ReactionMirroringService.GetReactionKey(emote)));
                if (_messageStates.TryGetValue(message, out var reactions))
                {
                    var existingKey = reactions.Keys.FirstOrDefault(key => ReactionMirroringService.GetReactionKey(key) == ReactionMirroringService.GetReactionKey(emote));
                    if (existingKey != null)
                    {
                        reactions.Remove(existingKey);
                    }
                }

                return Task.CompletedTask;
            }

            public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(IMessage message, IEmote emote, int limit)
            {
                var emoteKey = ReactionMirroringService.GetReactionKey(emote);
                if (_reactionUsers.TryGetValue((message, emoteKey), out var pages))
                {
                    return YieldPagesAsync(pages);
                }

                if (_messageStates.TryGetValue(message, out var reactions))
                {
                    var existingKey = reactions.Keys.FirstOrDefault(key => ReactionMirroringService.GetReactionKey(key) == emoteKey);
                    if (existingKey != null && reactions[existingKey].IsMe)
                    {
                        return YieldPagesAsync([[ _botUser ]]);
                    }
                }

                return YieldPagesAsync([]);
            }

            public void RegisterMessage(ulong channelId, ulong messageId, TestMessageState messageState)
            {
                _messageStates[messageState.Message] = messageState.Reactions;
                RegisterChannel(channelId, new Dictionary<ulong, IMessage?> { [messageId] = messageState.Message });
            }

            public void RegisterChannel(ulong channelId, Dictionary<ulong, IMessage?> messagesById)
            {
                var channelMock = new Mock<IMessageChannel>();
                channelMock
                    .Setup(channel => channel.GetMessageAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                    .ReturnsAsync((ulong messageId, CacheMode _, RequestOptions _) =>
                        messagesById.TryGetValue(messageId, out var message) ? message : null);

                _channels[channelId] = channelMock.Object;
            }

            public void SetReactionUsers(IMessage message, IEmote emote, params IReadOnlyCollection<IUser>[] pages)
            {
                _reactionUsers[(message, ReactionMirroringService.GetReactionKey(emote))] = pages.ToList();
            }

            private static async IAsyncEnumerable<IReadOnlyCollection<IUser>> YieldPagesAsync(IEnumerable<IReadOnlyCollection<IUser>> pages)
            {
                foreach (var page in pages)
                {
                    yield return page;
                    await Task.Yield();
                }
            }
        }

        public sealed class TestMessageState
        {
            public TestMessageState(IMessage message, Dictionary<IEmote, ReactionMetadata> reactions)
            {
                Message = message;
                Reactions = reactions;
            }

            public IMessage Message { get; }

            public Dictionary<IEmote, ReactionMetadata> Reactions { get; }
        }

        public sealed record ReactionOperation(IMessage Message, string EmoteKey);
    }
}
