namespace Tsumari.Bot.Models
{
    public class StartupSyncResult
    {
        public int ProcessedCount { get; set; }

        public int FailedCount { get; set; }

        public int SkippedCount { get; set; }

        public int ChannelsChecked { get; set; }

        public int ChannelsSynced { get; set; }
    }
}
