namespace Tsumari.Bot.Services.Abstractions
{
    /// <summary>
    /// Signals when the Discord gateway hosted service has completed its initialization
    /// (database schema, interaction modules, global slash commands) and the bot is fully ready.
    /// </summary>
    public interface IGatewayReadinessCoordinator
    {
        /// <summary>
        /// Returns a task that completes once the gateway initialization has finished successfully.
        /// </summary>
        Task WaitForReadyAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks the gateway as ready. Should only be called after all initialization steps
        /// have completed successfully.
        /// </summary>
        void MarkReady();
    }
}
