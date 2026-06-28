using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class DiscordGatewayEventProcessorService : IDiscordGatewayEventProcessor
    {
        private readonly MirroredMessageRoutingService _mirroredMessageRoutingService;
        private readonly EditedMessageSyncService _editedMessageSyncService;
        private readonly LinkedMessageDeletionService _linkedMessageDeletionService;
        private readonly ReactionMirroringService _reactionMirroringService;
        private readonly ILogger<DiscordGatewayEventProcessorService> _logger;

        public DiscordGatewayEventProcessorService(
            MirroredMessageRoutingService mirroredMessageRoutingService,
            EditedMessageSyncService editedMessageSyncService,
            LinkedMessageDeletionService linkedMessageDeletionService,
            ReactionMirroringService reactionMirroringService,
            ILogger<DiscordGatewayEventProcessorService> logger)
        {
            _mirroredMessageRoutingService = mirroredMessageRoutingService;
            _editedMessageSyncService = editedMessageSyncService;
            _linkedMessageDeletionService = linkedMessageDeletionService;
            _reactionMirroringService = reactionMirroringService;
            _logger = logger;
        }

        public async Task ProcessAsync(GatewayIngressEvent gatewayEvent)
        {
            switch (gatewayEvent)
            {
                case MessageReceivedGatewayEvent messageReceived:
                    await ProcessMessageReceivedAsync(messageReceived.Message);
                    break;
                case MessageUpdatedGatewayEvent messageUpdated:
                    await ProcessMessageUpdatedEventAsync(messageUpdated.BeforeCache, messageUpdated.AfterMessage);
                    break;
                case MessageDeletedGatewayEvent messageDeleted:
                    await ProcessMessageDeletedAsync(messageDeleted.MessageId);
                    break;
                case MessagesBulkDeletedGatewayEvent bulkDeleted:
                    await ProcessMessagesBulkDeletedAsync(bulkDeleted.MessageIds, bulkDeleted.ChannelId);
                    break;
                case ReactionAddedGatewayEvent reactionAdded:
                    await ProcessReactionAddedAsync(reactionAdded.Reaction);
                    break;
                case ReactionRemovedGatewayEvent reactionRemoved:
                    await ProcessReactionRemovedAsync(reactionRemoved.Reaction);
                    break;
                case ReactionsClearedGatewayEvent reactionsCleared:
                    await ProcessReactionsClearedAsync(reactionsCleared.MessageId, reactionsCleared.ChannelId);
                    break;
                case ReactionsRemovedForEmoteGatewayEvent reactionsRemovedForEmote:
                    await ProcessReactionsRemovedForEmoteAsync(
                        reactionsRemovedForEmote.MessageId,
                        reactionsRemovedForEmote.ChannelId,
                        reactionsRemovedForEmote.Emote);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported gateway event type '{gatewayEvent.GetType().Name}'.");
            }
        }

        public async Task ProcessMessageReceivedAsync(IMessage rawMessage)
        {
            try
            {
                await _mirroredMessageRoutingService.HandleMessageReceivedAsync(rawMessage);
            }
            catch (Exception ex)
            {
                _logger.LogMessageRoutingFailed(ex, rawMessage.Id);
            }
        }

        public async Task ProcessMessageDeletedAsync(ulong messageId)
        {
            try
            {
                await _linkedMessageDeletionService.HandleMessageDeletedAsync(messageId);
            }
            catch (Exception ex)
            {
                _logger.LogDeleteSynchronizationFailed(ex, messageId);
            }
        }

        public async Task ProcessMessagesBulkDeletedAsync(IReadOnlyCollection<ulong> messageIds, ulong channelId)
        {
            try
            {
                await _linkedMessageDeletionService.HandleMessagesDeletedAsync(messageIds);
            }
            catch (Exception ex)
            {
                _logger.LogBulkDeleteSynchronizationFailed(ex, channelId);
            }
        }

        public async Task ProcessMessageUpdatedAsync(bool hadCachedSnapshot, string? beforeContent, IMessage afterMessage)
        {
            try
            {
                await _editedMessageSyncService.HandleMessageUpdatedAsync(hadCachedSnapshot, beforeContent, afterMessage);
            }
            catch (Exception ex)
            {
                _logger.LogEditSynchronizationFailed(ex, afterMessage.Id);
            }
        }

        private async Task ProcessMessageUpdatedEventAsync(Cacheable<IMessage, ulong> beforeCache, IMessage afterMessage)
        {
            try
            {
                var hadCachedSnapshot = beforeCache.HasValue;
                var beforeMessage = hadCachedSnapshot
                    ? await beforeCache.GetOrDownloadAsync()
                    : null;
                var beforeContent = beforeMessage?.Content ?? string.Empty;

                await ProcessMessageUpdatedAsync(hadCachedSnapshot, beforeContent, afterMessage);
            }
            catch (Exception ex)
            {
                _logger.LogEditedMessageEventHandlingFailed(ex, afterMessage.Id);
            }
        }

        public async Task ProcessReactionAddedAsync(DiscordReactionEvent reaction)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionAddedAsync(
                    reaction.MessageId,
                    reaction.ChannelId,
                    reaction.Emote,
                    reaction.ReactionType,
                    reaction.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogReactionAddedMirroringFailed(ex, reaction.MessageId);
            }
        }

        public async Task ProcessReactionRemovedAsync(DiscordReactionEvent reaction)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionRemovedAsync(
                    reaction.MessageId,
                    reaction.ChannelId,
                    reaction.Emote,
                    reaction.ReactionType,
                    reaction.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogReactionRemovedMirroringFailed(ex, reaction.MessageId);
            }
        }

        public async Task ProcessReactionsClearedAsync(ulong messageId, ulong channelId)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionsClearedAsync(messageId, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogReactionsClearedMirroringFailed(ex, messageId);
            }
        }

        public async Task ProcessReactionsRemovedForEmoteAsync(ulong messageId, ulong channelId, IEmote emote)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionsRemovedForEmoteAsync(messageId, channelId, emote);
            }
            catch (Exception ex)
            {
                _logger.LogReactionsRemovedForEmoteMirroringFailed(ex, messageId);
            }
        }
    }
}
