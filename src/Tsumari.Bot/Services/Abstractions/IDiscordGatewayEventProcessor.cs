using Discord;

namespace Tsumari.Bot.Services.Abstractions
{
    public interface IDiscordGatewayEventProcessor
    {
        Task ProcessAsync(GatewayIngressEvent gatewayEvent);

        Task ProcessMessageReceivedAsync(IMessage rawMessage);

        Task ProcessMessageDeletedAsync(ulong messageId);

        Task ProcessMessagesBulkDeletedAsync(IReadOnlyCollection<ulong> messageIds, ulong channelId);

        Task ProcessMessageUpdatedAsync(bool hadCachedSnapshot, string? beforeContent, IMessage afterMessage);

        Task ProcessReactionAddedAsync(DiscordReactionEvent reaction);

        Task ProcessReactionRemovedAsync(DiscordReactionEvent reaction);

        Task ProcessReactionsClearedAsync(ulong messageId, ulong channelId);

        Task ProcessReactionsRemovedForEmoteAsync(ulong messageId, ulong channelId, IEmote emote);
    }
}
