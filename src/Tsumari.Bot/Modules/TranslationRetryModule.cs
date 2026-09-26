using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Modules;

// Separate ungrouped module: component IDs must not acquire the /tsumari command prefix.
public sealed class TranslationRetryModule(TranslationRetryService retry, ILogger<TranslationRetryModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction(TranslationRetryRules.ButtonId, ignoreGroupNames: true)]
    public async Task RetryAsync()
    {
        if (Context.Guild == null || Context.User is not IGuildUser user
            || Context.Interaction is not Discord.WebSocket.SocketMessageComponent component)
        {
            await RespondAsync("Retry is only available in a server.", ephemeral: true);
            return;
        }
        await DeferAsync(ephemeral: true);
        try
        {
            var result = await retry.RetryAsync(Context.Guild.Id, component.ChannelId!.Value, component.Message.Id, user);
            await FollowupAsync(result.Message, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not process retry button");
            await FollowupAsync("Retry could not be processed. Please try again later.", ephemeral: true);
        }
    }
}
