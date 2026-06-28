using Discord;

namespace Tsumari.Bot.Models
{
    public abstract record GatewayIngressEvent;

    public sealed record MessageReceivedGatewayEvent(IMessage Message) : GatewayIngressEvent;

    public sealed record MessageUpdatedGatewayEvent(Cacheable<IMessage, ulong> BeforeCache, IMessage AfterMessage) : GatewayIngressEvent;

    public sealed record MessageDeletedGatewayEvent(ulong MessageId, ulong ChannelId) : GatewayIngressEvent;

    public sealed record MessagesBulkDeletedGatewayEvent(IReadOnlyCollection<ulong> MessageIds, ulong ChannelId) : GatewayIngressEvent;

    public sealed record ReactionAddedGatewayEvent(DiscordReactionEvent Reaction) : GatewayIngressEvent;

    public sealed record ReactionRemovedGatewayEvent(DiscordReactionEvent Reaction) : GatewayIngressEvent;

    public sealed record ReactionsClearedGatewayEvent(ulong MessageId, ulong ChannelId) : GatewayIngressEvent;

    public sealed record ReactionsRemovedForEmoteGatewayEvent(ulong MessageId, ulong ChannelId, IEmote Emote) : GatewayIngressEvent;

    public sealed record GatewayDispatchItem(ulong GroupKey, GatewayIngressEvent Event);
}
