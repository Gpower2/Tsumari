using System.Threading;
using System.Threading.Tasks;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class GatewayReadinessCoordinatorTests
    {
        [Fact]
        public async Task WaitForReadyAsync_Completes_WhenMarkReadyCalled()
        {
            var coordinator = new GatewayReadinessCoordinator();

            var waitTask = coordinator.WaitForReadyAsync(CancellationToken.None);
            Assert.False(waitTask.IsCompleted);

            coordinator.MarkReady();

            await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task WaitForReadyAsync_IsIdempotent()
        {
            var coordinator = new GatewayReadinessCoordinator();

            coordinator.MarkReady();

            await coordinator.WaitForReadyAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            await coordinator.WaitForReadyAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task WaitForReadyAsync_ThrowsOperationCanceledException_WhenTokenCanceled()
        {
            var coordinator = new GatewayReadinessCoordinator();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => coordinator.WaitForReadyAsync(cts.Token));
        }
    }
}
