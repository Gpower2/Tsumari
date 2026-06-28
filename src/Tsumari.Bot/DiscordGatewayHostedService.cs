using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Services;

namespace Tsumari.Bot
{
    public class DiscordGatewayHostedService : BackgroundService
    {
        private readonly object _eventHandlerLock = new();
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactionService;
        private readonly DatabaseService _dbService;
        private readonly MirroredMessageRoutingService _mirroredMessageRoutingService;
        private readonly EditedMessageSyncService _editedMessageSyncService;
        private readonly LinkedMessageDeletionService _linkedMessageDeletionService;
        private readonly ReactionMirroringService _reactionMirroringService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiscordGatewayHostedService> _logger;
        private bool _eventHandlersRegistered;

        public DiscordGatewayHostedService(
            DiscordSocketClient client,
            InteractionService interactionService,
            DatabaseService dbService,
            MirroredMessageRoutingService mirroredMessageRoutingService,
            EditedMessageSyncService editedMessageSyncService,
            LinkedMessageDeletionService linkedMessageDeletionService,
            ReactionMirroringService reactionMirroringService,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<DiscordGatewayHostedService> logger)
        {
            _client = client;
            _interactionService = interactionService;
            _dbService = dbService;
            _mirroredMessageRoutingService = mirroredMessageRoutingService;
            _editedMessageSyncService = editedMessageSyncService;
            _linkedMessageDeletionService = linkedMessageDeletionService;
            _reactionMirroringService = reactionMirroringService;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Tsumari Discord Gateway Hosted Service...");

            var token = _configuration["Discord:Token"] ?? _configuration["DiscordToken"];
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogCritical("Discord Token is completely missing from configuration! Shutting down worker.");
                throw new InvalidOperationException("Discord Token is required.");
            }

            RegisterEventHandlers();

            try
            {
                await _client.LoginAsync(TokenType.Bot, token);
                await _client.StartAsync();

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Discord gateway hosted service cancellation requested.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "A fatal exception crashed the Gateway Client lifecycle.");
                throw;
            }
            finally
            {
                UnregisterEventHandlers();
                await DisconnectClientAsync();
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            UnregisterEventHandlers();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            UnregisterEventHandlers();
            base.Dispose();
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
            return HandleMessageReceivedAsync(rawMessage);
        }

        private Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache)
        {
            return HandleMessageDeletedAsync(messageCache.Id);
        }

        private async Task OnMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messageCaches, Cacheable<IMessageChannel, ulong> channelCache)
        {
            var messageIds = new List<ulong>(messageCaches.Count);
            foreach (var messageCache in messageCaches)
            {
                messageIds.Add(messageCache.Id);
            }

            await HandleMessagesBulkDeletedAsync(messageIds, channelCache.Id);
        }

        private async Task OnMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after, ISocketMessageChannel channel)
        {
            try
            {
                var hadCachedSnapshot = beforeCache.HasValue;
                var beforeMessage = hadCachedSnapshot
                    ? await beforeCache.GetOrDownloadAsync()
                    : null;
                var beforeContent = beforeMessage?.Content ?? string.Empty;
                await HandleMessageUpdatedAsync(hadCachedSnapshot, beforeContent, after);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error handling edited message {MsgId}.", after.Id);
            }
        }

        private Task OnReactionAddedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            return HandleReactionAddedAsync(new DiscordReactionEvent
            {
                MessageId = reaction.MessageId,
                ChannelId = reaction.Channel.Id,
                Emote = reaction.Emote,
                ReactionType = reaction.ReactionType,
                UserId = reaction.UserId
            });
        }

        private Task OnReactionRemovedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            return HandleReactionRemovedAsync(new DiscordReactionEvent
            {
                MessageId = reaction.MessageId,
                ChannelId = reaction.Channel.Id,
                Emote = reaction.Emote,
                ReactionType = reaction.ReactionType,
                UserId = reaction.UserId
            });
        }

        private Task OnReactionsClearedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache)
        {
            return HandleReactionsClearedAsync(messageCache.Id, channelCache.Id);
        }

        private Task OnReactionsRemovedForEmoteAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, IEmote emote)
        {
            return HandleReactionsRemovedForEmoteAsync(messageCache.Id, channelCache.Id, emote);
        }

        public async Task HandleMessageReceivedAsync(IMessage rawMessage)
        {
            try
            {
                await _mirroredMessageRoutingService.HandleMessageReceivedAsync(rawMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error routing received message {MessageId}.", rawMessage.Id);
            }
        }

        public async Task HandleMessageDeletedAsync(ulong messageId)
        {
            try
            {
                await _linkedMessageDeletionService.HandleMessageDeletedAsync(messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error synchronizing delete for message {MessageId}.", messageId);
            }
        }

        public async Task HandleMessagesBulkDeletedAsync(IReadOnlyCollection<ulong> messageIds, ulong channelId)
        {
            try
            {
                await _linkedMessageDeletionService.HandleMessagesDeletedAsync(messageIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error synchronizing bulk delete in channel {ChannelId}.", channelId);
            }
        }

        public async Task HandleMessageUpdatedAsync(bool hadCachedSnapshot, string? beforeContent, IMessage afterMessage)
        {
            try
            {
                await _editedMessageSyncService.HandleMessageUpdatedAsync(hadCachedSnapshot, beforeContent, afterMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error synchronizing edited message {MessageId}.", afterMessage.Id);
            }
        }

        public async Task HandleReactionAddedAsync(DiscordReactionEvent reaction)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionAddedAsync(
                    reaction.MessageId,
                    reaction.ChannelId,
                    reaction.Emote,
                    reaction.ReactionType,
                    reaction.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error mirroring added reaction for message {MessageId}.", reaction.MessageId);
            }
        }

        public async Task HandleReactionRemovedAsync(DiscordReactionEvent reaction)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionRemovedAsync(
                    reaction.MessageId,
                    reaction.ChannelId,
                    reaction.Emote,
                    reaction.ReactionType,
                    reaction.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error mirroring removed reaction for message {MessageId}.", reaction.MessageId);
            }
        }

        public async Task HandleReactionsClearedAsync(ulong messageId, ulong channelId)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionsClearedAsync(messageId, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error mirroring cleared reactions for message {MessageId}.", messageId);
            }
        }

        public async Task HandleReactionsRemovedForEmoteAsync(ulong messageId, ulong channelId, IEmote emote)
        {
            try
            {
                await _reactionMirroringService.MirrorReactionsRemovedForEmoteAsync(messageId, channelId, emote);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error mirroring removed-for-emote reactions for message {MessageId}.", messageId);
            }
        }

        private void RegisterEventHandlers()
        {
            lock (_eventHandlerLock)
            {
                if (_eventHandlersRegistered)
                {
                    return;
                }

                _client.Log += OnLogAsync;
                _client.Ready += OnReadyAsync;
                _client.MessageReceived += OnMessageReceivedAsync;
                _client.MessageDeleted += OnMessageDeletedAsync;
                _client.MessagesBulkDeleted += OnMessagesBulkDeletedAsync;
                _client.MessageUpdated += OnMessageUpdatedAsync;
                _client.ReactionAdded += OnReactionAddedAsync;
                _client.ReactionRemoved += OnReactionRemovedAsync;
                _client.ReactionsCleared += OnReactionsClearedAsync;
                _client.ReactionsRemovedForEmote += OnReactionsRemovedForEmoteAsync;
                _client.InteractionCreated += OnInteractionCreatedAsync;

                _interactionService.Log += OnLogAsync;
                _eventHandlersRegistered = true;
            }
        }

        private void UnregisterEventHandlers()
        {
            lock (_eventHandlerLock)
            {
                if (!_eventHandlersRegistered)
                {
                    return;
                }

                _client.Log -= OnLogAsync;
                _client.Ready -= OnReadyAsync;
                _client.MessageReceived -= OnMessageReceivedAsync;
                _client.MessageDeleted -= OnMessageDeletedAsync;
                _client.MessagesBulkDeleted -= OnMessagesBulkDeletedAsync;
                _client.MessageUpdated -= OnMessageUpdatedAsync;
                _client.ReactionAdded -= OnReactionAddedAsync;
                _client.ReactionRemoved -= OnReactionRemovedAsync;
                _client.ReactionsCleared -= OnReactionsClearedAsync;
                _client.ReactionsRemovedForEmote -= OnReactionsRemovedForEmoteAsync;
                _client.InteractionCreated -= OnInteractionCreatedAsync;

                _interactionService.Log -= OnLogAsync;
                _eventHandlersRegistered = false;
            }
        }

        private async Task DisconnectClientAsync()
        {
            _logger.LogInformation("Disconnecting and disposing Discord client connection...");

            try
            {
                if (_client.LoginState != LoginState.LoggedOut)
                {
                    await _client.LogoutAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discord client logout failed during shutdown.");
            }

            try
            {
                if (_client.ConnectionState != ConnectionState.Disconnected)
                {
                    await _client.StopAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discord client stop failed during shutdown.");
            }
        }
    }
}
