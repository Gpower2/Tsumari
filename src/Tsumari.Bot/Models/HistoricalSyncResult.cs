namespace Tsumari.Bot.Models
{
    public sealed class HistoricalSyncResult
    {
        public bool Success { get; init; }

        public string? ErrorMessage { get; init; }

        public int ProcessedCount { get; init; }

        public int FailedCount { get; init; }

        public int SkippedCount { get; init; }

        public static HistoricalSyncResult Failure(string errorMessage)
        {
            return new HistoricalSyncResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
