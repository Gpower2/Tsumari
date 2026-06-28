using System.Threading.Tasks;
using Discord;

namespace Tsumari.Bot.Services
{
    public class ReplyMirroringService
    {
        private readonly DatabaseService _dbService;

        public ReplyMirroringService(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        public async Task<ReplyMirroringContext?> ResolveReplyContextAsync(ulong sourceChannelId, MessageReference? sourceReference)
        {
            if (sourceReference == null || !sourceReference.MessageId.IsSpecified)
            {
                return null;
            }

            var repliedMessageId = sourceReference.MessageId.Value;
            var parentMessageFamily = await _dbService.GetLinkedMessageFamilyAsync(repliedMessageId, sourceChannelId);
            if (parentMessageFamily == null)
            {
                return null;
            }

            return new ReplyMirroringContext
            {
                RepliedMessageId = repliedMessageId,
                ParentMessageFamily = parentMessageFamily
            };
        }

        public static MessageReference? CreateReplyReference(ReplyMirroringContext? replyContext, ulong destinationChannelId)
        {
            if (replyContext == null)
            {
                return null;
            }

            var replyTargetMessageId = replyContext.ParentMessageFamily.ResolveReplyTargetMessageId(
                replyContext.RepliedMessageId,
                destinationChannelId);

            return replyTargetMessageId.HasValue
                ? new MessageReference(replyTargetMessageId.Value, destinationChannelId, null, false, default)
                : null;
        }
    }
}
