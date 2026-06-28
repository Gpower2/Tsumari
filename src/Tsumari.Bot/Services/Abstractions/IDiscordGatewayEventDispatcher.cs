namespace Tsumari.Bot.Services.Abstractions
{
    public interface IDiscordGatewayEventDispatcher
    {
        bool TryEnqueue(GatewayIngressEvent gatewayEvent);
    }
}
