using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot;
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
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                null!,
                null!,
                null!,
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
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                null!,
                null!,
                null!,
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
        public void RegisterAndUnregisterEventHandlers_AreIdempotent_AndRestoreSubscriberCounts()
        {
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());
            var configMock = new Mock<IConfiguration>();
            var hostedService = new DiscordGatewayHostedService(
                client,
                interactionService,
                null!,
                null!,
                null!,
                null!,
                null!,
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
    }
}
