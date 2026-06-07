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

            _client.Log += OnLogAsync;
            _client.Ready += OnReadyAsync;
            _client.MessageReceived += OnMessageReceivedAsync;
            _client.InteractionCreated += OnInteractionCreatedAsync;

            _interactionService.Log += OnLogAsync;

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
                await _dbService.InitializeDatabaseAsync();

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
            if (rawMessage is not SocketUserMessage message) return Task.CompletedTask;
            if (message.Source != MessageSource.User) return Task.CompletedTask;

            bool hasAttachments = message.Attachments != null && message.Attachments.Count > 0;
            if (string.IsNullOrWhiteSpace(message.Content) && !hasAttachments) return Task.CompletedTask;

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

            bool isMaster = await _dbService.IsMasterChannelAsync(channelId);
            bool isLocal = await _dbService.IsLocalizedChannelAsync(channelId);

            if (!isMaster && !isLocal)
            {
                return;
            }

            _logger.LogInformation("Processing message {Id} in channel {ChanName} (Master: {Master}, Local: {Local})", 
                message.Id, message.Channel.Name, isMaster, isLocal);

            if (!_translationService.IsActive)
            {
                _logger.LogWarning("Translation service is currently inactive. Outbound routing aborted.");
                return;
            }

            string content = message.Content ?? string.Empty;
            string detectedLang = "EN";

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
                var localLang = await _dbService.GetTargetLanguageCodeAsync(channelId);
                if (localLang != null) detectedLang = localLang.ToUpperInvariant();
            }

            var mediaAssets = new List<(string Filename, byte[] Bytes)>();
            if (hasAttachments)
            {
                foreach (var attachment in message.Attachments)
                {
                    try
                    {
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

            // Cast to IGuildUser to get the correct Nickname/Display Name
            string authorName = message.Author.Username;
            if (message.Author is IGuildUser guildUser)
            {
                authorName = guildUser.Nickname ?? guildUser.GlobalName ?? guildUser.Username;
            }

            // We hold a mapping of target channel -> dispatched message so we can edit in the buttons
            var sentMessages = new Dictionary<ulong, IUserMessage>();
            var targetsList = new List<(ulong ChannelId, string LanguageLabel, string TargetLangCode)>();

            // ===================================================================================
            // ROUTING BRANCH A: Message received directly in a Master Channel (e.g., #general)
            // ===================================================================================
            if (isMaster)
            {
                var children = await _dbService.GetLocalizedChannelsForMasterAsync(channelId);
                
                // Track master link (original message link target)
                targetsList.Add((channelId, "Original", "master"));

                // 1. Initial sequential send with temporary placeholder button linking to Original message
                var initBuilder = new ComponentBuilder()
                    .WithButton("Original", style: ButtonStyle.Link, url: message.GetJumpUrl());

                foreach (var child in children)
                {
                    var childChannel = await _client.GetChannelAsync(child.ChannelId) as IMessageChannel;
                    if (childChannel == null) continue;

                    string textToSend;
                    if (!string.Equals(detectedLang, child.TargetLanguageCode, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var translatedText = await _translationService.TranslateTextAsync(content, child.TargetLanguageCode);
                            textToSend = $"**{authorName}** ({detectedLang.ToUpperInvariant()} to {child.TargetLanguageCode.ToUpperInvariant()}):\n{translatedText}";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to translate message {MsgId} to {Lang}. Forwarding raw.", message.Id, child.TargetLanguageCode);
                            textToSend = $"**{authorName}**:\n{content} *(Translation Failed)*";
                        }
                    }
                    else
                    {
                        textToSend = $"**{authorName}**:\n{content}";
                    }

                    var sentMsg = await SendMessageWithFilesAsync(childChannel, textToSend, mediaAssets, initBuilder.Build());
                    if (sentMsg != null)
                    {
                        sentMessages.Add(child.ChannelId, sentMsg);
                        await _dbService.LinkMessagesAsync(message.Id, sentMsg.Id, childChannel.Id, child.TargetLanguageCode);
                        targetsList.Add((child.ChannelId, child.TargetLanguageCode.ToUpperInvariant(), child.TargetLanguageCode));
                    }
                }

                // 2. Perform message edits to append language-specific jump buttons for each generated message
                await EditJumpButtonsIntoSentMessagesAsync(message, sentMessages, targetsList);
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
                if (parentChannel == null) return;

                bool isMatch = string.Equals(detectedLang, targetLang, StringComparison.OrdinalIgnoreCase);

                // Initialize target lists
                targetsList.Add((channelId, targetLang.ToUpperInvariant(), targetLang));
                targetsList.Add((parentMasterId.Value, "Original", "master"));

                var initBuilder = new ComponentBuilder()
                    .WithButton("Original", style: ButtonStyle.Link, url: message.GetJumpUrl());

                // --- MATCH FLOW (User typed Greek in #general-greek) ---
                if (isMatch)
                {
                    // 1. Broadcast raw to parent master channel
                    string textToParent = $"**{authorName}**:\n{content}";
                    var parentSent = await SendMessageWithFilesAsync(parentChannel, textToParent, mediaAssets, initBuilder.Build());
                    if (parentSent != null)
                    {
                        sentMessages.Add(parentMasterId.Value, parentSent);
                        await _dbService.LinkMessagesAsync(message.Id, parentSent.Id, parentChannel.Id, "master");
                    }

                    // 2. Fetch sibling channels, translate, and send
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
                                textToSibling = $"**{authorName}** ({detectedLang.ToUpperInvariant()} to {sibling.TargetLanguageCode.ToUpperInvariant()}):\n{translatedText}";
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

                        var siblingSent = await SendMessageWithFilesAsync(siblingChannel, textToSibling, mediaAssets, initBuilder.Build());
                        if (siblingSent != null)
                        {
                            sentMessages.Add(sibling.ChannelId, siblingSent);
                            await _dbService.LinkMessagesAsync(message.Id, siblingSent.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                            targetsList.Add((sibling.ChannelId, sibling.TargetLanguageCode.ToUpperInvariant(), sibling.TargetLanguageCode));
                        }
                    }

                    // Edit in the jump buttons
                    await EditJumpButtonsIntoSentMessagesAsync(message, sentMessages, targetsList);
                }
                // --- MISMATCH FLOW (User typed English in #general-greek) ---
                else
                {
                    // 1. Leave raw in #general-greek, post native reply in Greek
                    IUserMessage? nativeReply = null;
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var nativeTranslation = await _translationService.TranslateTextAsync(content, targetLang);
                            nativeReply = await message.ReplyAsync($"*({detectedLang.ToUpperInvariant()} to {targetLang.ToUpperInvariant()}):* {nativeTranslation}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Mismatch Flow: Failed native translation reply in channel {Id}", channelId);
                        }

                        if (nativeReply != null)
                        {
                            sentMessages.Add(channelId, nativeReply);
                            await _dbService.LinkMessagesAsync(message.Id, nativeReply.Id, channelId, targetLang);
                        }
                    }

                    // 2. Post original raw to master channel
                    string textToParent = $"**{authorName}**:\n{content}";
                    var parentSent = await SendMessageWithFilesAsync(parentChannel, textToParent, mediaAssets, initBuilder.Build());
                    if (parentSent != null)
                    {
                        sentMessages.Add(parentMasterId.Value, parentSent);
                        await _dbService.LinkMessagesAsync(message.Id, parentSent.Id, parentChannel.Id, "master");
                    }

                    // 3. Post original raw to sibling home channel and translate to other siblings
                    var siblings = await _dbService.GetSiblingChannelsAsync(channelId);
                    foreach (var sibling in siblings)
                    {
                        var siblingChannel = await _client.GetChannelAsync(sibling.ChannelId) as IMessageChannel;
                        if (siblingChannel == null) continue;

                        string textToSibling;
                        bool siblingIsHome = string.Equals(detectedLang, sibling.TargetLanguageCode, StringComparison.OrdinalIgnoreCase);

                        if (siblingIsHome)
                        {
                            textToSibling = $"**{authorName}**:\n{content}";
                        }
                        else if (!string.IsNullOrWhiteSpace(content))
                        {
                            try
                            {
                                var translatedText = await _translationService.TranslateTextAsync(content, sibling.TargetLanguageCode);
                                textToSibling = $"**{authorName}** ({detectedLang.ToUpperInvariant()} to {sibling.TargetLanguageCode.ToUpperInvariant()}):\n{translatedText}";
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

                        var siblingSent = await SendMessageWithFilesAsync(siblingChannel, textToSibling, mediaAssets, initBuilder.Build());
                        if (siblingSent != null)
                        {
                            sentMessages.Add(sibling.ChannelId, siblingSent);
                            await _dbService.LinkMessagesAsync(message.Id, siblingSent.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                            targetsList.Add((sibling.ChannelId, sibling.TargetLanguageCode.ToUpperInvariant(), sibling.TargetLanguageCode));
                        }
                    }

                    // Edit in the jump buttons
                    await EditJumpButtonsIntoSentMessagesAsync(message, sentMessages, targetsList);
                }
            }
        }

        private async Task EditJumpButtonsIntoSentMessagesAsync(
            SocketUserMessage originalMessage,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<(ulong ChannelId, string LanguageLabel, string TargetLangCode)> targets)
        {
            // Build the dynamic jump link button layout
            var finalBuilder = new ComponentBuilder();

            // Original post jump link
            finalBuilder.WithButton("Original", style: ButtonStyle.Link, url: originalMessage.GetJumpUrl());

            foreach (var target in targets)
            {
                // Skip if it points to the starting master channel (original button covers that link)
                if (target.LanguageLabel == "Original") continue;

                if (sentMessages.TryGetValue(target.ChannelId, out var sentMsg))
                {
                    finalBuilder.WithButton(target.LanguageLabel, style: ButtonStyle.Link, url: sentMsg.GetJumpUrl());
                }
            }

            var finalComponents = finalBuilder.Build();

            // Asynchronously edit each sent copy message to add the jump buttons
            foreach (var item in sentMessages)
            {
                try
                {
                    await item.Value.ModifyAsync(properties => properties.Components = finalComponents);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to edit buttons into sent message {MsgId} in channel {ChanId}.", item.Value.Id, item.Key);
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
                    var ms = new MemoryStream(asset.Bytes);
                    streams.Add(ms);
                    fileAttachments.Add(new FileAttachment(ms, asset.Filename));
                }

                return await channel.SendFilesAsync(fileAttachments, text, components: components);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send files/message to channel {Id}. Fallback to text.", channel.Id);
                try
                {
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

