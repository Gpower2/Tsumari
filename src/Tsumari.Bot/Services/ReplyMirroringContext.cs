namespace Tsumari.Bot.Services
{
    public sealed class ReplyMirroringContext
    {
        public ulong RepliedMessageId { get; init; }

        public LinkedMessageFamily ParentMessageFamily { get; init; } = new();
    }
}
