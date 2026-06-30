namespace Tsumari.Bot.Models
{
    public sealed record DatabaseStatusSnapshot(
        string DatabaseFilePath,
        long DatabaseFileSizeBytes,
        long DatabaseWalFileSizeBytes,
        DateTime? DatabaseLastActivityUtc,
        long MasterChannelCount,
        long LocalizedChannelCount,
        long ConfiguredChannelCount,
        long LinkedMessageFamilyCount,
        long LinkedBotMessageCount,
        long LocalizedMessageLinkCount,
        long CurrentMonthCharacterCount)
    {
        public long DatabaseStorageSizeBytes => DatabaseFileSizeBytes + DatabaseWalFileSizeBytes;
    }
}
