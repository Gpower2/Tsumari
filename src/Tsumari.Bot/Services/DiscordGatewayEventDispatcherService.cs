using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class DiscordGatewayEventDispatcherService : IHostedService, IDisposable, IDiscordGatewayEventDispatcher
    {
        private readonly Channel<GatewayIngressEvent> _ingressChannel = Channel.CreateUnbounded<GatewayIngressEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        private readonly ConcurrentDictionary<ulong, GroupQueueState> _groupQueues = new();
        private readonly IGatewayEventGroupResolver _groupResolver;
        private readonly IDiscordGatewayEventProcessor _eventProcessor;
        private readonly ILogger<DiscordGatewayEventDispatcherService> _logger;

        private readonly object _lifecycleLock = new();
        private bool _acceptingEvents;
        private bool _started;
        private Task? _routerTask;

        public DiscordGatewayEventDispatcherService(
            IGatewayEventGroupResolver groupResolver,
            IDiscordGatewayEventProcessor eventProcessor,
            ILogger<DiscordGatewayEventDispatcherService> logger)
        {
            _groupResolver = groupResolver;
            _eventProcessor = eventProcessor;
            _logger = logger;
        }

        public bool TryEnqueue(GatewayIngressEvent gatewayEvent)
        {
            if (!_acceptingEvents)
            {
                _logger.LogGatewayEventDroppedDispatcherStopping(gatewayEvent.GetType().Name);
                return false;
            }

            if (!_ingressChannel.Writer.TryWrite(gatewayEvent))
            {
                _logger.LogGatewayEventDroppedQueueClosed(gatewayEvent.GetType().Name);
                return false;
            }

            return true;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_lifecycleLock)
            {
                if (_started)
                {
                    return Task.CompletedTask;
                }

                _acceptingEvents = true;
                _started = true;
                _routerTask = RunRouterLoopAsync();
                return Task.CompletedTask;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task? routerTask;

            lock (_lifecycleLock)
            {
                if (!_started)
                {
                    return;
                }

                _acceptingEvents = false;
                _started = false;
                _ingressChannel.Writer.TryComplete();
                routerTask = _routerTask;
            }

            if (routerTask != null)
            {
                await routerTask.WaitAsync(cancellationToken);
            }
        }

        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                _acceptingEvents = false;
                _ingressChannel.Writer.TryComplete();
            }
        }

        private async Task RunRouterLoopAsync()
        {
            try
            {
                while (await _ingressChannel.Reader.WaitToReadAsync())
                {
                    while (_ingressChannel.Reader.TryRead(out var gatewayEvent))
                    {
                        IReadOnlyList<GatewayDispatchItem> dispatchItems;

                        try
                        {
                            dispatchItems = await _groupResolver.ResolveDispatchesAsync(gatewayEvent);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogGatewayEventGroupResolutionFailed(ex, gatewayEvent.GetType().Name);
                            continue;
                        }

                        foreach (var dispatchItem in dispatchItems)
                        {
                            var groupQueue = _groupQueues.GetOrAdd(dispatchItem.GroupKey, CreateGroupQueueState);
                            if (!groupQueue.Channel.Writer.TryWrite(dispatchItem.Event))
                            {
                                _logger.LogGroupQueueWriteRejected(dispatchItem.GroupKey, dispatchItem.Event.GetType().Name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogGatewayEventRouterLoopFailed(ex);
            }
            finally
            {
                foreach (var groupQueue in _groupQueues.Values)
                {
                    groupQueue.Channel.Writer.TryComplete();
                }

                var groupTasks = _groupQueues.Values.Select(groupQueue => groupQueue.ConsumerTask).ToArray();
                if (groupTasks.Length > 0)
                {
                    await Task.WhenAll(groupTasks);
                }
            }
        }

        private GroupQueueState CreateGroupQueueState(ulong groupKey)
        {
            var channel = Channel.CreateUnbounded<GatewayIngressEvent>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true
                });

            return new GroupQueueState(
                channel,
                RunGroupLoopAsync(groupKey, channel.Reader));
        }

        private async Task RunGroupLoopAsync(ulong groupKey, ChannelReader<GatewayIngressEvent> channelReader)
        {
            while (await channelReader.WaitToReadAsync())
            {
                while (channelReader.TryRead(out var gatewayEvent))
                {
                    try
                    {
                        await _eventProcessor.ProcessAsync(gatewayEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogGatewayEventGroupWorkerFailed(ex, groupKey, gatewayEvent.GetType().Name);
                    }
                }
            }
        }

        private sealed record GroupQueueState(Channel<GatewayIngressEvent> Channel, Task ConsumerTask);
    }
}
