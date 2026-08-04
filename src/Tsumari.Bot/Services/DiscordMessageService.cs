using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using Tsumari.Bot.Configuration;

namespace Tsumari.Bot.Services
{
    public class DiscordMessageService : IDiscordMessageService
    {
        private readonly DiscordSocketClient _client;
        private readonly IDiscordRestRetryPolicy _retryPolicy;
        private readonly DiscordRestOptions _options;

        public DiscordMessageService(DiscordSocketClient client, IDiscordRestRetryPolicy retryPolicy, IOptions<DiscordRestOptions> options)
        {
            _client = client;
            _retryPolicy = retryPolicy;
            _options = options.Value;
        }

        public ulong CurrentUserId => _client.CurrentUser.Id;

        public async Task<IMessageChannel?> GetChannelAsync(ulong channelId)
        {
            var options = DiscordRequestOptionsFactory.MessageOptions(_options);
            var channel = await _retryPolicy.ExecuteAsync(
                async () => await _client.GetChannelAsync(channelId, options),
                $"get channel {channelId}");
            return channel as IMessageChannel;
        }

        public async Task<IUserMessage?> SendMessageAsync(ulong channelId, string message)
        {
            var channel = await GetChannelAsync(channelId);
            if (channel == null)
            {
                return null;
            }

            var options = DiscordRequestOptionsFactory.MessageOptions(_options);
            return await _retryPolicy.ExecuteAsync(
                () => channel.SendMessageAsync(message, options: options),
                $"send message to channel {channelId}");
        }

        public async Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId)
        {
            var channel = await GetChannelAsync(channelId);
            if (channel == null)
            {
                return false;
            }

            var options = DiscordRequestOptionsFactory.MessageOptions(_options);
            await _retryPolicy.ExecuteAsync(
                () => channel.DeleteMessageAsync(messageId, options),
                $"delete message {messageId} from channel {channelId}");
            return true;
        }

        public Task AddReactionAsync(IMessage message, IEmote emote)
        {
            var options = DiscordRequestOptionsFactory.MessageOptions(_options);
            return _retryPolicy.ExecuteAsync(
                () => message switch
                {
                    SocketUserMessage socketMessage => socketMessage.AddReactionAsync(emote, options),
                    RestUserMessage restUserMessage => restUserMessage.AddReactionAsync(emote, options),
                    RestMessage restMessage => restMessage.AddReactionAsync(emote, options),
                    _ => throw new InvalidOperationException($"Message type '{message.GetType().FullName}' does not support reaction mirroring.")
                },
                $"add reaction to message {message.Id}");
        }

        public Task RemoveOwnReactionAsync(IMessage message, IEmote emote)
        {
            var options = DiscordRequestOptionsFactory.MessageOptions(_options);
            return _retryPolicy.ExecuteAsync(
                () => message switch
                {
                    SocketUserMessage socketMessage => socketMessage.RemoveReactionAsync(emote, CurrentUserId, options),
                    RestUserMessage restUserMessage => restUserMessage.RemoveReactionAsync(emote, CurrentUserId, options),
                    RestMessage restMessage => restMessage.RemoveReactionAsync(emote, CurrentUserId, options),
                    _ => throw new InvalidOperationException($"Message type '{message.GetType().FullName}' does not support reaction mirroring.")
                },
                $"remove reaction from message {message.Id}");
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

        public async Task<IMessage?> GetMessageAsync(ulong channelId, ulong messageId)
        {
            var channel = await GetChannelAsync(channelId);
            if (channel == null)
            {
                return null;
            }

            var options = DiscordRequestOptionsFactory.MessageOptions(_options);
            return await _retryPolicy.ExecuteAsync(
                () => channel.GetMessageAsync(messageId, options: options),
                $"get message {messageId} from channel {channelId}");
        }

        public async Task<DateTimeOffset?> GetMessageTimestampAsync(ulong channelId, ulong messageId)
        {
            var message = await GetMessageAsync(channelId, messageId);
            return message?.Timestamp;
        }
    }
}
