using System.Collections.Concurrent;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class DiscordGatewayEventDispatcherServiceTests
    {
        [Fact]
        public async Task TryEnqueue_ProcessesSameGroupSequentially()
        {
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processedMessageIds = new ConcurrentQueue<ulong>();
            var invocationCount = 0;

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .ReturnsAsync((GatewayIngressEvent gatewayEvent) => [new GatewayDispatchItem(10UL, gatewayEvent)]);

            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(async gatewayEvent =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    processedMessageIds.Enqueue(messageDeleted.MessageId);

                    var invocation = Interlocked.Increment(ref invocationCount);
                    if (invocation == 1)
                    {
                        firstStarted.TrySetResult();
                        await allowFirstToFinish.Task;
                        return;
                    }

                    secondStarted.TrySetResult();
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(2UL, 10UL)));

                await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(secondStarted.Task.IsCompleted);

                allowFirstToFinish.TrySetResult();

                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await dispatcher.StopAsync(CancellationToken.None);

                Assert.Equal([1UL, 2UL], processedMessageIds.ToArray());
            }
            finally
            {
                dispatcher.Dispose();
            }
        }

        [Fact]
        public async Task TryEnqueue_ProcessesDifferentGroupsInParallel()
        {
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .ReturnsAsync((GatewayIngressEvent gatewayEvent) =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    return (IReadOnlyList<GatewayDispatchItem>)[new GatewayDispatchItem(messageDeleted.MessageId, gatewayEvent)];
                });

            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(async gatewayEvent =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    if (messageDeleted.MessageId == 1UL)
                    {
                        firstStarted.TrySetResult();
                    }
                    else if (messageDeleted.MessageId == 2UL)
                    {
                        secondStarted.TrySetResult();
                    }

                    await allowCompletion.Task;
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(2UL, 20UL)));

                await Task.WhenAll(
                    firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)),
                    secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)));

                allowCompletion.TrySetResult();
                await dispatcher.StopAsync(CancellationToken.None);
            }
            finally
            {
                dispatcher.Dispose();
            }
        }

        [Fact]
        public async Task StopAsync_DrainsQueuedItemsBeforeReturning()
        {
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processedMessageIds = new ConcurrentQueue<ulong>();

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .ReturnsAsync((GatewayIngressEvent gatewayEvent) => [new GatewayDispatchItem(10UL, gatewayEvent)]);

            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(async gatewayEvent =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    processedMessageIds.Enqueue(messageDeleted.MessageId);

                    if (messageDeleted.MessageId == 1UL)
                    {
                        firstStarted.TrySetResult();
                        await allowFirstToFinish.Task;
                        return;
                    }

                    secondCompleted.TrySetResult();
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(2UL, 10UL)));

                await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

                var stopTask = dispatcher.StopAsync(CancellationToken.None);
                Assert.False(stopTask.IsCompleted);

                allowFirstToFinish.TrySetResult();

                await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await stopTask;

                Assert.Equal([1UL, 2UL], processedMessageIds.ToArray());
            }
            finally
            {
                dispatcher.Dispose();
            }
        }

        [Fact]
        public async Task ProcessorException_DoesNotStopSameGroupWorker()
        {
            var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationCount = 0;

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .ReturnsAsync((GatewayIngressEvent gatewayEvent) => [new GatewayDispatchItem(10UL, gatewayEvent)]);

            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(gatewayEvent =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    var invocation = Interlocked.Increment(ref invocationCount);
                    if (invocation == 1)
                    {
                        throw new InvalidOperationException("boom");
                    }

                    secondCompleted.TrySetResult();
                    Assert.Equal(2UL, messageDeleted.MessageId);
                    return Task.CompletedTask;
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(2UL, 10UL)));

                await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await dispatcher.StopAsync(CancellationToken.None);
            }
            finally
            {
                dispatcher.Dispose();
            }
        }

        [Fact]
        public async Task ResolverException_DoesNotStopRouter()
        {
            var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationCount = 0;

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(gatewayEvent =>
                {
                    var invocation = Interlocked.Increment(ref invocationCount);
                    if (invocation == 1)
                    {
                        throw new InvalidOperationException("resolver boom");
                    }

                    return Task.FromResult<IReadOnlyList<GatewayDispatchItem>>([new GatewayDispatchItem(20UL, gatewayEvent)]);
                });

            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(gatewayEvent =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    Assert.Equal(2UL, messageDeleted.MessageId);
                    secondCompleted.TrySetResult();
                    return Task.CompletedTask;
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(2UL, 10UL)));

                await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await dispatcher.StopAsync(CancellationToken.None);
            }
            finally
            {
                dispatcher.Dispose();
            }
        }

        [Fact]
        public async Task TryEnqueue_ReturnsFalseAfterStopAndDispose()
        {
            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            await dispatcher.StopAsync(CancellationToken.None);

            Assert.False(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));

            dispatcher.Dispose();

            Assert.False(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(2UL, 10UL)));
        }

        [Fact]
        public async Task TryEnqueue_PreservesMixedEventOrderingWithinSameGroup()
        {
            var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processedEvents = new ConcurrentQueue<string>();

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .ReturnsAsync((GatewayIngressEvent gatewayEvent) => [new GatewayDispatchItem(99UL, gatewayEvent)]);

            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(gatewayEvent =>
                {
                    processedEvents.Enqueue(gatewayEvent.GetType().Name);
                    if (gatewayEvent is ReactionAddedGatewayEvent)
                    {
                        secondCompleted.TrySetResult();
                    }

                    return Task.CompletedTask;
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));
                Assert.True(dispatcher.TryEnqueue(new ReactionAddedGatewayEvent(new DiscordReactionEvent
                {
                    MessageId = 1UL,
                    ChannelId = 10UL,
                    Emote = Mock.Of<IEmote>(),
                    ReactionType = Discord.ReactionType.Normal,
                    UserId = 42UL
                })));

                await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await dispatcher.StopAsync(CancellationToken.None);

                Assert.Equal(
                    [nameof(MessageDeletedGatewayEvent), nameof(ReactionAddedGatewayEvent)],
                    processedEvents.ToArray());
            }
            finally
            {
                dispatcher.Dispose();
            }
        }

        [Fact]
        public async Task StopAsync_DrainsQueuedItemsAcrossMultipleGroups()
        {
            var firstGroupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondGroupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondGroupCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstGroupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processedMessageIds = new ConcurrentQueue<ulong>();

            var resolverMock = new Mock<IGatewayEventGroupResolver>();
            resolverMock
                .Setup(resolver => resolver.ResolveDispatchesAsync(It.IsAny<GatewayIngressEvent>()))
                .ReturnsAsync((GatewayIngressEvent gatewayEvent) =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    return (IReadOnlyList<GatewayDispatchItem>)[new GatewayDispatchItem(messageDeleted.ChannelId, gatewayEvent)];
                });

            var processorMock = new Mock<IDiscordGatewayEventProcessor>();
            processorMock
                .Setup(processor => processor.ProcessAsync(It.IsAny<GatewayIngressEvent>()))
                .Returns<GatewayIngressEvent>(async gatewayEvent =>
                {
                    var messageDeleted = Assert.IsType<MessageDeletedGatewayEvent>(gatewayEvent);
                    processedMessageIds.Enqueue(messageDeleted.MessageId);

                    if (messageDeleted.ChannelId == 10UL)
                    {
                        firstGroupStarted.TrySetResult();
                        await allowFirstGroupToFinish.Task;
                        return;
                    }

                    secondGroupStarted.TrySetResult();
                    secondGroupCompleted.TrySetResult();
                });

            var dispatcher = new DiscordGatewayEventDispatcherService(
                resolverMock.Object,
                processorMock.Object,
                NullLogger<DiscordGatewayEventDispatcherService>.Instance);

            await dispatcher.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(1UL, 10UL)));
                Assert.True(dispatcher.TryEnqueue(new MessageDeletedGatewayEvent(2UL, 20UL)));

                await Task.WhenAll(
                    firstGroupStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)),
                    secondGroupStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)));

                var stopTask = dispatcher.StopAsync(CancellationToken.None);
                Assert.False(stopTask.IsCompleted);

                await secondGroupCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                allowFirstGroupToFinish.TrySetResult();
                await stopTask;

                Assert.Contains(1UL, processedMessageIds);
                Assert.Contains(2UL, processedMessageIds);
            }
            finally
            {
                dispatcher.Dispose();
            }
        }
    }
}
