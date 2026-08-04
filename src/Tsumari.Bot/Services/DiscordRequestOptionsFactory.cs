using Discord;
using Tsumari.Bot.Configuration;

namespace Tsumari.Bot.Services
{
    /// <summary>
    /// Creates Discord.Net <see cref="RequestOptions"/> instances with timeouts that match the
    /// operation type (text vs. file upload) while disabling Discord.Net's internal infinite retry
    /// loop for timeouts and 502s. Rate-limit handling is still delegated to Discord.Net.
    /// </summary>
    public static class DiscordRequestOptionsFactory
    {
        /// <summary>
        /// Request options for text-only operations such as sending, editing, deleting, or fetching
        /// messages, and for adding or removing reactions.
        /// </summary>
        public static RequestOptions MessageOptions(DiscordRestOptions options)
        {
            return new RequestOptions
            {
                Timeout = (int)options.MessageTimeout.TotalMilliseconds,
                // Discord.Net's default retry mode is AlwaysRetry, which can loop forever on timeouts.
                // We let Discord.Net handle rate limits internally and rely on our own finite retry
                // policy for transient network/server failures and timeouts.
                RetryMode = RetryMode.RetryRatelimit
            };
        }

        /// <summary>
        /// Request options for operations that upload or download files, such as SendFilesAsync and
        /// CDN attachment downloads.
        /// </summary>
        public static RequestOptions FileUploadOptions(DiscordRestOptions options)
        {
            return new RequestOptions
            {
                Timeout = (int)options.FileUploadTimeout.TotalMilliseconds,
                RetryMode = RetryMode.RetryRatelimit
            };
        }
    }
}
