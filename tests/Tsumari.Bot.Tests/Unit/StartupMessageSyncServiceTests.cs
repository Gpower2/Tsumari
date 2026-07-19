using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services;
using Tsumari.Bot.Services.Abstractions;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class StartupMessageSyncServiceTests : IDisposable
    {
        private readonly TemporarySqliteDatabase _database;
        private readonly DatabaseService _dbService;

        public StartupMessageSyncServiceTests()
        {
            _database = new TemporarySqliteDatabase("startup-sync");
            _dbService = _database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            _database.Dispose();
        }

        [Fact]
        public async Task RunAsync_SkipsChannelsWithNoBaseline()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            var discordMessageServiceMock = new Mock<IDiscordMessageService>();

            var service = new StartupMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            var result = await service.RunAsync(CancellationToken.None);

            Assert.Equal(1, result.ChannelsChecked);
            Assert.Equal(0, result.ChannelsSynced);
            historicalSyncMock.Verify(
                service => service.HasUnprocessedMessagesAsync(It.IsAny<ulong>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_PostsAnnouncementAndCompletionWhenMessagesFound()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);
            await _dbService.LinkMessagesAsync(1UL, 100UL, 10UL, 100UL, "master", DateTimeOffset.UtcNow - TimeSpan.FromHours(2));

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            historicalSyncMock
                .Setup(service => service.SyncMasterChannelAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoricalSyncResult
                {
                    Success = true,
                    ProcessedCount = 3,
                    FailedCount = 0,
                    SkippedCount = 0
                });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.SendMessageAsync(100UL, It.IsAny<string>()))
                .ReturnsAsync(Mock.Of<IUserMessage>());

            var service = new StartupMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            var result = await service.RunAsync(CancellationToken.None);

            Assert.Equal(1, result.ChannelsChecked);
            Assert.Equal(1, result.ChannelsSynced);
            Assert.Equal(3, result.ProcessedCount);
            discordMessageServiceMock.Verify(
                service => service.SendMessageAsync(100UL, "I found some messages that I missed... 🫣 Syncing them now! 🙇"),
                Times.Once);
            discordMessageServiceMock.Verify(
                service => service.SendMessageAsync(100UL, "Messages synced! 🥳"),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_QueriesDiscordForMissingTimestampAndCachesIt()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);
            await _dbService.LinkMessagesAsync(ulong.MaxValue, 100UL, 10UL, 100UL, "master");

            var expectedTimestamp = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetMessageTimestampAsync(100UL, ulong.MaxValue))
                .ReturnsAsync(expectedTimestamp);

            var service = new StartupMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            await service.RunAsync(CancellationToken.None);

            var cachedTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(100UL);
            Assert.Equal(expectedTimestamp, cachedTimestamp);
            discordMessageServiceMock.Verify(service => service.GetMessageTimestampAsync(100UL, ulong.MaxValue), Times.Once);
        }

        [Fact]
        public async Task RunAsync_FallsBackToSnowflakeTimestamp_WhenDiscordQueryReturnsNull()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);
            var messageId = 178033636437071874UL; // Known Discord snowflake from 2016.
            await _dbService.LinkMessagesAsync(messageId, 100UL, 10UL, 100UL, "master");

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.GetMessageTimestampAsync(100UL, messageId))
                .ReturnsAsync((DateTimeOffset?)null);

            var service = new StartupMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            await service.RunAsync(CancellationToken.None);

            var cachedTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(100UL);
            Assert.NotNull(cachedTimestamp);
            Assert.True(cachedTimestamp.Value.Year >= 2016);
        }

        [Fact]
        public async Task RunAsync_DoesNotPostWhenNoUnprocessedMessages()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);
            await _dbService.LinkMessagesAsync(1UL, 100UL, 10UL, 100UL, "master", DateTimeOffset.UtcNow - TimeSpan.FromHours(2));

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();

            var service = new StartupMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            var result = await service.RunAsync(CancellationToken.None);

            Assert.Equal(0, result.ChannelsSynced);
            discordMessageServiceMock.Verify(
                service => service.SendMessageAsync(It.IsAny<ulong>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_ContinuesWithNextChannel_WhenOneChannelThrows()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);
            await _dbService.AddMasterChannelAsync(200UL);
            await _dbService.LinkMessagesAsync(1UL, 100UL, 10UL, 100UL, "master", DateTimeOffset.UtcNow - TimeSpan.FromHours(2));
            await _dbService.LinkMessagesAsync(2UL, 200UL, 20UL, 200UL, "master", DateTimeOffset.UtcNow - TimeSpan.FromHours(2));

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Discord API unavailable"));
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(200UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            historicalSyncMock
                .Setup(service => service.SyncMasterChannelAsync(200UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoricalSyncResult { Success = true, ProcessedCount = 3, FailedCount = 0, SkippedCount = 0 });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.SendMessageAsync(It.IsAny<ulong>(), It.IsAny<string>()))
                .ReturnsAsync(Mock.Of<IUserMessage>());

            var service = new StartupMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            var result = await service.RunAsync(CancellationToken.None);

            Assert.Equal(2, result.ChannelsChecked);
            Assert.Equal(1, result.ChannelsSynced);
            Assert.Equal(3, result.ProcessedCount);
            historicalSyncMock.Verify(service => service.SyncMasterChannelAsync(200UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_PropagatesOperationCanceledException()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);
            await _dbService.LinkMessagesAsync(1UL, 100UL, 10UL, 100UL, "master", DateTimeOffset.UtcNow - TimeSpan.FromHours(2));

            using var cts = new CancellationTokenSource();
            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException(cts.Token));

            var service = new StartupMessageSyncService(
                _dbService,
                Mock.Of<IDiscordMessageService>(),
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunAsync(cts.Token));
        }

        [Fact]
        public async Task RunAsync_AggregatesResultsAcrossChannels()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(100UL);
            await _dbService.AddMasterChannelAsync(200UL);
            await _dbService.LinkMessagesAsync(1UL, 100UL, 10UL, 100UL, "master", DateTimeOffset.UtcNow - TimeSpan.FromHours(2));
            await _dbService.LinkMessagesAsync(2UL, 200UL, 20UL, 200UL, "master", DateTimeOffset.UtcNow - TimeSpan.FromHours(2));

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();
            historicalSyncMock
                .Setup(service => service.HasUnprocessedMessagesAsync(It.IsAny<ulong>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            historicalSyncMock
                .Setup(service => service.SyncMasterChannelAsync(100UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoricalSyncResult { Success = true, ProcessedCount = 2, FailedCount = 1, SkippedCount = 0 });
            historicalSyncMock
                .Setup(service => service.SyncMasterChannelAsync(200UL, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoricalSyncResult { Success = true, ProcessedCount = 3, FailedCount = 0, SkippedCount = 1 });

            var discordMessageServiceMock = new Mock<IDiscordMessageService>();
            discordMessageServiceMock
                .Setup(service => service.SendMessageAsync(It.IsAny<ulong>(), It.IsAny<string>()))
                .ReturnsAsync(Mock.Of<IUserMessage>());

            var service = new StartupMessageSyncService(
                _dbService,
                discordMessageServiceMock.Object,
                historicalSyncMock.Object,
                NullLogger<StartupMessageSyncService>.Instance);

            var result = await service.RunAsync(CancellationToken.None);

            Assert.Equal(2, result.ChannelsChecked);
            Assert.Equal(2, result.ChannelsSynced);
            Assert.Equal(5, result.ProcessedCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(1, result.SkippedCount);
        }
    }
}
