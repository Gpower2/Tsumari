using System.Collections.Generic;
using System.Linq;

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

        public bool ContainsMessage(ulong messageId)
        {
            return messageId == OriginalMessageId || FindMirroredMessageById(messageId) != null;
        }

        public MirroredMessageLink? FindMirroredMessageById(ulong messageId)
        {
            return MirroredMessages.FirstOrDefault(link => link.MirroredMessageId == messageId);
        }

        public MirroredMessageLink? FindMirroredMessageByChannelId(ulong channelId)
        {
            return MirroredMessages.FirstOrDefault(link => link.ChannelId == channelId);
        }

        public ulong? ResolveReplyTargetMessageId(ulong repliedMessageId, ulong destinationChannelId)
        {
            if (!ContainsMessage(repliedMessageId))
            {
                return null;
            }

            if (destinationChannelId != OriginalChannelId)
            {
                return FindMirroredMessageByChannelId(destinationChannelId)?.MirroredMessageId;
            }

            if (repliedMessageId == OriginalMessageId)
            {
                return OriginalMessageId;
            }

            return FindMirroredMessageByChannelId(destinationChannelId)?.MirroredMessageId ?? OriginalMessageId;
        }
    }
}
