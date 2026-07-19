using System;
using System.Threading;
using System.Threading.Tasks;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.Services.Abstractions
{
    public interface IHistoricalMessageSyncService
    {
        Task<HistoricalSyncResult> SyncMasterChannelAsync(
            ulong masterChannelId,
            TimeSpan duration,
            CancellationToken cancellationToken = default);

        Task<HistoricalSyncResult> SyncMasterChannelAsync(
            ulong masterChannelId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default);

        Task<bool> HasUnprocessedMessagesAsync(
            ulong masterChannelId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default);
    }
}
