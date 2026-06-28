using System.Collections.Generic;

namespace Tsumari.Bot.Services
{
    public sealed class MirroredMessageLink
    {
        public ulong MirroredMessageId { get; init; }

        public ulong ChannelId { get; init; }

        public string LanguageCode { get; init; } = string.Empty;

        public ulong? OriginalChannelId { get; init; }
    }

    public sealed class LinkedMessageFamily
    {
        public ulong OriginalMessageId { get; init; }

        public ulong OriginalChannelId { get; init; }

        public List<MirroredMessageLink> MirroredMessages { get; init; } = [];
    }
}
