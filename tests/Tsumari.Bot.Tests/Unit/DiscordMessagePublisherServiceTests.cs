using System.Net;
using System.Net.Http;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class DiscordMessagePublisherServiceTests
    {
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
                .Setup(channel => channel.GetMessageAsync(It.IsAny<ulong>()))
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
                .Setup(channel => channel.GetMessageAsync(It.IsAny<ulong>()))
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
    }
}
