using System.Threading.Tasks;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Tests
{
    /// <summary>
    /// Test double that bypasses retry logic so unit/component tests can exercise the underlying
    /// Discord operations directly without timeouts or backoff.
    /// </summary>
    public sealed class PassThroughDiscordRestRetryPolicy : IDiscordRestRetryPolicy
    {
        public Task ExecuteAsync(Func<Task> operation, string operationName) => operation();

        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName) => operation();
    }
}
