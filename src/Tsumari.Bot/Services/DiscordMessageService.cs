using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Tsumari.Bot.Services
{
    public class DiscordMessageService : IDiscordMessageService
    {
        private readonly DiscordSocketClient _client;

        public DiscordMessageService(DiscordSocketClient client)
        {
            _client = client;
        }

        public ulong CurrentUserId => _client.CurrentUser.Id;

        public async Task<IMessageChannel?> GetChannelAsync(ulong channelId)
        {
            return await _client.GetChannelAsync(channelId) as IMessageChannel;
        }

        public async Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId)
        {
            var channel = await GetChannelAsync(channelId);
            if (channel == null)
            {
                return false;
            }

            await channel.DeleteMessageAsync(messageId);
            return true;
        }

        public Task AddReactionAsync(IMessage message, IEmote emote)
        {
            return message switch
            {
                SocketUserMessage socketMessage => socketMessage.AddReactionAsync(emote),
                RestUserMessage restUserMessage => restUserMessage.AddReactionAsync(emote, null),
                RestMessage restMessage => restMessage.AddReactionAsync(emote, null),
                _ => throw new InvalidOperationException($"Message type '{message.GetType().FullName}' does not support reaction mirroring.")
            };
        }

        public Task RemoveOwnReactionAsync(IMessage message, IEmote emote)
        {
            return message switch
            {
                SocketUserMessage socketMessage => socketMessage.RemoveReactionAsync(emote, CurrentUserId),
                RestUserMessage restUserMessage => restUserMessage.RemoveReactionAsync(emote, CurrentUserId, null),
                RestMessage restMessage => restMessage.RemoveReactionAsync(emote, CurrentUserId, null),
                _ => throw new InvalidOperationException($"Message type '{message.GetType().FullName}' does not support reaction mirroring.")
            };
        }

        public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(IMessage message, IEmote emote, int limit)
        {
            return message switch
            {
                SocketUserMessage socketMessage => socketMessage.GetReactionUsersAsync(emote, limit, null, ReactionType.Normal),
                RestUserMessage restUserMessage => restUserMessage.GetReactionUsersAsync(emote, limit, null, ReactionType.Normal),
                RestMessage restMessage => restMessage.GetReactionUsersAsync(emote, limit, null, ReactionType.Normal),
                _ => throw new InvalidOperationException($"Message type '{message.GetType().FullName}' does not support reaction mirroring.")
            };
        }
    }
}
