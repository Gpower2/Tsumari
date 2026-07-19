using System;
using System.Threading.Tasks;
using Discord;

namespace Tsumari.Bot.Services.Abstractions
{
    public interface IMirroredMessageRoutingService
    {
        Task HandleMessageReceivedAsync(IMessage rawMessage);

        Task RouteHistoricalMessageAsync(IUserMessage message, DateTimeOffset originalTimestamp);
    }
}
