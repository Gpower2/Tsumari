namespace Tsumari.Bot.Models
{
    public sealed class ChannelRoutingContext
    {
        public ulong ChannelId { get; init; }

        public bool IsMaster { get; init; }

        public ulong? ParentMasterChannelId { get; init; }

        public string? TargetLanguageCode { get; init; }

        public bool IsLocalized => ParentMasterChannelId.HasValue && !string.IsNullOrWhiteSpace(TargetLanguageCode);

        public ulong? LinkedGroupKey => IsMaster ? ChannelId : ParentMasterChannelId;
    }
}
