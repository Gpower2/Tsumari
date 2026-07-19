using System.Threading;
using System.Threading.Tasks;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.Services.Abstractions
{
    public interface IStartupMessageSyncService
    {
        Task<StartupSyncResult> RunAsync(CancellationToken cancellationToken = default);
    }
}
