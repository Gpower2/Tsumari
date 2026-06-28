using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;

namespace Tsumari.Bot.Services
{
    public interface IDiscordMessageService
    {
        ulong CurrentUserId { get; }

        Task<IMessageChannel?> GetChannelAsync(ulong channelId);

        Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId);

        Task AddReactionAsync(IMessage message, IEmote emote);

        Task RemoveOwnReactionAsync(IMessage message, IEmote emote);

        IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(IMessage message, IEmote emote, int limit);
    }
}
