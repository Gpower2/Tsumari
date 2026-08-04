namespace Tsumari.Bot.Configuration
{
    /// <summary>
    /// Configurable guardrails for Discord.Net REST operations.
    /// </summary>
    public class DiscordRestOptions
    {
        /// <summary>
        /// Timeout applied to text-only Discord operations such as sending/editing/deleting messages,
        /// fetching messages, and adding or removing reactions.
        /// </summary>
        public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Timeout applied to Discord operations that involve file uploads or downloads, such as
        /// SendFilesAsync and CDN attachment downloads.
        /// </summary>
        public TimeSpan FileUploadTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Maximum number of retry attempts for transient Discord REST failures.
        /// </summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>
        /// Base delay between retry attempts. Each subsequent retry multiplies this value by the attempt number.
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    }
}
