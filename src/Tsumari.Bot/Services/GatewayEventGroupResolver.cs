using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class GatewayEventGroupResolver : IGatewayEventGroupResolver
    {
        private static readonly IReadOnlyList<GatewayDispatchItem> EmptyDispatchItems = Array.Empty<GatewayDispatchItem>();

        private readonly DatabaseService _dbService;
        private readonly ILogger<GatewayEventGroupResolver> _logger;

        public GatewayEventGroupResolver(
            DatabaseService dbService,
            ILogger<GatewayEventGroupResolver> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<GatewayDispatchItem>> ResolveDispatchesAsync(GatewayIngressEvent gatewayEvent)
        {
            switch (gatewayEvent)
            {
                case MessageReceivedGatewayEvent messageReceived:
                    return await ResolveChannelScopedDispatchAsync(messageReceived, messageReceived.Message.Channel.Id);

                case MessageUpdatedGatewayEvent messageUpdated:
                    return await ResolveChannelScopedDispatchAsync(messageUpdated, messageUpdated.AfterMessage.Channel.Id);

                case MessageDeletedGatewayEvent messageDeleted:
                    return await ResolveMessageScopedDispatchAsync(messageDeleted.MessageId, messageDeleted.ChannelId, messageDeleted);

                case MessagesBulkDeletedGatewayEvent bulkDeleted:
                    return await ResolveBulkDeleteDispatchesAsync(bulkDeleted);

                case ReactionAddedGatewayEvent reactionAdded:
                    return await ResolveMessageScopedDispatchAsync(reactionAdded.Reaction.MessageId, reactionAdded.Reaction.ChannelId, reactionAdded);

                case ReactionRemovedGatewayEvent reactionRemoved:
                    return await ResolveMessageScopedDispatchAsync(reactionRemoved.Reaction.MessageId, reactionRemoved.Reaction.ChannelId, reactionRemoved);

                case ReactionsClearedGatewayEvent reactionsCleared:
                    return await ResolveMessageScopedDispatchAsync(reactionsCleared.MessageId, reactionsCleared.ChannelId, reactionsCleared);

                case ReactionsRemovedForEmoteGatewayEvent reactionsRemovedForEmote:
                    return await ResolveMessageScopedDispatchAsync(reactionsRemovedForEmote.MessageId, reactionsRemovedForEmote.ChannelId, reactionsRemovedForEmote);

                default:
                    _logger.LogUnsupportedGatewayEventType(gatewayEvent.GetType().Name);
                    return EmptyDispatchItems;
            }
        }

        private async Task<IReadOnlyList<GatewayDispatchItem>> ResolveChannelScopedDispatchAsync(GatewayIngressEvent gatewayEvent, ulong channelId)
        {
            var groupKey = await _dbService.GetLinkedGroupKeyForChannelAsync(channelId);
            if (!groupKey.HasValue)
            {
                return EmptyDispatchItems;
            }

            return [new GatewayDispatchItem(groupKey.Value, gatewayEvent)];
        }

        private async Task<IReadOnlyList<GatewayDispatchItem>> ResolveMessageScopedDispatchAsync(ulong messageId, ulong knownChannelId, GatewayIngressEvent gatewayEvent)
        {
            var groupKey = await _dbService.GetLinkedGroupKeyForMessageAsync(messageId, knownChannelId);
            if (!groupKey.HasValue)
            {
                return EmptyDispatchItems;
            }

            return [new GatewayDispatchItem(groupKey.Value, gatewayEvent)];
        }

        private async Task<IReadOnlyList<GatewayDispatchItem>> ResolveBulkDeleteDispatchesAsync(MessagesBulkDeletedGatewayEvent bulkDeleted)
        {
            if (bulkDeleted.MessageIds.Count == 0)
            {
                return EmptyDispatchItems;
            }

            var dispatchItems = new List<GatewayDispatchItem>(bulkDeleted.MessageIds.Count);
            foreach (var messageId in bulkDeleted.MessageIds)
            {
                var groupKey = await _dbService.GetLinkedGroupKeyForMessageAsync(messageId, bulkDeleted.ChannelId);
                if (!groupKey.HasValue)
                {
                    continue;
                }

                dispatchItems.Add(new GatewayDispatchItem(
                    groupKey.Value,
                    new MessageDeletedGatewayEvent(messageId, bulkDeleted.ChannelId)));
            }

            return dispatchItems;
        }
    }
}
