using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class GatewayEventGroupResolverTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;

        public GatewayEventGroupResolverTests()
        {
            _testDbPath = $"test_tsumari_gateway_resolver_{Guid.NewGuid():N}.db";

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(configuration => configuration["Database:FilePath"]).Returns(_testDbPath);

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
        public async Task ResolveDispatchesAsync_UsesMasterChannelAsGroupKey()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);

            var resolver = new GatewayEventGroupResolver(_dbService, NullLogger<GatewayEventGroupResolver>.Instance);
            var message = CreateMessage(100UL, 10UL);

            var dispatches = await resolver.ResolveDispatchesAsync(new MessageReceivedGatewayEvent(message.Object));

            var dispatch = Assert.Single(dispatches);
            Assert.Equal(10UL, dispatch.GroupKey);
            Assert.IsType<MessageReceivedGatewayEvent>(dispatch.Event);
        }

        [Fact]
        public async Task ResolveDispatchesAsync_UsesParentMasterForLocalizedChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");

            var resolver = new GatewayEventGroupResolver(_dbService, NullLogger<GatewayEventGroupResolver>.Instance);
            var message = CreateMessage(200UL, 20UL);

            var dispatches = await resolver.ResolveDispatchesAsync(new MessageReceivedGatewayEvent(message.Object));

            var dispatch = Assert.Single(dispatches);
            Assert.Equal(10UL, dispatch.GroupKey);
        }

        [Fact]
        public async Task ResolveDispatchesAsync_MapsMirroredDeleteBackToOriginalGroup()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.LinkMessagesAsync(500UL, 20UL, 700UL, 10UL, "master");

            var resolver = new GatewayEventGroupResolver(_dbService, NullLogger<GatewayEventGroupResolver>.Instance);

            var dispatches = await resolver.ResolveDispatchesAsync(new MessageDeletedGatewayEvent(700UL, 10UL));

            var dispatch = Assert.Single(dispatches);
            Assert.Equal(10UL, dispatch.GroupKey);
            var deleteEvent = Assert.IsType<MessageDeletedGatewayEvent>(dispatch.Event);
            Assert.Equal(700UL, deleteEvent.MessageId);
        }

        [Fact]
        public async Task ResolveDispatchesAsync_SplitsBulkDeletesPerTrackedMessage()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.LinkMessagesAsync(500UL, 20UL, 700UL, 10UL, "master");
            await _dbService.LinkMessagesAsync(501UL, 20UL, 701UL, 20UL, "de");

            var resolver = new GatewayEventGroupResolver(_dbService, NullLogger<GatewayEventGroupResolver>.Instance);

            var dispatches = await resolver.ResolveDispatchesAsync(new MessagesBulkDeletedGatewayEvent([700UL, 701UL, 999UL], 10UL));

            Assert.Equal(2, dispatches.Count);
            Assert.All(dispatches, dispatch => Assert.Equal(10UL, dispatch.GroupKey));
            Assert.Contains(dispatches, dispatch => Assert.IsType<MessageDeletedGatewayEvent>(dispatch.Event).MessageId == 700UL);
            Assert.Contains(dispatches, dispatch => Assert.IsType<MessageDeletedGatewayEvent>(dispatch.Event).MessageId == 701UL);
        }

        private static Mock<IMessage> CreateMessage(ulong messageId, ulong channelId)
        {
            var channel = new Mock<IMessageChannel>();
            channel.As<ISnowflakeEntity>().SetupGet(entity => entity.Id).Returns(channelId);

            var message = new Mock<IMessage>();
            message.As<ISnowflakeEntity>().SetupGet(entity => entity.Id).Returns(messageId);
            message.SetupGet(value => value.Channel).Returns(channel.Object);

            return message;
        }
    }
}
