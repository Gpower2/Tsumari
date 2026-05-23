using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Services;

namespace Tsumari.Bot
{
    public class Worker : BackgroundService
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactionService;
        private readonly DatabaseService _dbService;
        private readonly TranslationService _translationService;
        private readonly HttpClient _httpClient;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<Worker> _logger;

        public Worker(
            DiscordSocketClient client,
            InteractionService interactionService,
            DatabaseService dbService,
            TranslationService translationService,
            HttpClient httpClient,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<Worker> logger)
        {
            _client = client;
            _interactionService = interactionService;
            _dbService = dbService;
            _translationService = translationService;
            _httpClient = httpClient;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Tsumari Discord Translation Bot Worker Service...");

            // Hook Gateway events
            _client.Log += OnLogAsync;
            _client.Ready += OnReadyAsync;
            _client.MessageReceived += OnMessageReceivedAsync;
            _client.InteractionCreated += OnInteractionCreatedAsync;

            // Hook Interaction Service events
            _interactionService.Log += OnLogAsync;

            // Retrieve token from config slots
            var token = _configuration["Discord:Token"] ?? _configuration["DiscordToken"];
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogCritical("Discord Token is completely missing from configuration! Shutting down worker.");
                throw new InvalidOperationException("Discord Token is required.");
            }

            try
            {
                await _client.LoginAsync(TokenType.Bot, token);
                await _client.StartAsync();

                // Keep service alive until cancellation is requested
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Tsumari Worker Service cancellation requested.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "A fatal exception crashed the Gateway Client lifecycle.");
                throw;
            }
            finally
            {
                _logger.LogInformation("Disconnecting and disposing Discord client connection...");
                await _client.LogoutAsync();
                await _client.StopAsync();
            }
        }

        private Task OnLogAsync(LogMessage log)
        {
            var level = log.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Debug,
                LogSeverity.Debug => LogLevel.Trace,
                _ => LogLevel.Information
            };

            _logger.Log(level, log.Exception, "[Discord.Net] {Source}: {Message}", log.Source, log.Message);
            return Task.CompletedTask;
        }

        private async Task OnReadyAsync()
        {
            _logger.LogInformation("Tsumari is connected to Discord Gateway as: {User}", _client.CurrentUser.ToString());

            try
            {
                // 1. Initialize SQLite connection and tables
                await _dbService.InitializeDatabaseAsync();

                // 2. Add Modules and Register commands globally
                await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);
                await _interactionService.RegisterCommandsGloballyAsync();
                
                _logger.LogInformation("Administrative slash commands registered globally.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during worker initialization on Ready event.");
            }
        }

        private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
        {
            var context = new SocketInteractionContext(_client, interaction);
            var result = await _interactionService.ExecuteCommandAsync(context, _serviceProvider);

            if (!result.IsSuccess)
            {
                _logger.LogError("Slash command execution failed: {Reason}", result.ErrorReason);
                
                if (interaction.HasResponded)
                {
                    await interaction.FollowupAsync($"❌ Command error: {result.ErrorReason}", ephemeral: true);
                }
                else
                {
                    await interaction.RespondAsync($"❌ Command error: {result.ErrorReason}", ephemeral: true);
                }
            }
        }

        private Task OnMessageReceivedAsync(SocketMessage rawMessage)
        {
            // Ignore system notifications, empty contents, or input posts generated by other bots
            if (rawMessage is not SocketUserMessage message) return Task.CompletedTask;
            if (message.Source != MessageSource.User) return Task.CompletedTask;

            bool hasAttachments = message.Attachments != null && message.Attachments.Count > 0;
            if (string.IsNullOrWhiteSpace(message.Content) && !hasAttachments) return Task.CompletedTask;

            // Execute the routing pipeline asynchronously inside a non-blocking background Thread/Task structure
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessMessageRoutingPipelineAsync(message, hasAttachments);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error inside routing pipeline for message {MsgId}.", message.Id);
                }
            });

            return Task.CompletedTask;
        }

        private async Task ProcessMessageRoutingPipelineAsync(SocketUserMessage message, bool hasAttachments)
        {
            ulong channelId = message.Channel.Id;

            // Step 1: Context validation - does it belong to a cluster?
            bool isMaster = await _dbService.IsMasterChannelAsync(channelId);
            bool isLocal = await _dbService.IsLocalizedChannelAsync(channelId);

            if (!isMaster && !isLocal)
            {
                // Skip execution entirely if it doesn't belong to any cluster
                return;
            }

            _logger.LogInformation("Processing message {Id} in channel {ChanName} (Master: {Master}, Local: {Local})", 
                message.Id, message.Channel.Name, isMaster, isLocal);

            // Step 2: Ensure translation engine is ready
            if (!_translationService.IsActive)
            {
                _logger.LogWarning("Translation service is currently inactive. Outbound routing aborted.");
                return;
            }

            // Step 3: Automatically detect raw text language code (unless content is empty)
            string content = message.Content ?? string.Empty;
            string detectedLang = "EN"; // fallback default

            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    detectedLang = await _translationService.DetectLanguageAsync(content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to run language detection. Fallback to EN.");
                }
            }
            else if (isLocal)
            {
                // If it's a file-only upload inside a localized channel, assume the channel's target language
                var localLang = await _dbService.GetTargetLanguageCodeAsync(channelId);
                if (localLang != null) detectedLang = localLang.ToUpperInvariant();
            }

            // Step 4: EXPIRING CDN MEDIA RE-UPLOAD LAYER
            var mediaAssets = new List<(string Filename, byte[] Bytes)>();
            if (hasAttachments)
            {
                foreach (var attachment in message.Attachments)
                {
                    try
                    {
                        // Instantly download physical data bytes down from the source URL into memory
                        var bytes = await _httpClient.GetByteArrayAsync(attachment.Url);
                        mediaAssets.Add((attachment.Filename, bytes));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "CDN re-upload fail: Could not download {File} from {Url}", 
                            attachment.Filename, attachment.Url);
                    }
                }
            }

            // Step 5: Create view context link button
            var components = new ComponentBuilder()
                .WithButton("View Context", style: ButtonStyle.Link, url: message.GetJumpUrl())
                .Build();

            // Establish author name
            string authorName = message.Author.GlobalName ?? message.Author.Username;

            // ===================================================================================
            // ROUTING BRANCH A: Message received directly in a Master Channel (e.g., #general)
            // ===================================================================================
            if (isMaster)
            {
                // Leave the original message untouched in #general.
                // Query child localized channels
                var children = await _dbService.GetLocalizedChannelsForMasterAsync(channelId);

                foreach (var child in children)
                {
                    var childChannel = await _client.GetChannelAsync(child.ChannelId) as IMessageChannel;
                    if (childChannel == null) continue;

                    string textToSend;
                    
                    // If detected language does not match target language, translate via DeepL. Otherwise, forward raw.
                    if (!string.Equals(detectedLang, child.TargetLanguageCode, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var translatedText = await _translationService.TranslateTextAsync(content, child.TargetLanguageCode);
                            textToSend = $"**{authorName}** (Translated to {child.TargetLanguageCode.ToUpperInvariant()}):\n{translatedText}";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to translate message {MsgId} to {Lang}. Forwarding raw content.", message.Id, child.TargetLanguageCode);
                            textToSend = $"**{authorName}**:\n{content} *(Translation Failed)*";
                        }
                    }
                    else
                    {
                        textToSend = $"**{authorName}**:\n{content}";
                    }

                    var sentMsg = await SendMessageWithFilesAsync(childChannel, textToSend, mediaAssets, components);
                    if (sentMsg != null)
                    {
                        await _dbService.LinkMessagesAsync(message.Id, sentMsg.Id, childChannel.Id, child.TargetLanguageCode);
                    }
                }
            }
            // ===================================================================================
            // ROUTING BRANCH B: Message received in a Localized Channel (e.g., #general-greek)
            // ===================================================================================
            else if (isLocal)
            {
                ulong? parentMasterId = await _dbService.GetParentMasterChannelIdAsync(channelId);
                string? targetLang = await _dbService.GetTargetLanguageCodeAsync(channelId);

                if (parentMasterId == null || string.IsNullOrWhiteSpace(targetLang))
                {
                    _logger.LogError("Channel {Id} has incomplete configuration in localized table.", channelId);
                    return;
                }

                var parentChannel = await _client.GetChannelAsync(parentMasterId.Value) as IMessageChannel;
                if (parentChannel == null)
                {
                    _logger.LogWarning("Master parent channel {Id} is not accessible to the bot.", parentMasterId.Value);
                    return;
                }

                bool isMatch = string.Equals(detectedLang, targetLang, StringComparison.OrdinalIgnoreCase);

                // --- MATCH FLOW (User typed Greek in #general-greek) ---
                if (isMatch)
                {
                    // 1. Broadcast the original, untouched text payload directly to parent master channel
                    string textToParent = $"**{authorName}**:\n{content}";
                    var parentSent = await SendMessageWithFilesAsync(parentChannel, textToParent, mediaAssets, components);
                    if (parentSent != null)
                    {
                        await _dbService.LinkMessagesAsync(message.Id, parentSent.Id, parentChannel.Id, "master");
                    }

                    // 2. Fetch sibling channels, translate via DeepL and disperse
                    var siblings = await _dbService.GetSiblingChannelsAsync(channelId);
                    foreach (var sibling in siblings)
                    {
                        var siblingChannel = await _client.GetChannelAsync(sibling.ChannelId) as IMessageChannel;
                        if (siblingChannel == null) continue;

                        string textToSibling;
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            try
                            {
                                var translatedText = await _translationService.TranslateTextAsync(content, sibling.TargetLanguageCode);
                                textToSibling = $"**{authorName}** (Translated to {sibling.TargetLanguageCode.ToUpperInvariant()}):\n{translatedText}";
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Match Flow: Failed to translate to sibling {Lang}.", sibling.TargetLanguageCode);
                                textToSibling = $"**{authorName}**:\n{content} *(Translation Failed)*";
                            }
                        }
                        else
                        {
                            textToSibling = $"**{authorName}**:";
                        }

                        var siblingSent = await SendMessageWithFilesAsync(siblingChannel, textToSibling, mediaAssets, components);
                        if (siblingSent != null)
                        {
                            await _dbService.LinkMessagesAsync(message.Id, siblingSent.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                        }
                    }
                }
                // --- MISMATCH FLOW (User typed English in #general-greek) ---
                else
                {
                    // 1. Do NOT delete user's message. Leave raw text in #general-greek.
                    // Translate the text into Greek (target language of #general-greek) and post as inline reply inside it
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var nativeTranslation = await _translationService.TranslateTextAsync(content, targetLang);
                            await message.ReplyAsync($"*Translation ({targetLang.ToUpperInvariant()}):* {nativeTranslation}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Mismatch Flow: Failed to post native follow-up translation inside channel {Id}", channelId);
                        }
                    }

                    // 2. Post original raw text as-is to parent Master channel
                    string textToParent = $"**{authorName}**:\n{content}";
                    var parentSent = await SendMessageWithFilesAsync(parentChannel, textToParent, mediaAssets, components);
                    if (parentSent != null)
                    {
                        await _dbService.LinkMessagesAsync(message.Id, parentSent.Id, parentChannel.Id, "master");
                    }

                    // 3. Post original raw text as-is to its proper sibling home channel and translate to other remaining siblings
                    var siblings = await _dbService.GetSiblingChannelsAsync(channelId);
                    foreach (var sibling in siblings)
                    {
                        var siblingChannel = await _client.GetChannelAsync(sibling.ChannelId) as IMessageChannel;
                        if (siblingChannel == null) continue;

                        string textToSibling;
                        bool siblingIsHome = string.Equals(detectedLang, sibling.TargetLanguageCode, StringComparison.OrdinalIgnoreCase);

                        if (siblingIsHome)
                        {
                            // Post as-is (Original raw text) to proper sibling home
                            textToSibling = $"**{authorName}**:\n{content}";
                        }
                        else if (!string.IsNullOrWhiteSpace(content))
                        {
                            // Translate to all other remaining sibling channels
                            try
                            {
                                var translatedText = await _translationService.TranslateTextAsync(content, sibling.TargetLanguageCode);
                                textToSibling = $"**{authorName}** (Translated to {sibling.TargetLanguageCode.ToUpperInvariant()}):\n{translatedText}";
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Mismatch Flow: Failed sibling translation to {Lang}", sibling.TargetLanguageCode);
                                textToSibling = $"**{authorName}**:\n{content} *(Translation Failed)*";
                            }
                        }
                        else
                        {
                            textToSibling = $"**{authorName}**:";
                        }

                        var siblingSent = await SendMessageWithFilesAsync(siblingChannel, textToSibling, mediaAssets, components);
                        if (siblingSent != null)
                        {
                            await _dbService.LinkMessagesAsync(message.Id, siblingSent.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                        }
                    }
                }
            }
        }

        private async Task<IUserMessage?> SendMessageWithFilesAsync(
            IMessageChannel channel,
            string text,
            List<(string Filename, byte[] Bytes)> mediaAssets,
            MessageComponent components)
        {
            if (channel == null) return null;

            var fileAttachments = new List<FileAttachment>();
            var streams = new List<MemoryStream>();

            try
            {
                foreach (var asset in mediaAssets)
                {
                    // Create isolated streams per dispatch action
                    var ms = new MemoryStream(asset.Bytes);
                    streams.Add(ms);
                    fileAttachments.Add(new FileAttachment(ms, asset.Filename));
                }

                // Send files physically as fresh native assets to this distribution room
                return await channel.SendFilesAsync(fileAttachments, text, components: components);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send files/message to channel {Id}. Fallback to text.", channel.Id);
                try
                {
                    // Fallback to text send so routing pipelines don't fully break down on re-upload failure
                    return await channel.SendMessageAsync(text + "\n*(Media attachments failed to mirror)*", components: components);
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Fallback message transmission failed completely for channel {Id}", channel.Id);
                    return null;
                }
            }
            finally
            {
                // CRITICAL: Cleanly close and run .Dispose() routines over all memory streams immediately after execution
                // to block RAM memory leak scaling inside the HidenCloud server container
                foreach (var stream in streams)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to dispose MemoryStream in mirroring routine.");
                    }
                }
            }
        }
    }
}
