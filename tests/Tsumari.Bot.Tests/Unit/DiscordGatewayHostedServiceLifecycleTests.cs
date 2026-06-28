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
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());
            var configMock = new Mock<IConfiguration>();
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                processorMock.Object,
                null!,
                configMock.Object,
                NullLogger<DiscordGatewayHostedService>.Instance);
            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(12345UL);

            try
            {
                await hostedService.HandleMessageReceivedAsync(messageMock.Object);
            }
            finally
            {
                hostedService.Dispose();
                client.Dispose();
            }
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_SwallowsBoundaryExceptions()
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());
            var configMock = new Mock<IConfiguration>();
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                processorMock.Object,
                null!,
                configMock.Object,
                NullLogger<DiscordGatewayHostedService>.Instance);
            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(67890UL);

            try
            {
                await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "before", messageMock.Object);
            }
            finally
            {
                hostedService.Dispose();
                client.Dispose();
            }
        }

        [Fact]
        public async Task OnMessageDeletedAsync_ReturnsBeforeQueuedProcessingCompletes()
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());
            var configMock = new Mock<IConfiguration>();
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

            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                dispatcher,
                processorMock.Object,
                null!,
                configMock.Object,
                NullLogger<DiscordGatewayHostedService>.Instance);
            var messageCache = new Cacheable<IMessage, ulong>(null!, 12345UL, false, () => Task.FromResult<IMessage>(null!));
            var channelCache = new Cacheable<IMessageChannel, ulong>(null!, 10UL, false, () => Task.FromResult<IMessageChannel>(null!));

            try
            {
                var invokeTask = InvokeHostedServiceAsync(
                    hostedService,
                    "OnMessageDeletedAsync",
                    messageCache,
                    channelCache);

                await invokeTask.WaitAsync(TimeSpan.FromSeconds(2));
                await processorStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                allowProcessingToFinish.TrySetResult();
                await dispatcher.StopAsync(CancellationToken.None);
                dispatcher.Dispose();
                hostedService.Dispose();
                client.Dispose();
            }
        }

        [Fact]
        public async Task EnsureInteractionCommandsInitializedAsync_OnlyLoadsModulesAndRegistersCommandsOnce()
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());
            var configMock = new Mock<IConfiguration>();
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                processorMock.Object,
                null!,
                configMock.Object,
                NullLogger<DiscordGatewayHostedService>.Instance);
            var addModulesCallCount = 0;
            var registerCommandsCallCount = 0;

            try
            {
                await InvokeHostedServiceAsync(
                    hostedService,
                    "EnsureInteractionCommandsInitializedAsync",
                    new Func<Task>(() =>
                    {
                        addModulesCallCount++;
                        return Task.CompletedTask;
                    }),
                    new Func<Task>(() =>
                    {
                        registerCommandsCallCount++;
                        return Task.CompletedTask;
                    }));

                await InvokeHostedServiceAsync(
                    hostedService,
                    "EnsureInteractionCommandsInitializedAsync",
                    new Func<Task>(() =>
                    {
                        addModulesCallCount++;
                        return Task.CompletedTask;
                    }),
                    new Func<Task>(() =>
                    {
                        registerCommandsCallCount++;
                        return Task.CompletedTask;
                    }));

                Assert.Equal(1, addModulesCallCount);
                Assert.Equal(1, registerCommandsCallCount);
            }
            finally
            {
                hostedService.Dispose();
                client.Dispose();
            }
        }

        [Fact]
        public async Task EnsureInteractionCommandsInitializedAsync_RetriesCommandRegistrationWithoutReloadingModules()
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());
            var configMock = new Mock<IConfiguration>();
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                processorMock.Object,
                null!,
                configMock.Object,
                NullLogger<DiscordGatewayHostedService>.Instance);
            var addModulesCallCount = 0;
            var registerCommandsCallCount = 0;

            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    InvokeHostedServiceAsync(
                        hostedService,
                        "EnsureInteractionCommandsInitializedAsync",
                        new Func<Task>(() =>
                        {
                            addModulesCallCount++;
                            return Task.CompletedTask;
                        }),
                        new Func<Task>(() =>
                        {
                            registerCommandsCallCount++;
                            throw new InvalidOperationException("register failed");
                        })));

                await InvokeHostedServiceAsync(
                    hostedService,
                    "EnsureInteractionCommandsInitializedAsync",
                    new Func<Task>(() =>
                    {
                        addModulesCallCount++;
                        return Task.CompletedTask;
                    }),
                    new Func<Task>(() =>
                    {
                        registerCommandsCallCount++;
                        return Task.CompletedTask;
                    }));

                Assert.Equal(1, addModulesCallCount);
                Assert.Equal(2, registerCommandsCallCount);
            }
            finally
            {
                hostedService.Dispose();
                client.Dispose();
            }
        }

        [Fact]
        public void RegisterAndUnregisterEventHandlers_AreIdempotent_AndRestoreSubscriberCounts()
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());
            var configMock = new Mock<IConfiguration>();
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                processorMock.Object,
                null!,
                configMock.Object,
                NullLogger<DiscordGatewayHostedService>.Instance);

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
                fieldName => GetAsyncEventSubscriptionCount(client, fieldName));
            foreach (var fieldName in interactionEventFields)
            {
                baselineCounts[$"interaction:{fieldName}"] = GetAsyncEventSubscriptionCount(interactionService, fieldName);
            }

            try
            {
                InvokePrivateMethod(hostedService, "RegisterEventHandlers");
                AssertSubscriberDeltas(client, clientEventFields, baselineCounts, expectedDelta: 1, "client");
                AssertSubscriberDeltas(interactionService, interactionEventFields, baselineCounts, expectedDelta: 1, "interaction");

                InvokePrivateMethod(hostedService, "RegisterEventHandlers");
                AssertSubscriberDeltas(client, clientEventFields, baselineCounts, expectedDelta: 1, "client");
                AssertSubscriberDeltas(interactionService, interactionEventFields, baselineCounts, expectedDelta: 1, "interaction");

                InvokePrivateMethod(hostedService, "UnregisterEventHandlers");
                AssertSubscriberDeltas(client, clientEventFields, baselineCounts, expectedDelta: 0, "client");
                AssertSubscriberDeltas(interactionService, interactionEventFields, baselineCounts, expectedDelta: 0, "interaction");

                InvokePrivateMethod(hostedService, "UnregisterEventHandlers");
                AssertSubscriberDeltas(client, clientEventFields, baselineCounts, expectedDelta: 0, "client");
                AssertSubscriberDeltas(interactionService, interactionEventFields, baselineCounts, expectedDelta: 0, "interaction");
            }
            finally
            {
                hostedService.Dispose();
                client.Dispose();
            }
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

        private static void InvokePrivateMethod(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Could not find private method '{methodName}' on {instance.GetType().FullName}.");
            method.Invoke(instance, null);
        }

        private static async Task InvokeHostedServiceAsync(object instance, string methodName, params object[] arguments)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Could not find private method '{methodName}' on {instance.GetType().FullName}.");
            var task = (Task?)method.Invoke(instance, arguments)
                ?? throw new InvalidOperationException($"Method '{methodName}' on {instance.GetType().FullName} did not return a task.");
            await task;
        }
    }
}
