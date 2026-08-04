using Tsumari.Bot.Services.Abstractions;

namespace Tsumari.Bot.Services
{
    /// <inheritdoc />
    public sealed class GatewayReadinessCoordinator : IGatewayReadinessCoordinator
    {
        private readonly TaskCompletionSource _readySource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForReadyAsync(CancellationToken cancellationToken = default)
            => _readySource.Task.WaitAsync(cancellationToken);

        public void MarkReady() => _readySource.TrySetResult();
    }
}
