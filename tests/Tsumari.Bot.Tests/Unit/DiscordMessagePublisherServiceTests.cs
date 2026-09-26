using System.Net;
using System.Net.Http;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tsumari.Bot.Configuration;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class DiscordMessagePublisherServiceTests
    {
        [Fact]
        public async Task FullButtonLayout_ReservesSelfLinkSlotForRetry_PreservesOtherLinks()
        {
            var original = new Mock<IMessage>();
            var originalChannel = new Mock<ITextChannel>();
            originalChannel.SetupGet(c => c.Id).Returns(10);
            originalChannel.SetupGet(c => c.GuildId).Returns(1);
            original.SetupGet(m => m.Channel).Returns(originalChannel.Object);
            original.SetupGet(m => m.Id).Returns(100);
            var messages = new Dictionary<ulong, IUserMessage>();
            var results = new Dictionary<ulong, MessageComponent>();
            var targets = new List<JumpLinkTarget>();
            for (ulong i = 20; i < 44; i++)
            {
                var id = i;
                var channel = new Mock<ITextChannel>();
                channel.SetupGet(c => c.Id).Returns(id); channel.SetupGet(c => c.GuildId).Returns(1);
                var message = new Mock<IUserMessage>();
                message.SetupGet(m => m.Id).Returns(id + 100);
                message.SetupGet(m => m.Channel).Returns(channel.Object);
                message.Setup(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()))
                    .Callback<Action<MessageProperties>, RequestOptions>((action, _) => { var p = new MessageProperties(); action(p); results[id] = p.Components.Value; }).Returns(Task.CompletedTask);
                messages[id] = message.Object;
                targets.Add(new() { ChannelId = id, LanguageLabel = $"L{id}" });
            }
            await CreatePublisher().EditJumpButtonsIntoSentMessagesAsync(original.Object, messages, targets);
            Assert.Equal(24, results.Count);
            foreach (var pair in results)
            {
                var buttons = pair.Value.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToArray();
                Assert.Equal(25, buttons.Length);
                Assert.Single(buttons, b => b.CustomId == TranslationRetryRules.ButtonId);
                Assert.Single(buttons, b => b.Label == "Original");
                foreach (var other in messages.Where(m => m.Key != pair.Key))
                    Assert.Contains(buttons, b => b.Url == MirroredMessageFormatter.BuildJumpUrl(other.Value));
            }
        }

        [Fact]
        public async Task JumpButtonRefresh_AddsSingleClockwiseRetryOnlyToLocalizedCopy()
        {
            var original = new Mock<IMessage>();
            var channel = new Mock<ITextChannel>();
            channel.SetupGet(c => c.Id).Returns(10);
            channel.SetupGet(c => c.GuildId).Returns(1);
            original.SetupGet(m => m.Id).Returns(100);
            original.SetupGet(m => m.Channel).Returns(channel.Object);
            var local = new Mock<IUserMessage>();
            local.SetupGet(m => m.Id).Returns(200);
            local.SetupGet(m => m.Channel).Returns(channel.Object);
            var master = new Mock<IUserMessage>();
            master.SetupGet(m => m.Id).Returns(300);
            master.SetupGet(m => m.Channel).Returns(channel.Object);
            MessageComponent? localComponents = null, masterComponents = null;
            local.Setup(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()))
                .Callback<Action<MessageProperties>, RequestOptions>((action, _) => { var p = new MessageProperties(); action(p); localComponents = p.Components.Value; }).Returns(Task.CompletedTask);
            master.Setup(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()))
                .Callback<Action<MessageProperties>, RequestOptions>((action, _) => { var p = new MessageProperties(); action(p); masterComponents = p.Components.Value; }).Returns(Task.CompletedTask);
            var publisher = CreatePublisher();
            for (var i = 0; i < 2; i++)
                await publisher.EditJumpButtonsIntoSentMessagesAsync(original.Object,
                    new Dictionary<ulong, IUserMessage> { [20] = local.Object, [30] = master.Object },
                    [new() { ChannelId = 20, LanguageLabel = "FR" }, new() { ChannelId = 30, LanguageLabel = "Original" }]);
            var buttons = localComponents!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToArray();
            var retry = Assert.Single(buttons, b => b.CustomId == TranslationRetryRules.ButtonId);
            Assert.Equal("↻", retry.Label);
            Assert.Equal(ButtonStyle.Secondary, retry.Style);
            Assert.Null(retry.Url);
            Assert.Contains(buttons, b => b.Label == "Original" && b.Style == ButtonStyle.Link);
            Assert.DoesNotContain(masterComponents!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>(), b => b.CustomId == TranslationRetryRules.ButtonId);
        }

        [Fact]
        public async Task SendMessageWithFilesAsync_PassesReplyReference_ForTextOnlyMessages()
        {
            var publisher = CreatePublisher();
            var channelMock = new Mock<IMessageChannel>();
            var userMessageMock = new Mock<IUserMessage>();
            MessageReference? capturedReference = null;

            channelMock
                .Setup(channel => channel.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .Callback<string, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[]?, Embed[]?, MessageFlags, PollProperties?>(
                    (_, _, _, _, _, messageReference, _, _, _, _, _) => capturedReference = messageReference)
                .ReturnsAsync(userMessageMock.Object);

            var replyReference = new MessageReference(12345UL, 54321UL, null, false, default);

            var result = await publisher.SendMessageWithFilesAsync(channelMock.Object, "hello", [], null, replyReference);

            Assert.Same(userMessageMock.Object, result);
            Assert.NotNull(capturedReference);
            Assert.Equal(12345UL, capturedReference!.MessageId.Value);
            Assert.Equal(54321UL, capturedReference.ChannelId);
        }

        [Fact]
        public async Task SendMessageWithFilesAsync_PassesReplyReference_ForFileMessages()
        {
            var publisher = CreatePublisher();
            var channelMock = new Mock<IMessageChannel>();
            var userMessageMock = new Mock<IUserMessage>();
            MessageReference? capturedReference = null;

            channelMock
                .Setup(channel => channel.SendFilesAsync(
                    It.IsAny<IEnumerable<FileAttachment>>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .Callback<IEnumerable<FileAttachment>, string, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[]?, Embed[]?, MessageFlags, PollProperties?>(
                    (_, _, _, _, _, _, messageReference, _, _, _, _, _) => capturedReference = messageReference)
                .ReturnsAsync(userMessageMock.Object);

            var replyReference = new MessageReference(22222UL, 33333UL, null, false, default);
            var mediaAssets = new List<MediaAsset>
            {
                new() { Filename = "file.txt", Bytes = [1, 2, 3] }
            };

            var result = await publisher.SendMessageWithFilesAsync(channelMock.Object, "hello", mediaAssets, null, replyReference);

            Assert.Same(userMessageMock.Object, result);
            Assert.NotNull(capturedReference);
            Assert.Equal(22222UL, capturedReference!.MessageId.Value);
            Assert.Equal(33333UL, capturedReference.ChannelId);
        }

        [Fact]
        public async Task SendMessageWithFilesAsync_PreservesReplyReference_WhenFallingBackToText()
        {
            var publisher = CreatePublisher();
            var channelMock = new Mock<IMessageChannel>();
            var userMessageMock = new Mock<IUserMessage>();
            MessageReference? capturedFallbackReference = null;

            channelMock
                .Setup(channel => channel.SendFilesAsync(
                    It.IsAny<IEnumerable<FileAttachment>>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .ThrowsAsync(new InvalidOperationException("upload failed"));

            channelMock
                .Setup(channel => channel.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .Callback<string, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[]?, Embed[]?, MessageFlags, PollProperties?>(
                    (_, _, _, _, _, messageReference, _, _, _, _, _) => capturedFallbackReference = messageReference)
                .ReturnsAsync(userMessageMock.Object);

            var replyReference = new MessageReference(77777UL, 88888UL, null, false, default);
            var mediaAssets = new List<MediaAsset>
            {
                new() { Filename = "file.txt", Bytes = [1, 2, 3] }
            };

            var result = await publisher.SendMessageWithFilesAsync(channelMock.Object, "hello", mediaAssets, null, replyReference);

            Assert.Same(userMessageMock.Object, result);
            Assert.NotNull(capturedFallbackReference);
            Assert.Equal(77777UL, capturedFallbackReference!.MessageId.Value);
            Assert.Equal(88888UL, capturedFallbackReference.ChannelId);
        }

        [Fact]
        public async Task TryModifyMessageContentAsync_Modifies_UserMessage()
        {
            var publisher = CreatePublisher();
            var channelMock = new Mock<IMessageChannel>();
            var userMessageMock = new Mock<IUserMessage>();

            string? capturedContent = null;

            userMessageMock
                .Setup(message => message.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions?>()))
                .Callback<Action<MessageProperties>, RequestOptions?>((action, _) =>
                {
                    var properties = new MessageProperties();
                    action(properties);
                    capturedContent = properties.Content.IsSpecified ? properties.Content.Value : null;
                })
                .Returns(Task.CompletedTask);

            channelMock
                .Setup(channel => channel.GetMessageAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions?>()))
                .ReturnsAsync(userMessageMock.Object);

            var result = await publisher.TryModifyMessageContentAsync(channelMock.Object, 12345UL, "new content");

            Assert.True(result);
            Assert.Equal("new content", capturedContent);
        }

        [Fact]
        public async Task TryModifyMessageContentAsync_ReturnsFalse_WhenFetchedMessageIsNotUserMessage()
        {
            var publisher = CreatePublisher();
            var channelMock = new Mock<IMessageChannel>();
            var messageMock = new Mock<IMessage>();

            channelMock
                .Setup(channel => channel.GetMessageAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions?>()))
                .ReturnsAsync(messageMock.Object);

            var result = await publisher.TryModifyMessageContentAsync(channelMock.Object, 12345UL, "new content");

            Assert.False(result);
        }

        [Fact]
        public async Task DownloadMediaAssetsAsync_ReturnsDownloadedBytes()
        {
            var handler = new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                });
            var httpClient = new HttpClient(handler);
            var publisher = CreatePublisher(httpClient);
            var attachmentMock = new Mock<IAttachment>();
            attachmentMock.SetupGet(attachment => attachment.Filename).Returns("file.txt");
            attachmentMock.SetupGet(attachment => attachment.Url).Returns("https://cdn.example/file.txt");

            var result = await publisher.DownloadMediaAssetsAsync([attachmentMock.Object]);

            Assert.Single(result);
            Assert.Equal("file.txt", result[0].Filename);
            Assert.Equal([1, 2, 3], result[0].Bytes);
        }

        [Fact]
        public async Task DownloadMediaAssetsAsync_PreservesAttachmentOrder_WhenRequestsCompleteOutOfOrder()
        {
            var handler = new DelayedStubHttpMessageHandler(async (request, cancellationToken) =>
            {
                if (request.RequestUri!.AbsoluteUri.EndsWith("slow", StringComparison.Ordinal))
                {
                    await Task.Delay(50, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent([1])
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([2])
                };
            });
            var publisher = CreatePublisher(new HttpClient(handler));
            var firstAttachment = new Mock<IAttachment>();
            firstAttachment.SetupGet(attachment => attachment.Filename).Returns("first.txt");
            firstAttachment.SetupGet(attachment => attachment.Url).Returns("https://cdn.example/slow");
            var secondAttachment = new Mock<IAttachment>();
            secondAttachment.SetupGet(attachment => attachment.Filename).Returns("second.txt");
            secondAttachment.SetupGet(attachment => attachment.Url).Returns("https://cdn.example/fast");

            var result = await publisher.DownloadMediaAssetsAsync([firstAttachment.Object, secondAttachment.Object]);

            Assert.Equal(2, result.Count);
            Assert.Equal("first.txt", result[0].Filename);
            Assert.Equal([1], result[0].Bytes);
            Assert.Equal("second.txt", result[1].Filename);
            Assert.Equal([2], result[1].Bytes);
        }

        [Fact]
        public async Task SendMessageWithFilesAsync_ReturnsNull_WhenFallbackSendFails()
        {
            var publisher = CreatePublisher();
            var channelMock = new Mock<IMessageChannel>();

            channelMock
                .Setup(channel => channel.SendFilesAsync(
                    It.IsAny<IEnumerable<FileAttachment>>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .ThrowsAsync(new InvalidOperationException("upload failed"));

            channelMock
                .Setup(channel => channel.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .ThrowsAsync(new InvalidOperationException("fallback failed"));

            var mediaAssets = new List<MediaAsset>
            {
                new() { Filename = "file.txt", Bytes = [1, 2, 3] }
            };

            var result = await publisher.SendMessageWithFilesAsync(channelMock.Object, "hello", mediaAssets, null);

            Assert.Null(result);
        }

        private static DiscordMessagePublisherService CreatePublisher(HttpClient? httpClient = null)
        {
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock
                .Setup(factory => factory.CreateClient(HttpClientNames.DiscordCdn))
                .Returns(httpClient ?? new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

            return new DiscordMessagePublisherService(
                httpClientFactoryMock.Object,
                new PassThroughDiscordRestRetryPolicy(),
                new OptionsWrapper<DiscordRestOptions>(new DiscordRestOptions()),
                NullLogger<DiscordMessagePublisherService>.Instance);
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            {
                _responseFactory = responseFactory;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_responseFactory(request));
            }
        }

        private sealed class DelayedStubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

            public DelayedStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
            {
                _responseFactory = responseFactory;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _responseFactory(request, cancellationToken);
            }
        }
    }
}
