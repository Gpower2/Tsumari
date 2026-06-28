using Discord;
using Microsoft.Extensions.Logging;
using Moq;
using Tsumari.Bot.Services;
using Tsumari.Bot.Tests;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class MirroredMessageRoutingServiceTests
    {
        [Fact]
        public async Task HandleMessageReceivedAsync_LogsDebug_WhenPayloadIsNotUserMessage()
        {
            var logger = new ListLogger<MirroredMessageRoutingService>();
            var service = CreateService(logger);
            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(12345UL);

            await service.HandleMessageReceivedAsync(messageMock.Object);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1109
                    && entry.Message.Contains("12345"));
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_LogsDebug_WhenMessageSourceIsNotUser()
        {
            var logger = new ListLogger<MirroredMessageRoutingService>();
            var service = CreateService(logger);
            var messageMock = new Mock<IUserMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(23456UL);
            messageMock.SetupGet(message => message.Source).Returns(MessageSource.System);

            await service.HandleMessageReceivedAsync(messageMock.Object);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1110
                    && entry.Message.Contains("23456")
                    && entry.Message.Contains("System"));
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_LogsDebug_WhenMessageHasNoContentOrAttachments()
        {
            var logger = new ListLogger<MirroredMessageRoutingService>();
            var service = CreateService(logger);
            var channelMock = new Mock<IMessageChannel>();
            channelMock.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(999UL);

            var messageMock = new Mock<IUserMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(34567UL);
            messageMock.SetupGet(message => message.Source).Returns(MessageSource.User);
            messageMock.SetupGet(message => message.Content).Returns(string.Empty);
            messageMock.SetupGet(message => message.Attachments).Returns(Array.Empty<IAttachment>());
            messageMock.SetupGet(message => message.Channel).Returns(channelMock.Object);

            await service.HandleMessageReceivedAsync(messageMock.Object);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1111
                    && entry.Message.Contains("34567")
                    && entry.Message.Contains("999"));
        }

        private static MirroredMessageRoutingService CreateService(ListLogger<MirroredMessageRoutingService> logger)
        {
            return new MirroredMessageRoutingService(
                null!,
                null!,
                null!,
                null!,
                null!,
                logger);
        }
    }
}
