using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class HistoricalMessageSyncService : IHistoricalMessageSyncService
    {
        // Safety cap: fetching more than this per channel likely means the user picked too wide
        // a window and continuing could spend minutes and hit Discord rate limits.
        private const int MaxMessagesPerChannel = 5000;

        // SQLite has a parameter limit; keep batches well below the common 999 ceiling so the
        // final "still untracked" check does not fail on very large sync windows.
        private const int TrackingBatchSize = 500;

        private readonly DatabaseService _dbService;
        private readonly IDiscordMessageService _discordMessageService;
        private readonly IMirroredMessageRoutingService _routingService;
        private readonly ILogger<HistoricalMessageSyncService> _logger;

        public HistoricalMessageSyncService(
            DatabaseService dbService,
            IDiscordMessageService discordMessageService,
            IMirroredMessageRoutingService routingService,
            ILogger<HistoricalMessageSyncService> logger)
        {
            _dbService = dbService;
            _discordMessageService = discordMessageService;
            _routingService = routingService;
            _logger = logger;
        }

        public async Task<HistoricalSyncResult> SyncMasterChannelAsync(
            ulong masterChannelId,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            var cutoff = DateTimeOffset.UtcNow - duration;
            return await SyncMasterChannelAsync(masterChannelId, cutoff, cancellationToken);
        }

        public async Task<HistoricalSyncResult> SyncMasterChannelAsync(
            ulong masterChannelId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
        {
            var masterChannel = await _discordMessageService.GetChannelAsync(masterChannelId);
            if (masterChannel == null)
            {
                return HistoricalSyncResult.Failure("Master channel could not be resolved.");
            }

            var localizedChannels = await _dbService.GetLocalizedChannelsForMasterAsync(masterChannelId);
            var allCandidateMessages = new List<IUserMessage>();
            var skipped = 0;

            _logger.LogHistoricalSyncScanningChannel(masterChannelId, isMaster: true);
            var (masterCandidates, masterSkipped) = await CollectUnprocessedMessagesAsync(masterChannel, cutoff, MaxMessagesPerChannel, cancellationToken);
            allCandidateMessages.AddRange(masterCandidates);
            skipped += masterSkipped;

            foreach (var (localizedChannelId, targetLanguageCode) in localizedChannels)
            {
                var localizedChannel = await _discordMessageService.GetChannelAsync(localizedChannelId);
                if (localizedChannel == null)
                {
                    _logger.LogHistoricalSyncChannelNotResolved(localizedChannelId);
                    continue;
                }

                _logger.LogHistoricalSyncScanningChannel(localizedChannelId, isMaster: false);
                var (localizedCandidates, localizedSkipped) = await CollectUnprocessedMessagesAsync(localizedChannel, cutoff, MaxMessagesPerChannel, cancellationToken);
                allCandidateMessages.AddRange(localizedCandidates);
                skipped += localizedSkipped;
            }

            // Process oldest first so reply chains resolve: a parent message is synced before
            // any reply that references it, allowing ReplyMirroringService to build references.
            var orderedMessages = allCandidateMessages
                .OrderBy(m => m.Timestamp)
                .ToList();

            // Final batch check: live gateway events may have routed messages while we were
            // scanning other channels. Filtering here avoids one DB round-trip per message.
            var stillUntrackedMessages = await FilterStillUntrackedAsync(orderedMessages);
            skipped += orderedMessages.Count - stillUntrackedMessages.Count;

            _logger.LogHistoricalSyncProcessingStarted(stillUntrackedMessages.Count, masterChannelId, cutoff);

            var processed = 0;
            var failed = 0;

            foreach (var message in stillUntrackedMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await _routingService.RouteHistoricalMessageAsync(message, message.Timestamp);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogHistoricalSyncMessageFailed(ex, message.Id);
                }
            }

            _logger.LogHistoricalSyncCompleted(masterChannelId, processed, failed, skipped);

            return new HistoricalSyncResult
            {
                Success = true,
                ProcessedCount = processed,
                FailedCount = failed,
                SkippedCount = skipped
            };
        }

        public async Task<bool> HasUnprocessedMessagesAsync(
            ulong masterChannelId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
        {
            var masterChannel = await _discordMessageService.GetChannelAsync(masterChannelId);
            if (masterChannel == null)
            {
                return false;
            }

            var (masterCandidates, _) = await CollectUnprocessedMessagesAsync(
                masterChannel,
                cutoff,
                MaxMessagesPerChannel,
                cancellationToken,
                stopOnFirstCandidate: true);
            if (masterCandidates.Count > 0)
            {
                return true;
            }

            var localizedChannels = await _dbService.GetLocalizedChannelsForMasterAsync(masterChannelId);
            foreach (var (localizedChannelId, targetLanguageCode) in localizedChannels)
            {
                var localizedChannel = await _discordMessageService.GetChannelAsync(localizedChannelId);
                if (localizedChannel == null)
                {
                    continue;
                }

                var (localizedCandidates, _) = await CollectUnprocessedMessagesAsync(
                    localizedChannel,
                    cutoff,
                    MaxMessagesPerChannel,
                    cancellationToken,
                    stopOnFirstCandidate: true);
                if (localizedCandidates.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<(List<IUserMessage> candidates, int skipped)> CollectUnprocessedMessagesAsync(
            IMessageChannel channel,
            DateTimeOffset cutoff,
            int messageLimit,
            CancellationToken cancellationToken,
            bool stopOnFirstCandidate = false)
        {
            var candidates = new List<IUserMessage>();
            var skipped = 0;

            await foreach (var page in channel.GetMessagesAsync(messageLimit)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                var reachedCutoff = false;
                var pageCandidates = new List<IUserMessage>();

                foreach (var message in page)
                {
                    if (message is not IUserMessage userMessage || userMessage.Source != MessageSource.User)
                    {
                        continue;
                    }

                    if (message.Timestamp < cutoff)
                    {
                        reachedCutoff = true;
                        break;
                    }

                    var hasContent = !string.IsNullOrWhiteSpace(userMessage.Content);
                    var hasAttachments = userMessage.Attachments != null && userMessage.Attachments.Count > 0;
                    if (!hasContent && !hasAttachments)
                    {
                        continue;
                    }

                    pageCandidates.Add(userMessage);
                }

                if (pageCandidates.Count > 0)
                {
                    var trackedIds = await _dbService.GetTrackedOriginalMessageIdsAsync(
                        pageCandidates.Select(m => m.Id).ToList());

                    foreach (var userMessage in pageCandidates)
                    {
                        if (trackedIds.Contains(userMessage.Id))
                        {
                            skipped++;
                            continue;
                        }

                        candidates.Add(userMessage);

                        if (stopOnFirstCandidate)
                        {
                            return (candidates, skipped);
                        }
                    }
                }

                if (reachedCutoff)
                {
                    break;
                }
            }

            return (candidates, skipped);
        }

        private async Task<List<IUserMessage>> FilterStillUntrackedAsync(List<IUserMessage> messages)
        {
            if (messages.Count == 0)
            {
                return messages;
            }

            var trackedIds = new HashSet<ulong>();
            for (var i = 0; i < messages.Count; i += TrackingBatchSize)
            {
                var batch = messages.Skip(i).Take(TrackingBatchSize).Select(m => m.Id).ToList();
                var trackedBatch = await _dbService.GetTrackedOriginalMessageIdsAsync(batch);
                trackedIds.UnionWith(trackedBatch);
            }

            return messages.Where(m => !trackedIds.Contains(m.Id)).ToList();
        }
    }
}
