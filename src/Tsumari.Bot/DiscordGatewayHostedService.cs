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
        private readonly IDiscordGatewayEventDispatcher _eventDispatcher;
        private readonly IDiscordGatewayEventProcessor _eventProcessor;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiscordGatewayHostedService> _logger;
        private readonly SemaphoreSlim _readyInitializationLock = new(1, 1);
        private bool _eventHandlersRegistered;
        private bool _databaseInitialized;
        private bool _interactionModulesLoaded;
        private bool _globalCommandsRegistered;

        public DiscordGatewayHostedService(
            DiscordSocketClient client,
            InteractionService interactionService,
            DatabaseService dbService,
            IDiscordGatewayEventDispatcher eventDispatcher,
            IDiscordGatewayEventProcessor eventProcessor,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<DiscordGatewayHostedService> logger)
        {
            _client = client;
            _interactionService = interactionService;
            _dbService = dbService;
            _eventDispatcher = eventDispatcher;
            _eventProcessor = eventProcessor;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogStartingHostedService();

            var token = _configuration["Discord:Token"] ?? _configuration["DiscordToken"];
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogMissingDiscordToken();
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
                _logger.LogCancellationRequested();
            }
            catch (Exception ex)
            {
                _logger.LogGatewayLifecycleCrashed(ex);
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

            _logger.LogDiscordNetMessage(level, log.Source, log.Message, log.Exception);
            return Task.CompletedTask;
        }

        private async Task OnReadyAsync()
        {
            _logger.LogReadyEventReceived();
            _logger.LogConnectedToGateway(_client.CurrentUser.ToString());

            try
            {
                await EnsureReadyInitializationAsync(
                    async () => await _dbService.InitializeDatabaseAsync(),
                    async () => await _interactionService.AddModulesAsync(typeof(Program).Assembly, _serviceProvider),
                    async () =>
                    {
                        await _interactionService.RegisterCommandsGloballyAsync();
                        _logger.LogAdministrativeCommandsRegistered();
                    });
            }
            catch (Exception ex)
            {
                _logger.LogReadyInitializationFailed(ex);
            }
        }

        internal async Task EnsureReadyInitializationAsync(
            Func<Task> initializeDatabaseAsync,
            Func<Task> addModulesAsync,
            Func<Task> registerCommandsAsync)
        {
            await _readyInitializationLock.WaitAsync();
            try
            {
                if (!_databaseInitialized)
                {
                    await initializeDatabaseAsync();
                    _databaseInitialized = true;
                }

                if (!_interactionModulesLoaded)
                {
                    await addModulesAsync();
                    _interactionModulesLoaded = true;
                }

                if (_globalCommandsRegistered)
                {
                    return;
                }

                await registerCommandsAsync();
                _globalCommandsRegistered = true;
            }
            finally
            {
                _readyInitializationLock.Release();
            }
        }

        private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
        {
            _logger.LogInteractionCreatedEventReceived(interaction.Type.ToString(), interaction.ChannelId, interaction.User.Id);
            var context = new SocketInteractionContext(_client, interaction);
            var result = await _interactionService.ExecuteCommandAsync(context, _serviceProvider);

            if (!result.IsSuccess)
            {
                _logger.LogSlashCommandExecutionFailed(result.ErrorReason);

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

        internal Task OnMessageReceivedAsync(SocketMessage rawMessage)
        {
            var enqueued = _eventDispatcher.TryEnqueue(new MessageReceivedGatewayEvent(rawMessage));
            _logger.LogMessageReceivedEventReceived(rawMessage.Id, rawMessage.Channel.Id, rawMessage.Source, enqueued);
            return Task.CompletedTask;
        }

        internal Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache)
        {
            var enqueued = _eventDispatcher.TryEnqueue(new MessageDeletedGatewayEvent(messageCache.Id, channelCache.Id));
            _logger.LogMessageDeletedEventReceived(messageCache.Id, channelCache.Id, enqueued);
            return Task.CompletedTask;
        }

        internal Task OnMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messageCaches, Cacheable<IMessageChannel, ulong> channelCache)
        {
            var messageIds = new List<ulong>(messageCaches.Count);
            foreach (var messageCache in messageCaches)
            {
                messageIds.Add(messageCache.Id);
            }

            var enqueued = _eventDispatcher.TryEnqueue(new MessagesBulkDeletedGatewayEvent(messageIds, channelCache.Id));
            _logger.LogMessagesBulkDeletedEventReceived(messageIds.Count, channelCache.Id, enqueued);
            return Task.CompletedTask;
        }

        internal Task OnMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after, ISocketMessageChannel channel)
        {
            var enqueued = _eventDispatcher.TryEnqueue(new MessageUpdatedGatewayEvent(beforeCache, after));
            _logger.LogMessageUpdatedEventReceived(after.Id, channel.Id, beforeCache.HasValue, enqueued);
            return Task.CompletedTask;
        }

        internal Task OnReactionAddedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            var enqueued = _eventDispatcher.TryEnqueue(new ReactionAddedGatewayEvent(new DiscordReactionEvent
            {
                MessageId = reaction.MessageId,
                ChannelId = channelCache.Id,
                Emote = reaction.Emote,
                ReactionType = reaction.ReactionType,
                UserId = reaction.UserId
            }));
            _logger.LogReactionAddedEventReceived(
                reaction.MessageId,
                channelCache.Id,
                reaction.UserId,
                reaction.Emote.ToString() ?? string.Empty,
                reaction.ReactionType,
                enqueued);
            return Task.CompletedTask;
        }

        internal Task OnReactionRemovedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            var enqueued = _eventDispatcher.TryEnqueue(new ReactionRemovedGatewayEvent(new DiscordReactionEvent
            {
                MessageId = reaction.MessageId,
                ChannelId = channelCache.Id,
                Emote = reaction.Emote,
                ReactionType = reaction.ReactionType,
                UserId = reaction.UserId
            }));
            _logger.LogReactionRemovedEventReceived(
                reaction.MessageId,
                channelCache.Id,
                reaction.UserId,
                reaction.Emote.ToString() ?? string.Empty,
                reaction.ReactionType,
                enqueued);
            return Task.CompletedTask;
        }

        internal Task OnReactionsClearedAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache)
        {
            var enqueued = _eventDispatcher.TryEnqueue(new ReactionsClearedGatewayEvent(messageCache.Id, channelCache.Id));
            _logger.LogReactionsClearedEventReceived(messageCache.Id, channelCache.Id, enqueued);
            return Task.CompletedTask;
        }

        internal Task OnReactionsRemovedForEmoteAsync(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, IEmote emote)
        {
            var enqueued = _eventDispatcher.TryEnqueue(new ReactionsRemovedForEmoteGatewayEvent(messageCache.Id, channelCache.Id, emote));
            _logger.LogReactionsRemovedForEmoteEventReceived(messageCache.Id, channelCache.Id, emote.ToString() ?? string.Empty, enqueued);
            return Task.CompletedTask;
        }

        internal bool EventHandlersRegistered => _eventHandlersRegistered;

        internal async Task HandleMessageReceivedAsync(IMessage rawMessage)
        {
            try
            {
                await _eventProcessor.ProcessMessageReceivedAsync(rawMessage);
            }
            catch (Exception ex)
            {
                _logger.LogMessageRoutingFailed(ex, rawMessage.Id);
            }
        }

        internal async Task HandleMessageDeletedAsync(ulong messageId)
        {
            try
            {
                await _eventProcessor.ProcessMessageDeletedAsync(messageId);
            }
            catch (Exception ex)
            {
                _logger.LogDeleteSynchronizationFailed(ex, messageId);
            }
        }

        internal async Task HandleMessagesBulkDeletedAsync(IReadOnlyCollection<ulong> messageIds, ulong channelId)
        {
            try
            {
                await _eventProcessor.ProcessMessagesBulkDeletedAsync(messageIds, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogBulkDeleteSynchronizationFailed(ex, channelId);
            }
        }

        internal async Task HandleMessageUpdatedAsync(bool hadCachedSnapshot, string? beforeContent, IMessage afterMessage)
        {
            try
            {
                await _eventProcessor.ProcessMessageUpdatedAsync(hadCachedSnapshot, beforeContent, afterMessage);
            }
            catch (Exception ex)
            {
                _logger.LogEditSynchronizationFailed(ex, afterMessage.Id);
            }
        }

        internal async Task HandleReactionAddedAsync(DiscordReactionEvent reaction)
        {
            try
            {
                await _eventProcessor.ProcessReactionAddedAsync(reaction);
            }
            catch (Exception ex)
            {
                _logger.LogReactionAddedMirroringFailed(ex, reaction.MessageId);
            }
        }

        internal async Task HandleReactionRemovedAsync(DiscordReactionEvent reaction)
        {
            try
            {
                await _eventProcessor.ProcessReactionRemovedAsync(reaction);
            }
            catch (Exception ex)
            {
                _logger.LogReactionRemovedMirroringFailed(ex, reaction.MessageId);
            }
        }

        internal async Task HandleReactionsClearedAsync(ulong messageId, ulong channelId)
        {
            try
            {
                await _eventProcessor.ProcessReactionsClearedAsync(messageId, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogReactionsClearedMirroringFailed(ex, messageId);
            }
        }

        internal async Task HandleReactionsRemovedForEmoteAsync(ulong messageId, ulong channelId, IEmote emote)
        {
            try
            {
                await _eventProcessor.ProcessReactionsRemovedForEmoteAsync(messageId, channelId, emote);
            }
            catch (Exception ex)
            {
                _logger.LogReactionsRemovedForEmoteMirroringFailed(ex, messageId);
            }
        }

        internal void RegisterEventHandlers()
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

        internal void UnregisterEventHandlers()
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
            _logger.LogDisconnectingClient();

            try
            {
                if (_client.LoginState != LoginState.LoggedOut)
                {
                    await _client.LogoutAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogClientLogoutFailed(ex);
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
                    _logger.LogClientStopFailed(ex);
            }
        }
    }
}
