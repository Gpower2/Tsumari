using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class LinkedMessageDeletionService
    {
        private readonly IDiscordMessageService _discordMessageService;
        private readonly DatabaseService _dbService;
        private readonly ILogger<LinkedMessageDeletionService> _logger;

        public LinkedMessageDeletionService(
            IDiscordMessageService discordMessageService,
            DatabaseService dbService,
            ILogger<LinkedMessageDeletionService> logger)
        {
            _discordMessageService = discordMessageService;
            _dbService = dbService;
            _logger = logger;
        }

        public async Task HandleMessageDeletedAsync(ulong messageId)
        {
            var mirroredMessages = await _dbService.GetMirroredMessagesAsync(messageId);
            if (mirroredMessages.Count == 0)
            {
                await _dbService.DeleteMessageLinkByMirroredMessageIdAsync(messageId);
                return;
            }

            foreach (var mirroredMessage in mirroredMessages)
            {
                try
                {
                    var deleted = await _discordMessageService.DeleteMessageAsync(
                        mirroredMessage.ChannelId,
                        mirroredMessage.MirroredMessageId);

                    if (!deleted)
                    {
                        _logger.LogLinkedMessageNotResolvedDuringDelete(mirroredMessage.MirroredMessageId, mirroredMessage.ChannelId, messageId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogLinkedMessageDeleteFailed(ex, mirroredMessage.MirroredMessageId, mirroredMessage.ChannelId, messageId);
                }
            }

            await _dbService.DeleteMessageLinksAsync(messageId);
        }

        public async Task HandleMessagesDeletedAsync(IEnumerable<ulong> messageIds)
        {
            foreach (var messageId in messageIds)
            {
                await HandleMessageDeletedAsync(messageId);
            }
        }
    }
}
