using System;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Logging;
using Tsumari.Bot.Services.Abstractions;

namespace Tsumari.Bot
{
    public class StartupMessageSyncHostedService : BackgroundService
    {
        private readonly DiscordSocketClient _client;
        private readonly IStartupMessageSyncService _startupMessageSyncService;
        private readonly ILogger<StartupMessageSyncHostedService> _logger;
        private int _readyEventReceived;
        private CancellationToken _stoppingToken;

        public StartupMessageSyncHostedService(
            DiscordSocketClient client,
            IStartupMessageSyncService startupMessageSyncService,
            ILogger<StartupMessageSyncHostedService> logger)
        {
            _client = client;
            _startupMessageSyncService = startupMessageSyncService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            _client.Ready += OnReadyAsync;

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected when the host is stopping.
            }
            finally
            {
                _client.Ready -= OnReadyAsync;
            }
        }

        internal async Task OnReadyAsync()
        {
            // Discord.NET can fire multiple Ready events during a session; only run once.
            if (Interlocked.Exchange(ref _readyEventReceived, 1) == 1)
            {
                return;
            }

            _logger.LogStartupSyncHostedServiceReady();

            try
            {
                await _startupMessageSyncService.RunAsync(_stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when the host is stopping; do not log as a failure.
            }
            catch (Exception ex)
            {
                _logger.LogStartupSyncHostedServiceFailed(ex);
            }
        }
    }
}
