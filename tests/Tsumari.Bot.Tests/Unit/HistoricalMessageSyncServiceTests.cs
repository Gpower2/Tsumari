using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Tsumari.Bot.Services.Abstractions;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class HistoricalMessageSyncServiceTests : IDisposable
    {
        private readonly TemporarySqliteDatabase _database;
        private readonly DatabaseService _dbService;

        public HistoricalMessageSyncServiceTests()
        {
            _database = new TemporarySqliteDatabase("historical-sync");
            _dbService = _database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            _database.Dispose();
        }

        [Fact]
        public async Task SyncMasterChannelAsync_ProcessesUnprocessedMessagesInChronologicalOrder()
        {
            await _dbService.InitializeDatabaseAsync();

            var masterChannelId = 100UL;
            var localChannelId = 200UL;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

            await _dbService.AddMasterChannelAsync(masterChannelId);
            await _dbService.RegisterLocalChannelAsync(localChannelId, masterChannelId, "el");

            // Order in channel history (newest first): msg2, msg1 in master; msg3 in local
            var msg1 = CreateUserMessage(1UL, masterChannelId, cutoff + TimeSpan.FromMinutes(10), "oldest");
            var msg2 = CreateUserMessage(2UL, masterChannelId, cutoff + TimeSpan.FromMinutes(20), "middle");
            var msg3 = CreateUserMessage(3UL, localChannelId, cutoff + TimeSpan.FromMinutes(30), "newest");

            var masterChannelMock = CreateChannelMock(new[] { msg2, msg1 });
            var localChannelMock = CreateChannelMock(new[] { msg3 });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(masterChannelId))
                .ReturnsAsync(masterChannelMock.Object);
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(localChannelId))
                .ReturnsAsync(localChannelMock.Object);

            var processedOrder = new List<ulong>();
            var routingServiceMock = new Mock<IMirroredMessageRoutingService>();
            routingServiceMock
                .Setup(service => service.RouteHistoricalMessageAsync(It.IsAny<IUserMessage>(), It.IsAny<DateTimeOffset>()))
                .Callback<IUserMessage, DateTimeOffset>((message, _) => processedOrder.Add(message.Id))
                .Returns(Task.CompletedTask);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                routingServiceMock.Object,
                NullLogger<HistoricalMessageSyncService>.Instance);

            var result = await service.SyncMasterChannelAsync(masterChannelId, TimeSpan.FromHours(1));

            Assert.True(result.Success);
            Assert.Equal(3, result.ProcessedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(0, result.SkippedCount);
            // Chronological order: msg1 (oldest), msg2, msg3 (newest)
            Assert.Equal(new[] { 1UL, 2UL, 3UL }, processedOrder);
        }

        [Fact]
        public async Task SyncMasterChannelAsync_SkipsTrackedMessagesAndMessagesOutsideCutoff()
        {
            await _dbService.InitializeDatabaseAsync();

            var masterChannelId = 100UL;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

            await _dbService.AddMasterChannelAsync(masterChannelId);

            var insideTracked = CreateUserMessage(1UL, masterChannelId, cutoff + TimeSpan.FromMinutes(5), "tracked");
            var insideNew = CreateUserMessage(2UL, masterChannelId, cutoff + TimeSpan.FromMinutes(15), "new");
            var outside = CreateUserMessage(3UL, masterChannelId, cutoff - TimeSpan.FromMinutes(5), "too old");

            await _dbService.LinkMessagesAsync(1UL, masterChannelId, 10UL, masterChannelId, "master");

            var channelMock = CreateChannelMock(new[] { insideNew, insideTracked, outside });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(masterChannelId))
                .ReturnsAsync(channelMock.Object);

            var routingServiceMock = new Mock<IMirroredMessageRoutingService>();
            routingServiceMock
                .Setup(service => service.RouteHistoricalMessageAsync(It.IsAny<IUserMessage>(), It.IsAny<DateTimeOffset>()))
                .Returns(Task.CompletedTask);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                routingServiceMock.Object,
                NullLogger<HistoricalMessageSyncService>.Instance);

            var result = await service.SyncMasterChannelAsync(masterChannelId, TimeSpan.FromHours(1));

            Assert.True(result.Success);
            Assert.Equal(1, result.ProcessedCount);
            Assert.Equal(1, result.SkippedCount);
            routingServiceMock.Verify(
                service => service.RouteHistoricalMessageAsync(insideNew, insideNew.Timestamp),
                Times.Once);
            routingServiceMock.Verify(
                service => service.RouteHistoricalMessageAsync(insideTracked, It.IsAny<DateTimeOffset>()),
                Times.Never);
            routingServiceMock.Verify(
                service => service.RouteHistoricalMessageAsync(outside, It.IsAny<DateTimeOffset>()),
                Times.Never);
        }

        [Fact]
        public async Task SyncMasterChannelAsync_ReturnsFailure_WhenMasterChannelCannotBeResolved()
        {
            await _dbService.InitializeDatabaseAsync();

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(100UL))
                .ReturnsAsync((IMessageChannel?)null);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                Mock.Of<IMirroredMessageRoutingService>(),
                NullLogger<HistoricalMessageSyncService>.Instance);

            var result = await service.SyncMasterChannelAsync(100UL, TimeSpan.FromHours(1));

            Assert.False(result.Success);
            Assert.Equal("Master channel could not be resolved.", result.ErrorMessage);
        }

        [Fact]
        public async Task SyncMasterChannelAsync_WithCutoff_ProcessesMessagesNewerThanCutoff()
        {
            await _dbService.InitializeDatabaseAsync();

            var masterChannelId = 100UL;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

            await _dbService.AddMasterChannelAsync(masterChannelId);

            var older = CreateUserMessage(1UL, masterChannelId, cutoff - TimeSpan.FromMinutes(5), "older");
            var newer = CreateUserMessage(2UL, masterChannelId, cutoff + TimeSpan.FromMinutes(5), "newer");

            var channelMock = CreateChannelMock(new[] { newer, older });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(masterChannelId))
                .ReturnsAsync(channelMock.Object);

            var routingServiceMock = new Mock<IMirroredMessageRoutingService>();
            routingServiceMock
                .Setup(service => service.RouteHistoricalMessageAsync(It.IsAny<IUserMessage>(), It.IsAny<DateTimeOffset>()))
                .Returns(Task.CompletedTask);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                routingServiceMock.Object,
                NullLogger<HistoricalMessageSyncService>.Instance);

            var result = await service.SyncMasterChannelAsync(masterChannelId, cutoff);

            Assert.True(result.Success);
            Assert.Equal(1, result.ProcessedCount);
            routingServiceMock.Verify(
                service => service.RouteHistoricalMessageAsync(newer, newer.Timestamp),
                Times.Once);
            routingServiceMock.Verify(
                service => service.RouteHistoricalMessageAsync(older, It.IsAny<DateTimeOffset>()),
                Times.Never);
        }

        [Fact]
        public async Task HasUnprocessedMessagesAsync_ReturnsTrueWhenUnprocessedMessageExists()
        {
            await _dbService.InitializeDatabaseAsync();

            var masterChannelId = 100UL;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

            await _dbService.AddMasterChannelAsync(masterChannelId);

            var unprocessed = CreateUserMessage(1UL, masterChannelId, cutoff + TimeSpan.FromMinutes(5), "missed");

            var channelMock = CreateChannelMock(new[] { unprocessed });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(masterChannelId))
                .ReturnsAsync(channelMock.Object);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                Mock.Of<IMirroredMessageRoutingService>(),
                NullLogger<HistoricalMessageSyncService>.Instance);

            var hasUnprocessed = await service.HasUnprocessedMessagesAsync(masterChannelId, cutoff);

            Assert.True(hasUnprocessed);
        }

        [Fact]
        public async Task HasUnprocessedMessagesAsync_ReturnsTrueWhenUnprocessedMessageExistsBeyondFirstPage()
        {
            await _dbService.InitializeDatabaseAsync();

            var masterChannelId = 100UL;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

            await _dbService.AddMasterChannelAsync(masterChannelId);
            await _dbService.LinkMessagesAsync(1UL, masterChannelId, 10UL, masterChannelId, "master");

            // Page 1 contains only tracked/newer messages; page 2 contains the missed message.
            var tracked = CreateUserMessage(1UL, masterChannelId, cutoff + TimeSpan.FromMinutes(30), "tracked");
            var missed = CreateUserMessage(2UL, masterChannelId, cutoff + TimeSpan.FromMinutes(20), "missed");

            var channelMock = CreateChannelMock(new[] { tracked }, new[] { missed });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(masterChannelId))
                .ReturnsAsync(channelMock.Object);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                Mock.Of<IMirroredMessageRoutingService>(),
                NullLogger<HistoricalMessageSyncService>.Instance);

            var hasUnprocessed = await service.HasUnprocessedMessagesAsync(masterChannelId, cutoff);

            Assert.True(hasUnprocessed);
        }

        [Fact]
        public async Task HasUnprocessedMessagesAsync_ReturnsFalseWhenAllMessagesAreTrackedOrOld()
        {
            await _dbService.InitializeDatabaseAsync();

            var masterChannelId = 100UL;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

            await _dbService.AddMasterChannelAsync(masterChannelId);
            await _dbService.LinkMessagesAsync(1UL, masterChannelId, 10UL, masterChannelId, "master");

            var tracked = CreateUserMessage(1UL, masterChannelId, cutoff + TimeSpan.FromMinutes(5), "tracked");
            var old = CreateUserMessage(2UL, masterChannelId, cutoff - TimeSpan.FromMinutes(5), "old");

            var channelMock = CreateChannelMock(new[] { tracked, old });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(masterChannelId))
                .ReturnsAsync(channelMock.Object);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                Mock.Of<IMirroredMessageRoutingService>(),
                NullLogger<HistoricalMessageSyncService>.Instance);

            var hasUnprocessed = await service.HasUnprocessedMessagesAsync(masterChannelId, cutoff);

            Assert.False(hasUnprocessed);
        }

        [Fact]
        public async Task SyncMasterChannelAsync_BatchesFinalTrackingCheckForLargeCandidateSets()
        {
            await _dbService.InitializeDatabaseAsync();

            var masterChannelId = 100UL;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

            await _dbService.AddMasterChannelAsync(masterChannelId);

            // Create enough candidates to exceed the 500-item tracking batch size.
            var messages = new List<IUserMessage>();
            for (var i = 1; i <= 600; i++)
            {
                messages.Add(CreateUserMessage((ulong)i, masterChannelId, cutoff + TimeSpan.FromMinutes(i), $"message{i}"));
            }

            // Pre-track the first 100 messages so the final batched check must filter them out.
            for (var i = 1; i <= 100; i++)
            {
                await _dbService.LinkMessagesAsync((ulong)i, masterChannelId, (ulong)(1000 + i), masterChannelId, "master");
            }

            var channelMock = CreateChannelMock(messages);

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetChannelAsync(masterChannelId))
                .ReturnsAsync(channelMock.Object);

            var routingServiceMock = new Mock<IMirroredMessageRoutingService>();
            routingServiceMock
                .Setup(service => service.RouteHistoricalMessageAsync(It.IsAny<IUserMessage>(), It.IsAny<DateTimeOffset>()))
                .Returns(Task.CompletedTask);

            var service = new HistoricalMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                routingServiceMock.Object,
                NullLogger<HistoricalMessageSyncService>.Instance);

            var result = await service.SyncMasterChannelAsync(masterChannelId, cutoff);

            Assert.True(result.Success);
            Assert.Equal(500, result.ProcessedCount);
            Assert.Equal(100, result.SkippedCount);
            Assert.Equal(0, result.FailedCount);
            routingServiceMock.Verify(
                service => service.RouteHistoricalMessageAsync(It.Is<IUserMessage>(m => m.Id <= 100), It.IsAny<DateTimeOffset>()),
                Times.Never);
            routingServiceMock.Verify(
                service => service.RouteHistoricalMessageAsync(It.Is<IUserMessage>(m => m.Id > 100), It.IsAny<DateTimeOffset>()),
                Times.Exactly(500));
        }

        private static IUserMessage CreateUserMessage(ulong id, ulong channelId, DateTimeOffset timestamp, string content)
        {
            var channelMock = new Mock<IMessageChannel>();
            channelMock.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(channelId);

            var messageMock = new Mock<IUserMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(id);
            messageMock.SetupGet(message => message.Source).Returns(MessageSource.User);
            messageMock.SetupGet(message => message.Content).Returns(content);
            messageMock.SetupGet(message => message.Attachments).Returns(Array.Empty<IAttachment>());
            messageMock.SetupGet(message => message.Timestamp).Returns(timestamp);
            messageMock.SetupGet(message => message.Channel).Returns(channelMock.Object);

            return messageMock.Object;
        }

        private static Mock<IMessageChannel> CreateChannelMock(params IReadOnlyCollection<IUserMessage>[] pages)
        {
            var channelMock = new Mock<IMessageChannel>();
            channelMock
                .Setup(channel => channel.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(CreatePagedEnumerable(pages));
            return channelMock;
        }

        private static async IAsyncEnumerable<IReadOnlyCollection<IMessage>> CreatePagedEnumerable(
            IEnumerable<IReadOnlyCollection<IUserMessage>> pages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var page in pages)
            {
                yield return page;
            }

            // Discord.NET continues yielding empty pages once exhausted; returning without a final
            // empty page is sufficient for the await foreach loop to terminate.
            await Task.CompletedTask;
        }
    }
}
