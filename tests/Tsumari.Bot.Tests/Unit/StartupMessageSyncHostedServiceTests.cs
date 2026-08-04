using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Tsumari.Bot.Services.Abstractions;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class StartupMessageSyncHostedServiceTests
    {
        [Fact]
        public async Task ExecuteAsync_RunsSyncAfterCoordinatorMarksReady()
        {
            var readinessCoordinator = new GatewayReadinessCoordinator();
            var syncServiceMock = new Mock<IStartupMessageSyncService>();
            syncServiceMock
                .Setup(service => service.RunAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.StartupSyncResult());

            using var cts = new CancellationTokenSource();
            var hostedService = new StartupMessageSyncHostedService(
                readinessCoordinator,
                syncServiceMock.Object,
                NullLogger<StartupMessageSyncHostedService>.Instance);

            var executeTask = StartExecuteAsync(hostedService, cts.Token);

            // The service should be waiting, not running sync yet.
            syncServiceMock.Verify(service => service.RunAsync(It.IsAny<CancellationToken>()), Times.Never);

            readinessCoordinator.MarkReady();

            await executeTask.WaitAsync(TimeSpan.FromSeconds(2));

            syncServiceMock.Verify(service => service.RunAsync(cts.Token), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_DoesNotRunSyncWhenHostStopsBeforeReady()
        {
            var readinessCoordinator = new GatewayReadinessCoordinator();
            var syncServiceMock = new Mock<IStartupMessageSyncService>();
            syncServiceMock
                .Setup(service => service.RunAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.StartupSyncResult());

            using var cts = new CancellationTokenSource();
            var hostedService = new StartupMessageSyncHostedService(
                readinessCoordinator,
                syncServiceMock.Object,
                NullLogger<StartupMessageSyncHostedService>.Instance);

            var executeTask = StartExecuteAsync(hostedService, cts.Token);
            cts.Cancel();

            await executeTask.WaitAsync(TimeSpan.FromSeconds(2));

            syncServiceMock.Verify(service => service.RunAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_RunsSyncOnlyOnceForMultipleReadySignals()
        {
            var readinessCoordinator = new GatewayReadinessCoordinator();
            var syncServiceMock = new Mock<IStartupMessageSyncService>();
            syncServiceMock
                .Setup(service => service.RunAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.StartupSyncResult());

            using var cts = new CancellationTokenSource();
            var hostedService = new StartupMessageSyncHostedService(
                readinessCoordinator,
                syncServiceMock.Object,
                NullLogger<StartupMessageSyncHostedService>.Instance);

            var executeTask = StartExecuteAsync(hostedService, cts.Token);
            readinessCoordinator.MarkReady();
            readinessCoordinator.MarkReady();

            await executeTask.WaitAsync(TimeSpan.FromSeconds(2));

            syncServiceMock.Verify(service => service.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        private static Task StartExecuteAsync(StartupMessageSyncHostedService service, CancellationToken token)
        {
            var method = typeof(StartupMessageSyncHostedService).GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (Task)method!.Invoke(service, new object[] { token })!;
        }
    }
}
