using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Logging;
using Tsumari.Bot.Models;
using Tsumari.Bot.Services.Abstractions;

namespace Tsumari.Bot.Services
{
    public class StartupMessageSyncService : IStartupMessageSyncService
    {
        private const string AnnouncementMessage = "I found some messages that I missed... 🫣 Syncing them now! 🙇";
        private const string CompletionMessage = "Messages synced! 🥳";

        private readonly DatabaseService _dbService;
        private readonly IDiscordMessageService _discordMessageService;
        private readonly IHistoricalMessageSyncService _historicalSyncService;
        private readonly ILogger<StartupMessageSyncService> _logger;

        public StartupMessageSyncService(
            DatabaseService dbService,
            IDiscordMessageService discordMessageService,
            IHistoricalMessageSyncService historicalSyncService,
            ILogger<StartupMessageSyncService> logger)
        {
            _dbService = dbService;
            _discordMessageService = discordMessageService;
            _historicalSyncService = historicalSyncService;
            _logger = logger;
        }

        public async Task<StartupSyncResult> RunAsync(CancellationToken cancellationToken = default)
        {
            var result = new StartupSyncResult();
            var masterChannelIds = await _dbService.GetAllMasterChannelIdsAsync();

            foreach (var masterChannelId in masterChannelIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                result.ChannelsChecked++;

                try
                {
                    var lastTimestamp = await ResolveLastTrackedTimestampAsync(masterChannelId);
                    if (lastTimestamp == null)
                    {
                        _logger.LogStartupSyncNoBaseline(masterChannelId);
                        continue;
                    }

                    var hasUnprocessedMessages = await _historicalSyncService.HasUnprocessedMessagesAsync(
                        masterChannelId,
                        lastTimestamp.Value,
                        cancellationToken);

                    if (!hasUnprocessedMessages)
                    {
                        _logger.LogStartupSyncNoMissedMessages(masterChannelId);
                        continue;
                    }

                    await AnnounceAsync(masterChannelId);

                    var syncResult = await _historicalSyncService.SyncMasterChannelAsync(
                        masterChannelId,
                        lastTimestamp.Value,
                        cancellationToken);

                    if (!syncResult.Success)
                    {
                        _logger.LogStartupSyncChannelFailed(masterChannelId, syncResult.ErrorMessage ?? "unknown");
                        continue;
                    }

                    result.ProcessedCount += syncResult.ProcessedCount;
                    result.FailedCount += syncResult.FailedCount;
                    result.SkippedCount += syncResult.SkippedCount;
                    result.ChannelsSynced++;

                    await CompleteAsync(masterChannelId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogStartupSyncChannelException(masterChannelId, ex);
                }
            }

            _logger.LogStartupSyncCompleted(
                result.ChannelsChecked,
                result.ChannelsSynced,
                result.ProcessedCount,
                result.FailedCount,
                result.SkippedCount);

            return result;
        }

        private async Task<DateTimeOffset?> ResolveLastTrackedTimestampAsync(ulong masterChannelId)
        {
            var storedTimestamp = await _dbService.GetLastTrackedMessageTimestampAsync(masterChannelId);
            if (storedTimestamp != null)
            {
                return storedTimestamp;
            }

            var lastMessageId = await _dbService.GetLastTrackedOriginalMessageIdAsync(masterChannelId);
            if (lastMessageId == null)
            {
                return null;
            }

            var discordTimestamp = await ResolveTimestampFromDiscordAsync(masterChannelId, lastMessageId.Value);
            if (discordTimestamp != null)
            {
                await _dbService.SetOriginalMessageTimestampAsync(lastMessageId.Value, discordTimestamp.Value);
                return discordTimestamp;
            }

            // Fallback to the timestamp encoded in the Discord snowflake ID. This is accurate to the
            // millisecond Discord assigned the ID, so it is a safe baseline for startup sync.
            var snowflakeTimestamp = SnowflakeToDateTimeOffset(lastMessageId.Value);
            await _dbService.SetOriginalMessageTimestampAsync(lastMessageId.Value, snowflakeTimestamp);
            return snowflakeTimestamp;
        }

        private async Task<DateTimeOffset?> ResolveTimestampFromDiscordAsync(ulong channelId, ulong messageId)
        {
            try
            {
                return await _discordMessageService.GetMessageTimestampAsync(channelId, messageId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogStartupSyncTimestampResolveFailed(channelId, messageId, ex);
                return null;
            }
        }

        private static DateTimeOffset SnowflakeToDateTimeOffset(ulong snowflakeId)
        {
            const ulong discordEpochMilliseconds = 1420070400000UL;
            var milliseconds = (long)((snowflakeId >> 22) + discordEpochMilliseconds);
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        private async Task AnnounceAsync(ulong masterChannelId)
        {
            var announcement = await _discordMessageService.SendMessageAsync(masterChannelId, AnnouncementMessage);
            if (announcement == null)
            {
                _logger.LogStartupSyncAnnouncementFailed(masterChannelId);
            }
        }

        private async Task CompleteAsync(ulong masterChannelId)
        {
            var completion = await _discordMessageService.SendMessageAsync(masterChannelId, CompletionMessage);
            if (completion == null)
            {
                _logger.LogStartupSyncCompletionFailed(masterChannelId);
            }
        }
    }
}
