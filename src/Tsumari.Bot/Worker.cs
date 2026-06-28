using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly ReactionMirroringService _reactionMirroringService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<Worker> _logger;

        public Worker(
            DiscordSocketClient client,
            InteractionService interactionService,
            DatabaseService dbService,
            TranslationService translationService,
            ReactionMirroringService reactionMirroringService,
            IHttpClientFactory httpClientFactory,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<Worker> logger)
        {
            _client = client;
            _interactionService = interactionService;
            _dbService = dbService;
            _translationService = translationService;
            _reactionMirroringService = reactionMirroringService;
            _httpClientFactory = httpClientFactory;
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
            _client.MessageUpdated += OnMessageUpdatedAsync;
            _client.ReactionAdded += OnReactionAddedAsync;
            _client.ReactionRemoved += OnReactionRemovedAsync;
            _client.ReactionsCleared += OnReactionsClearedAsync;
            _client.ReactionsRemovedForEmote += OnReactionsRemovedForEmoteAsync;
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

        private Task OnMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after, ISocketMessageChannel channel)
        {
            // Only process user edits (ignore bot edits)
            if (after is not SocketUserMessage message) return Task.CompletedTask;
            if (message.Author.IsBot) return Task.CompletedTask;
            if (message.Source != MessageSource.User) return Task.CompletedTask;

            _ = Task.Run(async () =>
            {
                try
                {
                    // If the old message is not cached, downloading it here returns the already-edited
                    // state from Discord. In that case we must still process the update instead of
                    // comparing "after" against another copy of "after" and skipping the edit.
                    var hadCachedSnapshot = beforeCache.HasValue;
                    var beforeMsg = hadCachedSnapshot
                        ? await beforeCache.GetOrDownloadAsync()
                        : null;
                    var beforeContent = beforeMsg?.Content ?? string.Empty;
                    var afterContent = message.Content ?? string.Empty;

                    if (!ShouldProcessEditedMessage(hadCachedSnapshot, beforeContent, afterContent)) return;

                    await ProcessEditedMessageAsync(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error handling edited message {MsgId}.", message.Id);
                }
            });

            return Task.CompletedTask;
        }

        private Task OnReactionAddedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _reactionMirroringService.MirrorReactionAddedAsync(
                        reaction.MessageId,
                        reaction.Channel.Id,
                        reaction.Emote,
                        reaction.ReactionType,
                        reaction.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error mirroring added reaction for message {MessageId}.", reaction.MessageId);
                }
            });

            return Task.CompletedTask;
        }

        private Task OnReactionRemovedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _reactionMirroringService.MirrorReactionRemovedAsync(
                        reaction.MessageId,
                        reaction.Channel.Id,
                        reaction.Emote,
                        reaction.ReactionType,
                        reaction.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error mirroring removed reaction for message {MessageId}.", reaction.MessageId);
                }
            });

            return Task.CompletedTask;
        }

        private Task OnReactionsClearedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _reactionMirroringService.MirrorReactionsClearedAsync(messageCache.Id, channelCache.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error mirroring cleared reactions for message {MessageId}.", messageCache.Id);
                }
            });

            return Task.CompletedTask;
        }

        private Task OnReactionsRemovedForEmoteAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, IEmote emote)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _reactionMirroringService.MirrorReactionsRemovedForEmoteAsync(messageCache.Id, channelCache.Id, emote);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error mirroring removed-for-emote reactions for message {MessageId}.", messageCache.Id);
                }
            });

            return Task.CompletedTask;
        }

        private async Task ProcessEditedMessageAsync(SocketUserMessage message)
        {
            try
            {
                await _dbService.EnsureOriginalChannelIdAsync(message.Id, message.Channel.Id);
                var mirrored = await _dbService.GetMirroredMessagesAsync(message.Id);
                if (mirrored.Count == 0)
                {
                    return;
                }

                var afterContent = message.Content ?? string.Empty;
                string detectedLang = "EN";
                if (!string.IsNullOrWhiteSpace(afterContent))
                {
                    try
                    {
                        detectedLang = await _translationService.DetectLanguageAsync(afterContent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to detect language for edited message {MsgId}.", message.Id);
                    }
                }

                // Determine author name for header
                string authorName = message.Author.Username;
                if (message.Author is IGuildUser guildUser)
                {
                    authorName = guildUser.Nickname ?? guildUser.GlobalName ?? guildUser.Username;
                }
                var sourceChannelLang = await _dbService.GetTargetLanguageCodeAsync(message.Channel.Id);
                var sourceLang = LanguageCodeService.ResolveSourceLanguageCode(detectedLang, sourceChannelLang);

                foreach (var link in mirrored)
                {
                    var mirroredId = link.MirroredMessageId;
                    var chanId = link.ChannelId;

                    try
                    {
                        var ch = await _client.GetChannelAsync(chanId) as IMessageChannel;
                        if (ch == null) continue;

                        var configuredTargetLang = await _dbService.GetTargetLanguageCodeAsync(chanId);
                        var targetLang = ResolveLinkedMessageTargetLanguageCode(link.LanguageCode, configuredTargetLang);
                        string newText;

                        if (!ShouldTranslateLinkedMessage(sourceLang, targetLang) || string.IsNullOrWhiteSpace(afterContent))
                        {
                            newText = FormatEditedLinkedMessageText(
                                message.Channel.Id,
                                chanId,
                                authorName,
                                sourceLang,
                                targetLang,
                                afterContent);
                        }
                        else
                        {
                            var translationTargetLang = targetLang!;
                            try
                            {
                                var translated = await _translationService.TranslateTextAsync(afterContent, translationTargetLang);
                                newText = FormatEditedLinkedMessageText(
                                    message.Channel.Id,
                                    chanId,
                                    authorName,
                                    sourceLang,
                                    translationTargetLang,
                                    translated);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to retranslate edited message {MsgId} to {Lang}.", message.Id, translationTargetLang);
                                newText = FormatTranslationFailureText(
                                    message.Channel.Id,
                                    chanId,
                                    authorName,
                                    sourceLang,
                                    translationTargetLang,
                                    afterContent);
                            }
                        }

                        var modified = await TryModifyMessageContentAsync(ch, mirroredId, newText);
                        if (!modified)
                        {
                            _logger.LogWarning("Could not fetch mirrored IUserMessage {MirrorId} in channel {ChanId} for edited message.", mirroredId, chanId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while updating mirrored message {MirrorId} for edited original {MsgId}.", mirroredId, message.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled failure while processing edited message {MsgId}.", message.Id);
            }
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
                if (localLang != null) detectedLang = LanguageCodeService.NormalizeLanguageCode(localLang);
            }

            string sourceLang = LanguageCodeService.NormalizeLanguageCode(detectedLang);

            var mediaAssets = new List<(string Filename, byte[] Bytes)>();
            if (hasAttachments)
            {
                using var discordCdnClient = _httpClientFactory.CreateClient(HttpClientNames.DiscordCdn);
                foreach (var attachment in message.Attachments)
                {
                    try
                    {
                        using var response = await discordCdnClient.GetAsync(attachment.Url, HttpCompletionOption.ResponseHeadersRead);
                        var bytes = await response.ReadBytesWithStatusCheckAsync(
                            _logger,
                            $"downloading Discord attachment '{attachment.Filename}'");
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
                    if (!LanguageCodeService.AreSameLanguageCode(sourceLang, child.TargetLanguageCode) && !string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var translatedText = await _translationService.TranslateTextAsync(content, child.TargetLanguageCode);
                            textToSend = $"**{authorName}** {FormatLanguagePair(sourceLang, child.TargetLanguageCode)}:\n{translatedText}";
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
                        await _dbService.LinkMessagesAsync(message.Id, channelId, sentMsg.Id, childChannel.Id, child.TargetLanguageCode);
                        targetsList.Add((child.ChannelId, LanguageCodeService.NormalizeLanguageCode(child.TargetLanguageCode), child.TargetLanguageCode));
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

                sourceLang = LanguageCodeService.ResolveSourceLanguageCode(detectedLang, targetLang);
                bool isMatch = LanguageCodeService.AreSameLanguageCode(sourceLang, targetLang);

                // Initialize target lists
                targetsList.Add((channelId, LanguageCodeService.NormalizeLanguageCode(targetLang), targetLang));
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
                        await _dbService.LinkMessagesAsync(message.Id, channelId, parentSent.Id, parentChannel.Id, "master");
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
                                textToSibling = $"**{authorName}** {FormatLanguagePair(sourceLang, sibling.TargetLanguageCode)}:\n{translatedText}";
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
                            await _dbService.LinkMessagesAsync(message.Id, channelId, siblingSent.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                            targetsList.Add((sibling.ChannelId, LanguageCodeService.NormalizeLanguageCode(sibling.TargetLanguageCode), sibling.TargetLanguageCode));
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
                            nativeReply = await message.ReplyAsync(FormatTranslatedReplyText(sourceLang, targetLang, nativeTranslation));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Mismatch Flow: Failed native translation reply in channel {Id}", channelId);
                        }

                        if (nativeReply != null)
                        {
                            sentMessages.Add(channelId, nativeReply);
                            await _dbService.LinkMessagesAsync(message.Id, channelId, nativeReply.Id, channelId, targetLang);
                        }
                    }

                    // 2. Post original raw to master channel
                    string textToParent = $"**{authorName}**:\n{content}";
                    var parentSent = await SendMessageWithFilesAsync(parentChannel, textToParent, mediaAssets, initBuilder.Build());
                    if (parentSent != null)
                    {
                        sentMessages.Add(parentMasterId.Value, parentSent);
                        await _dbService.LinkMessagesAsync(message.Id, channelId, parentSent.Id, parentChannel.Id, "master");
                    }

                    // 3. Post original raw to sibling home channel and translate to other siblings
                    var siblings = await _dbService.GetSiblingChannelsAsync(channelId);
                    foreach (var sibling in siblings)
                    {
                        var siblingChannel = await _client.GetChannelAsync(sibling.ChannelId) as IMessageChannel;
                        if (siblingChannel == null) continue;

                        string textToSibling;
                        bool siblingIsHome = LanguageCodeService.AreSameLanguageCode(sourceLang, sibling.TargetLanguageCode);

                        if (siblingIsHome)
                        {
                            textToSibling = $"**{authorName}**:\n{content}";
                        }
                        else if (!string.IsNullOrWhiteSpace(content))
                        {
                            try
                            {
                                var translatedText = await _translationService.TranslateTextAsync(content, sibling.TargetLanguageCode);
                                textToSibling = $"**{authorName}** {FormatLanguagePair(sourceLang, sibling.TargetLanguageCode)}:\n{translatedText}";
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
                            await _dbService.LinkMessagesAsync(message.Id, channelId, siblingSent.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                            targetsList.Add((sibling.ChannelId, LanguageCodeService.NormalizeLanguageCode(sibling.TargetLanguageCode), sibling.TargetLanguageCode));
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

        public static string FormatLanguagePair(string sourceLang, string targetLang)
        {
            sourceLang = LanguageCodeService.NormalizeLanguageCode(sourceLang);
            targetLang = LanguageCodeService.NormalizeLanguageCode(targetLang);
            return $"({sourceLang} to {targetLang})";
        }

        public static bool ShouldProcessEditedMessage(bool hadCachedSnapshot, string? beforeContent, string? afterContent)
        {
            if (!hadCachedSnapshot)
            {
                return true;
            }

            return !string.Equals(beforeContent ?? string.Empty, afterContent ?? string.Empty, StringComparison.Ordinal);
        }

        public static string FormatTranslatedReplyText(string sourceLang, string targetLang, string translatedText)
        {
            var prefix = $"*{FormatLanguagePair(sourceLang, targetLang)}:*";
            return string.IsNullOrWhiteSpace(translatedText)
                ? prefix
                : $"{prefix} {translatedText}";
        }

        public static string FormatMirroredAuthorText(string authorName, string content)
        {
            return string.IsNullOrWhiteSpace(content)
                ? $"**{authorName}**:"
                : $"**{authorName}**:\n{content}";
        }

        public static string? ResolveLinkedMessageTargetLanguageCode(string? storedLanguageCode, string? configuredTargetLanguageCode)
        {
            if (!string.IsNullOrWhiteSpace(configuredTargetLanguageCode))
            {
                return LanguageCodeService.NormalizeLanguageCode(configuredTargetLanguageCode);
            }

            var stored = LanguageCodeService.NormalizeLanguageCode(storedLanguageCode);
            return string.Equals(stored, "MASTER", StringComparison.Ordinal) ? null : stored;
        }

        public static bool ShouldTranslateLinkedMessage(string sourceLang, string? targetLang)
        {
            return !string.IsNullOrWhiteSpace(targetLang)
                && !LanguageCodeService.AreSameLanguageCode(sourceLang, targetLang);
        }

        public static string FormatEditedLinkedMessageText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            string sourceLang,
            string? targetLang,
            string content)
        {
            if (!ShouldTranslateLinkedMessage(sourceLang, targetLang))
            {
                return FormatMirroredAuthorText(authorName, content);
            }

            return sourceChannelId == linkedChannelId
                ? FormatTranslatedReplyText(sourceLang, targetLang!, content)
                : string.IsNullOrWhiteSpace(content)
                    ? FormatMirroredAuthorText(authorName, string.Empty)
                    : $"**{authorName}** {FormatLanguagePair(sourceLang, targetLang!)}:\n{content}";
        }

        public static string FormatTranslationFailureText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            string sourceLang,
            string targetLang,
            string sourceContent)
        {
            return sourceChannelId == linkedChannelId
                ? FormatTranslatedReplyText(sourceLang, targetLang, $"{sourceContent} *(Translation Failed)*")
                : FormatMirroredAuthorText(authorName, $"{sourceContent} *(Translation Failed)*");
        }

        public static async Task<bool> TryModifyMessageContentAsync(IMessageChannel channel, ulong messageId, string newText)
        {
            if (channel == null) return false;

            var fetched = await channel.GetMessageAsync(messageId);
            if (fetched is IUserMessage userMsg)
            {
                await userMsg.ModifyAsync(p => p.Content = newText);
                return true;
            }

            return false;
        }
    }
}
