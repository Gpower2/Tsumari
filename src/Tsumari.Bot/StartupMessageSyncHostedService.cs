using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Logging;
using Tsumari.Bot.Services.Abstractions;

namespace Tsumari.Bot
{
    public class StartupMessageSyncHostedService : BackgroundService
    {
        private readonly IGatewayReadinessCoordinator _readinessCoordinator;
        private readonly IStartupMessageSyncService _startupMessageSyncService;
        private readonly ILogger<StartupMessageSyncHostedService> _logger;

        public StartupMessageSyncHostedService(
            IGatewayReadinessCoordinator readinessCoordinator,
            IStartupMessageSyncService startupMessageSyncService,
            ILogger<StartupMessageSyncHostedService> logger)
        {
            _readinessCoordinator = readinessCoordinator;
            _startupMessageSyncService = startupMessageSyncService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Wait until the gateway hosted service has finished initializing the database,
                // loading interaction modules, and registering global slash commands. This ensures
                // the bot is fully ready before we start the startup sync.
                await _readinessCoordinator.WaitForReadyAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Host stopped before the gateway was ready.
                return;
            }

            _logger.LogStartupSyncHostedServiceReady();

            try
            {
                await _startupMessageSyncService.RunAsync(stoppingToken);
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
