using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class DiscordGatewayHostedServiceDeleteTests : IDisposable
    {
        private readonly TemporarySqliteDatabase _database;
        private readonly DatabaseService _dbService;
        private readonly List<DiscordGatewayEventDispatcherService> _dispatchers = [];

        public DiscordGatewayHostedServiceDeleteTests()
        {
            _database = new TemporarySqliteDatabase("gateway-delete");
            _dbService = _database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            foreach (var dispatcher in _dispatchers)
            {
                dispatcher.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                dispatcher.Dispose();
            }

            _database.Dispose();
        }

        [Fact]
        public async Task OnMessageDeletedAsync_ForwardsToDeletionService()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");

            var deleteObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(true)
                .Callback(() => deleteObserved.TrySetResult());

            var hostedService = CreateHostedService(discordMessageService.Object);
            var messageCache = new Cacheable<IMessage, ulong>(null!, 55555UL, false, () => Task.FromResult<IMessage>(null!));
            var channelCache = new Cacheable<IMessageChannel, ulong>(null!, 11111UL, false, () => Task.FromResult<IMessageChannel>(null!));

            await hostedService.OnMessageDeletedAsync(messageCache, channelCache);
            await deleteObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(async () => (await _dbService.GetMirroredMessagesAsync(55555UL)).Count == 0);

            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
        }

        [Fact]
        public async Task OnMessagesBulkDeletedAsync_ForwardsAllIdsToDeletionService()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(55555UL, 11111UL, 66661UL, 10001UL, "de");
            await _dbService.LinkMessagesAsync(77777UL, 22222UL, 88881UL, 20001UL, "it");

            int deleteCount = 0;
            var allDeletesObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var discordMessageService = new Mock<IDiscordMessageService>();
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(10001UL, 66661UL))
                .ReturnsAsync(true)
                .Callback(() =>
                {
                    if (Interlocked.Increment(ref deleteCount) == 2)
                    {
                        allDeletesObserved.TrySetResult();
                    }
                });
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(20001UL, 88881UL))
                .ReturnsAsync(true)
                .Callback(() =>
                {
                    if (Interlocked.Increment(ref deleteCount) == 2)
                    {
                        allDeletesObserved.TrySetResult();
                    }
                });

            var hostedService = CreateHostedService(discordMessageService.Object);
            var messageCaches = new List<Cacheable<IMessage, ulong>>
            {
                new(null!, 55555UL, false, () => Task.FromResult<IMessage>(null!)),
                new(null!, 77777UL, false, () => Task.FromResult<IMessage>(null!))
            };
            var channelCache = new Cacheable<IMessageChannel, ulong>(null!, 11111UL, false, () => Task.FromResult<IMessageChannel>(null!));

            await hostedService.OnMessagesBulkDeletedAsync(messageCaches, channelCache);
            await allDeletesObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(async () =>
                (await _dbService.GetMirroredMessagesAsync(55555UL)).Count == 0
                && (await _dbService.GetMirroredMessagesAsync(77777UL)).Count == 0);

            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
            Assert.Empty(await _dbService.GetMirroredMessagesAsync(77777UL));
        }

        private DiscordGatewayHostedService CreateHostedService(IDiscordMessageService discordMessageService)
        {
            var deletionService = new LinkedMessageDeletionService(
                discordMessageService,
                _dbService,
                NullLogger<LinkedMessageDeletionService>.Instance);
            var processor = new DiscordGatewayEventProcessorService(
                null!,
                null!,
                deletionService,
                null!,
                NullLogger<DiscordGatewayEventProcessorService>.Instance);
            var resolver = new GatewayEventGroupResolver(
                _dbService,
                NullLogger<GatewayEventGroupResolver>.Instance);
            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolver,
                processor,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);
            dispatcher.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            _dispatchers.Add(dispatcher);

            return new DiscordGatewayHostedService(
                null!,
                null!,
                _dbService,
                dispatcher,
                processor,
                null!,
                null!,
                NullLogger<DiscordGatewayHostedService>.Instance);
        }

        private static async Task WaitUntilAsync(Func<Task<bool>> condition, int maxAttempts = 40, int delayMs = 25)
        {
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (await condition())
                {
                    return;
                }

                await Task.Delay(delayMs);
            }

            throw new TimeoutException("Condition was not satisfied before timeout.");
        }
    }
}
