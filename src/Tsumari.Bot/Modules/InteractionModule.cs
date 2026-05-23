using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Services;

namespace Tsumari.Bot.Modules
{
    [Group("tsumari", "Tsumari Admin and Channel Configuration Commands")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class InteractionModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _dbService;
        private readonly ILogger<InteractionModule> _logger;

        public InteractionModule(DatabaseService dbService, ILogger<InteractionModule> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [SlashCommand("add-master", "Registers an independent Master Channel.")]
        public async Task AddMasterAsync(
            [Summary("channel", "The channel to register as Master")] IChannel channel)
        {
            // Fail-fast validation
            if (channel is not ITextChannel textChannel)
            {
                await RespondAsync("❌ Error: The channel must be a standard Guild Text Channel.", ephemeral: true);
                return;
            }

            try
            {
                // Check if it's already registered as Local
                if (await _dbService.IsLocalizedChannelAsync(channel.Id))
                {
                    await RespondAsync("❌ Error: This channel is already registered as a Localized Channel. Unregister it first.", ephemeral: true);
                    return;
                }

                bool added = await _dbService.AddMasterChannelAsync(channel.Id);
                if (added)
                {
                    _logger.LogInformation("Channel {Name} ({Id}) registered as a Master channel by {User}.", 
                        textChannel.Name, channel.Id, Context.User.Username);
                    await RespondAsync($"✅ Success: Registered <#{channel.Id}> as a Tsumari Master Channel.", ephemeral: false);
                }
                else
                {
                    await RespondAsync($"⚠️ Notice: <#{channel.Id}> is already registered as a Master Channel.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering master channel {Id}.", channel.Id);
                await RespondAsync("❌ An internal database error occurred while registering the master channel.", ephemeral: true);
            }
        }

        [SlashCommand("register-local", "Links a localized channel to a specific parent Master Channel.")]
        public async Task RegisterLocalAsync(
            [Summary("local-channel", "The localized channel to register")] IChannel localChannel,
            [Summary("master-channel", "The parent Master channel")] IChannel masterChannel,
            [Summary("language-code", "DeepL target language code (e.g. en, el, it, es)")] string languageCode)
        {
            // Fail-fast validation
            if (localChannel is not ITextChannel localTextChannel || masterChannel is not ITextChannel masterTextChannel)
            {
                await RespondAsync("❌ Error: Both channels must be standard Guild Text Channels.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(languageCode))
            {
                await RespondAsync("❌ Error: You must specify a target language code.", ephemeral: true);
                return;
            }

            try
            {
                // Ensure the parent is indeed registered as a Master Channel
                if (!await _dbService.IsMasterChannelAsync(masterChannel.Id))
                {
                    await RespondAsync($"❌ Error: <#{masterChannel.Id}> is not registered as a Master Channel. Register it first using `/tsumari add-master`.", ephemeral: true);
                    return;
                }

                // Ensure local channel is not registered as Master channel itself
                if (await _dbService.IsMasterChannelAsync(localChannel.Id))
                {
                    await RespondAsync("❌ Error: The localized channel cannot be a Master Channel.", ephemeral: true);
                    return;
                }

                // If local channel is the same as master channel
                if (localChannel.Id == masterChannel.Id)
                {
                    await RespondAsync("❌ Error: Localized channel and Master channel cannot be the same.", ephemeral: true);
                    return;
                }

                string lang = languageCode.Trim().ToLowerInvariant();

                bool registered = await _dbService.RegisterLocalChannelAsync(localChannel.Id, masterChannel.Id, lang);
                if (registered)
                {
                    _logger.LogInformation("Local channel {Local} registered to Master {Master} with language {Lang} by {User}.", 
                        localTextChannel.Name, masterTextChannel.Name, lang, Context.User.Username);
                    await RespondAsync($"✅ Success: Linked localized channel <#{localChannel.Id}> to Master <#{masterChannel.Id}> with target language **{lang.ToUpper()}**.", ephemeral: false);
                }
                else
                {
                    await RespondAsync("❌ Error: Failed to register localized channel in database.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering localized channel {LocalId} for master {MasterId}.", localChannel.Id, masterChannel.Id);
                await RespondAsync("❌ An internal database error occurred while registering the localized channel.", ephemeral: true);
            }
        }

        [SlashCommand("unregister", "Dynamically unregisters a Master or Localized channel.")]
        public async Task UnregisterAsync(
            [Summary("channel", "The channel to remove from Tsumari configuration")] IChannel channel)
        {
            // Fail-fast validation
            if (channel is not ITextChannel)
            {
                await RespondAsync("❌ Error: The channel must be a standard Guild Text Channel.", ephemeral: true);
                return;
            }

            try
            {
                bool deleted = await _dbService.UnregisterChannelAsync(channel.Id);
                if (deleted)
                {
                    _logger.LogInformation("Unregistered channel {Id} by user {User}.", channel.Id, Context.User.Username);
                    await RespondAsync($"✅ Success: Unregistered <#{channel.Id}> from Tsumari. Sibling or cascading links have been purged.", ephemeral: false);
                }
                else
                {
                    await RespondAsync($"❌ Error: <#{channel.Id}> is not registered in Tsumari's Master or Localized database.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unregistering channel {Id}.", channel.Id);
                await RespondAsync("❌ An internal database error occurred while unregistering the channel.", ephemeral: true);
            }
        }
    }
}
