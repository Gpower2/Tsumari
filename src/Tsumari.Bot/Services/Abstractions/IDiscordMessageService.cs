using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;

namespace Tsumari.Bot.Services.Abstractions
{
    public interface IDiscordMessageService
    {
        ulong CurrentUserId { get; }

        Task<IMessageChannel?> GetChannelAsync(ulong channelId);

        Task<IUserMessage?> SendMessageAsync(ulong channelId, string message);

        Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId);

        Task AddReactionAsync(IMessage message, IEmote emote);

        Task RemoveOwnReactionAsync(IMessage message, IEmote emote);

        IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(IMessage message, IEmote emote, int limit);

        /// <summary>
        /// Gets the timestamp of a specific message by querying Discord.
        /// </summary>
        /// <param name="channelId">The channel id of the message.</param>
        /// <param name="messageId">The id of the message.</param>
        /// <returns>The message timestamp, or null if the message could not be found.</returns>
        Task<DateTimeOffset?> GetMessageTimestampAsync(ulong channelId, ulong messageId);
    }
}
