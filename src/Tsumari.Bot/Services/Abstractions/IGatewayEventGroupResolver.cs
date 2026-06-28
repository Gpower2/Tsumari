namespace Tsumari.Bot.Services.Abstractions
{
    public interface IGatewayEventGroupResolver
    {
        Task<IReadOnlyList<GatewayDispatchItem>> ResolveDispatchesAsync(GatewayIngressEvent gatewayEvent);
    }
}
