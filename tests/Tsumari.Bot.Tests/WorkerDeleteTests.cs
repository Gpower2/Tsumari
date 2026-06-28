using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests
{
    public class WorkerDeleteTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;

        public WorkerDeleteTests()
        {
            _testDbPath = $"test_tsumari_worker_delete_{Guid.NewGuid():N}.db";

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Database:FilePath"]).Returns(_testDbPath);

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

            var worker = CreateWorker(discordMessageService.Object);
            var messageCache = new Cacheable<IMessage, ulong>(null!, 55555UL, false, () => Task.FromResult<IMessage>(null!));
            var channelCache = new Cacheable<IMessageChannel, ulong>(null!, 11111UL, false, () => Task.FromResult<IMessageChannel>(null!));

            await InvokeWorkerAsync(worker, "OnMessageDeletedAsync", messageCache, channelCache);
            await deleteObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

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
                    if (System.Threading.Interlocked.Increment(ref deleteCount) == 2)
                    {
                        allDeletesObserved.TrySetResult();
                    }
                });
            discordMessageService
                .Setup(service => service.DeleteMessageAsync(20001UL, 88881UL))
                .ReturnsAsync(true)
                .Callback(() =>
                {
                    if (System.Threading.Interlocked.Increment(ref deleteCount) == 2)
                    {
                        allDeletesObserved.TrySetResult();
                    }
                });

            var worker = CreateWorker(discordMessageService.Object);
            var messageCaches = new List<Cacheable<IMessage, ulong>>
            {
                new(null!, 55555UL, false, () => Task.FromResult<IMessage>(null!)),
                new(null!, 77777UL, false, () => Task.FromResult<IMessage>(null!))
            };
            var channelCache = new Cacheable<IMessageChannel, ulong>(null!, 11111UL, false, () => Task.FromResult<IMessageChannel>(null!));

            await InvokeWorkerAsync(worker, "OnMessagesBulkDeletedAsync", messageCaches, channelCache);
            await allDeletesObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Empty(await _dbService.GetMirroredMessagesAsync(55555UL));
            Assert.Empty(await _dbService.GetMirroredMessagesAsync(77777UL));
        }

        private Worker CreateWorker(IDiscordMessageService discordMessageService)
        {
            var deletionService = new LinkedMessageDeletionService(
                discordMessageService,
                _dbService,
                NullLogger<LinkedMessageDeletionService>.Instance);

            var configMock = new Mock<IConfiguration>();

            return new Worker(
                null!,
                null!,
                _dbService,
                null!,
                deletionService,
                null!,
                null!,
                null!,
                configMock.Object,
                NullLogger<Worker>.Instance);
        }

        private static async Task InvokeWorkerAsync(Worker worker, string methodName, params object[] arguments)
        {
            var method = typeof(Worker).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Could not find Worker method '{methodName}'.");

            var task = (Task?)method.Invoke(worker, arguments)
                ?? throw new InvalidOperationException($"Worker method '{methodName}' did not return a task.");

            await task;
        }
    }
}
