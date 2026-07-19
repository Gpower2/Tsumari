using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services.Abstractions;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class StartupMessageSyncHostedServiceTests : IDisposable
    {
        private readonly DiscordSocketClient _client;

        public StartupMessageSyncHostedServiceTests()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        [Fact]
        public async Task OnReadyAsync_RunsSyncOnFirstCall()
        {
            var syncServiceMock = new Mock<IStartupMessageSyncService>();
            syncServiceMock
                .Setup(service => service.RunAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.StartupSyncResult());

            var hostedService = new StartupMessageSyncHostedService(
                _client,
                syncServiceMock.Object,
                NullLogger<StartupMessageSyncHostedService>.Instance);

            await hostedService.OnReadyAsync();

            syncServiceMock.Verify(service => service.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnReadyAsync_RunsSyncOnlyOnceForMultipleCalls()
        {
            var syncServiceMock = new Mock<IStartupMessageSyncService>();
            syncServiceMock
                .Setup(service => service.RunAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.StartupSyncResult());

            var hostedService = new StartupMessageSyncHostedService(
                _client,
                syncServiceMock.Object,
                NullLogger<StartupMessageSyncHostedService>.Instance);

            await hostedService.OnReadyAsync();
            await hostedService.OnReadyAsync();
            await hostedService.OnReadyAsync();

            syncServiceMock.Verify(service => service.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnReadyAsync_PassesStoppingTokenToSyncService()
        {
            using var cts = new CancellationTokenSource();
            var syncServiceMock = new Mock<IStartupMessageSyncService>();
            syncServiceMock
                .Setup(service => service.RunAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.StartupSyncResult());

            var hostedService = new StartupMessageSyncHostedService(
                _client,
                syncServiceMock.Object,
                NullLogger<StartupMessageSyncHostedService>.Instance);

            // Simulate ExecuteAsync registering the token before Ready fires.
            var stoppingTokenField = typeof(StartupMessageSyncHostedService).GetField(
                "_stoppingToken",
                BindingFlags.NonPublic | BindingFlags.Instance);
            stoppingTokenField!.SetValue(hostedService, cts.Token);

            await hostedService.OnReadyAsync();

            syncServiceMock.Verify(service => service.RunAsync(cts.Token), Times.Once);
        }
    }
}
