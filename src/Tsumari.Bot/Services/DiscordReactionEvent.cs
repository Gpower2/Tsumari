using Discord;

namespace Tsumari.Bot.Services
{
    public sealed class DiscordReactionEvent
    {
        public required ulong MessageId { get; init; }

        public required ulong ChannelId { get; init; }

        public required IEmote Emote { get; init; }

        public required ReactionType ReactionType { get; init; }

        public required ulong UserId { get; init; }
    }
}
