namespace Tsumari.Bot.Models
{
    public sealed class JumpLinkTarget
    {
        public ulong ChannelId { get; init; }

        public string LanguageLabel { get; init; } = string.Empty;
    }
}
