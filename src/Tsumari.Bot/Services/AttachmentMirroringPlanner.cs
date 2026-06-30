using Discord;

namespace Tsumari.Bot.Services
{
    public static class AttachmentMirroringPlanner
    {
        public static AttachmentMirroringPlan CreatePlan(IUserMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (message.Attachments == null || message.Attachments.Count == 0)
            {
                return AttachmentMirroringPlan.Empty;
            }

            if (message.Channel is not IGuildChannel guildChannel || guildChannel.Guild.MaxUploadLimit == 0)
            {
                return new AttachmentMirroringPlan(message.Attachments, []);
            }

            var mirrorableAttachments = new List<IAttachment>(message.Attachments.Count);
            var oversizedFilenames = new List<string>();

            foreach (var attachment in message.Attachments)
            {
                if ((ulong)attachment.Size > guildChannel.Guild.MaxUploadLimit)
                {
                    oversizedFilenames.Add(attachment.Filename);
                    continue;
                }

                mirrorableAttachments.Add(attachment);
            }

            return new AttachmentMirroringPlan(mirrorableAttachments, oversizedFilenames);
        }

        public readonly record struct AttachmentMirroringPlan(
            IReadOnlyCollection<IAttachment> AttachmentsToDownload,
            IReadOnlyCollection<string> OversizedFilenames)
        {
            public static AttachmentMirroringPlan Empty { get; } = new([], []);

            public bool HasOversizedAttachments => OversizedFilenames.Count > 0;
        }
    }
}
