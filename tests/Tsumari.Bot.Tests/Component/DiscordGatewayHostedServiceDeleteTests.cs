using System.Reflection;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class DiscordGatewayHostedServiceDeleteTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;

        public DiscordGatewayHostedServiceDeleteTests()
        {
            _testDbPath = $"test_tsumari_gateway_delete_{Guid.NewGuid():N}.db";

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(configuration => configuration["Database:FilePath"]).Returns(_testDbPath);

            _dbService = new DatabaseService(configMock.Object, NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }

                var walFile = $"{_testDbPath}-wal";
                if (File.Exists(walFile))
                {
                    File.Delete(walFile);
                }

                var shmFile = $"{_testDbPath}-shm";
                if (File.Exists(shmFile))
                {
                    File.Delete(shmFile);
                }
            }
            catch
            {
            }
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

            await InvokeHostedServiceAsync(hostedService, "OnMessageDeletedAsync", messageCache, channelCache);
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

            await InvokeHostedServiceAsync(hostedService, "OnMessagesBulkDeletedAsync", messageCaches, channelCache);
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

            var configMock = new Mock<IConfiguration>();

            return new DiscordGatewayHostedService(
                null!,
                null!,
                _dbService,
                null!,
                null!,
                deletionService,
                null!,
                null!,
                configMock.Object,
                NullLogger<DiscordGatewayHostedService>.Instance);
        }

        private static async Task InvokeHostedServiceAsync(DiscordGatewayHostedService hostedService, string methodName, params object[] arguments)
        {
            var method = typeof(DiscordGatewayHostedService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Could not find DiscordGatewayHostedService method '{methodName}'.");

            var task = (Task?)method.Invoke(hostedService, arguments)
                ?? throw new InvalidOperationException($"DiscordGatewayHostedService method '{methodName}' did not return a task.");

            await task;
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
