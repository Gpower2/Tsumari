using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class DiscordGatewayHostedServiceLifecycleTests
    {
        [Fact]
        public async Task HandleMessageReceivedAsync_SwallowsBoundaryExceptions()
        {
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            using var harness = CreateHarness(processorMock.Object);
            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(12345UL);

            await harness.HostedService.HandleMessageReceivedAsync(messageMock.Object);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_SwallowsBoundaryExceptions()
        {
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            using var harness = CreateHarness(processorMock.Object);
            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(67890UL);

            await harness.HostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "before", messageMock.Object);
        }

        [Fact]
        public async Task OnMessageDeletedAsync_ReturnsBeforeQueuedProcessingCompletes()
        {
            var processorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowProcessingToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .ReturnsAsync((GatewayIngressEvent gatewayEvent) => [new GatewayDispatchItem(10UL, gatewayEvent)]);
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(async _ =>
                {
                    processorStarted.TrySetResult();
                    await allowProcessingToFinish.Task;
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);
            await dispatcher.StartAsync(CancellationToken.None);

            using var harness = CreateHarness(processorMock.Object, dispatcher, dispatcher);
            var messageCache = new Cacheable<IMessage, ulong>(null!, 12345UL, false, () => Task.FromResult<IMessage>(null!));
            var channelCache = new Cacheable<IMessageChannel, ulong>(null!, 10UL, false, () => Task.FromResult<IMessageChannel>(null!));

            var invokeTask = harness.HostedService.OnMessageDeletedAsync(messageCache, channelCache);

            await invokeTask.WaitAsync(TimeSpan.FromSeconds(2));
            await processorStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            allowProcessingToFinish.TrySetResult();
        }

        [Fact]
        public async Task EnsureReadyInitializationAsync_OnlyInitializesDatabaseLoadsModulesAndRegistersCommandsOnce()
        {
            using var harness = CreateHarness();
            var initializeDatabaseCallCount = 0;
            var addModulesCallCount = 0;
            var registerCommandsCallCount = 0;

            await harness.HostedService.EnsureReadyInitializationAsync(
                () =>
                {
                    initializeDatabaseCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    addModulesCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    registerCommandsCallCount++;
                    return Task.CompletedTask;
                });

            await harness.HostedService.EnsureReadyInitializationAsync(
                () =>
                {
                    initializeDatabaseCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    addModulesCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    registerCommandsCallCount++;
                    return Task.CompletedTask;
                });

            Assert.Equal(1, initializeDatabaseCallCount);
            Assert.Equal(1, addModulesCallCount);
            Assert.Equal(1, registerCommandsCallCount);
        }

        [Fact]
        public async Task EnsureReadyInitializationAsync_RetriesCommandRegistrationWithoutReloadingModulesOrDatabase()
        {
            using var harness = CreateHarness();
            var initializeDatabaseCallCount = 0;
            var addModulesCallCount = 0;
            var registerCommandsCallCount = 0;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                harness.HostedService.EnsureReadyInitializationAsync(
                    () =>
                    {
                        initializeDatabaseCallCount++;
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        addModulesCallCount++;
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        registerCommandsCallCount++;
                        throw new InvalidOperationException("register failed");
                    }));

            await harness.HostedService.EnsureReadyInitializationAsync(
                () =>
                {
                    initializeDatabaseCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    addModulesCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    registerCommandsCallCount++;
                    return Task.CompletedTask;
                });

            Assert.Equal(1, initializeDatabaseCallCount);
            Assert.Equal(1, addModulesCallCount);
            Assert.Equal(2, registerCommandsCallCount);
        }

        [Fact]
        public async Task EnsureReadyInitializationAsync_RetriesDatabaseInitializationBeforeLoadingModules()
        {
            using var harness = CreateHarness();
            var initializeDatabaseCallCount = 0;
            var addModulesCallCount = 0;
            var registerCommandsCallCount = 0;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                harness.HostedService.EnsureReadyInitializationAsync(
                    () =>
                    {
                        initializeDatabaseCallCount++;
                        throw new InvalidOperationException("db failed");
                    },
                    () =>
                    {
                        addModulesCallCount++;
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        registerCommandsCallCount++;
                        return Task.CompletedTask;
                    }));

            await harness.HostedService.EnsureReadyInitializationAsync(
                () =>
                {
                    initializeDatabaseCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    addModulesCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    registerCommandsCallCount++;
                    return Task.CompletedTask;
                });

            Assert.Equal(2, initializeDatabaseCallCount);
            Assert.Equal(1, addModulesCallCount);
            Assert.Equal(1, registerCommandsCallCount);
        }

        [Fact]
        public async Task EnsureReadyInitializationAsync_RetriesModuleLoadingWithoutReinitializingDatabaseOrRegisteringCommands()
        {
            using var harness = CreateHarness();
            var initializeDatabaseCallCount = 0;
            var addModulesCallCount = 0;
            var registerCommandsCallCount = 0;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                harness.HostedService.EnsureReadyInitializationAsync(
                    () =>
                    {
                        initializeDatabaseCallCount++;
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        addModulesCallCount++;
                        throw new InvalidOperationException("module load failed");
                    },
                    () =>
                    {
                        registerCommandsCallCount++;
                        return Task.CompletedTask;
                    }));

            await harness.HostedService.EnsureReadyInitializationAsync(
                () =>
                {
                    initializeDatabaseCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    addModulesCallCount++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    registerCommandsCallCount++;
                    return Task.CompletedTask;
                });

            Assert.Equal(1, initializeDatabaseCallCount);
            Assert.Equal(2, addModulesCallCount);
            Assert.Equal(1, registerCommandsCallCount);
        }

        [Fact]
        public void RegisterAndUnregisterEventHandlers_AreIdempotent()
        {
            using var harness = CreateHarness();
            var clientEventFields = new[]
            {
                "_logEvent",
                "_readyEvent",
                "_messageReceivedEvent",
                "_messageDeletedEvent",
                "_messagesBulkDeletedEvent",
                "_messageUpdatedEvent",
                "_reactionAddedEvent",
                "_reactionRemovedEvent",
                "_reactionsClearedEvent",
                "_reactionsRemovedForEmoteEvent",
                "_interactionCreatedEvent"
            };
            var interactionEventFields = new[]
            {
                "_logEvent"
            };
            var baselineCounts = clientEventFields.ToDictionary(
                fieldName => $"client:{fieldName}",
                fieldName => GetAsyncEventSubscriptionCount(harness.Client, fieldName));
            foreach (var fieldName in interactionEventFields)
            {
                baselineCounts[$"interaction:{fieldName}"] = GetAsyncEventSubscriptionCount(harness.InteractionService, fieldName);
            }

            harness.HostedService.RegisterEventHandlers();
            Assert.True(harness.HostedService.EventHandlersRegistered);
            AssertSubscriberDeltas(harness.Client, clientEventFields, baselineCounts, expectedDelta: 1, "client");
            AssertSubscriberDeltas(harness.InteractionService, interactionEventFields, baselineCounts, expectedDelta: 1, "interaction");

            harness.HostedService.RegisterEventHandlers();
            Assert.True(harness.HostedService.EventHandlersRegistered);
            AssertSubscriberDeltas(harness.Client, clientEventFields, baselineCounts, expectedDelta: 1, "client");
            AssertSubscriberDeltas(harness.InteractionService, interactionEventFields, baselineCounts, expectedDelta: 1, "interaction");

            harness.HostedService.UnregisterEventHandlers();
            Assert.False(harness.HostedService.EventHandlersRegistered);
            AssertSubscriberDeltas(harness.Client, clientEventFields, baselineCounts, expectedDelta: 0, "client");
            AssertSubscriberDeltas(harness.InteractionService, interactionEventFields, baselineCounts, expectedDelta: 0, "interaction");

            harness.HostedService.UnregisterEventHandlers();
            Assert.False(harness.HostedService.EventHandlersRegistered);
            AssertSubscriberDeltas(harness.Client, clientEventFields, baselineCounts, expectedDelta: 0, "client");
            AssertSubscriberDeltas(harness.InteractionService, interactionEventFields, baselineCounts, expectedDelta: 0, "interaction");
        }

        private static void AssertSubscriberDeltas(
            object instance,
            IEnumerable<string> fieldNames,
            IReadOnlyDictionary<string, int> baselineCounts,
            int expectedDelta,
            string prefix)
        {
            foreach (var fieldName in fieldNames)
            {
                var actualCount = GetAsyncEventSubscriptionCount(instance, fieldName);
                var baseline = baselineCounts[$"{prefix}:{fieldName}"];
                Assert.Equal(baseline + expectedDelta, actualCount);
            }
        }

        private static int GetAsyncEventSubscriptionCount(object instance, string fieldName)
        {
            var field = FindField(instance.GetType(), fieldName)
                ?? throw new InvalidOperationException($"Could not find field '{fieldName}' on {instance.GetType().FullName}.");
            var asyncEvent = field.GetValue(instance)
                ?? throw new InvalidOperationException($"Field '{fieldName}' on {instance.GetType().FullName} was null.");
            var subscriptionsProperty = asyncEvent.GetType().GetProperty("Subscriptions", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Could not find Subscriptions property on {asyncEvent.GetType().FullName}.");
            var subscriptions = subscriptionsProperty.GetValue(asyncEvent)
                ?? throw new InvalidOperationException($"Subscriptions property on {asyncEvent.GetType().FullName} returned null.");
            var countProperty = subscriptions.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)
                ?? subscriptions.GetType().GetProperty("Length", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Could not find Count or Length property on {subscriptions.GetType().FullName}.");
            return (int)(countProperty.GetValue(subscriptions)
                ?? throw new InvalidOperationException($"{countProperty.Name} property on {subscriptions.GetType().FullName} returned null."));
        }

        private static FieldInfo? FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType!;
            }

            return null;
        }

        private static HostedServiceHarness CreateHarness(
            IDiscordGatewayEventProcessor? processor = null,
            IDiscordGatewayEventDispatcher? dispatcher = null,
            DiscordGatewayEventDispatcherService? managedDispatcher = null)
        {
            return new HostedServiceHarness(
                processor ?? Mock.Of<IDiscordGatewayEventProcessor>(),
                dispatcher ?? Mock.Of<IDiscordGatewayEventDispatcher>(),
                managedDispatcher);
        }

        private sealed class HostedServiceHarness : IDisposable
        {
            private readonly DiscordGatewayEventDispatcherService? _managedDispatcher;

            public HostedServiceHarness(
                IDiscordGatewayEventProcessor processor,
                IDiscordGatewayEventDispatcher dispatcher,
                DiscordGatewayEventDispatcherService? managedDispatcher)
            {
                _managedDispatcher = managedDispatcher;
                Client = new DiscordSocketClient(new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.None
                });
                InteractionService = new InteractionService(Client, new InteractionServiceConfig());
                HostedService = new DiscordGatewayHostedService(
                    Client,
                    InteractionService,
                    null!,
                    dispatcher,
                    processor,
                    null!,
                    new ConfigurationBuilder().Build(),
                    NullLogger<DiscordGatewayHostedService>.Instance);
            }

            public DiscordSocketClient Client { get; }

            public InteractionService InteractionService { get; }

            public DiscordGatewayHostedService HostedService { get; }

            public void Dispose()
            {
                _managedDispatcher?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                _managedDispatcher?.Dispose();
                HostedService.Dispose();
                Client.Dispose();
            }
        }
    }
}
