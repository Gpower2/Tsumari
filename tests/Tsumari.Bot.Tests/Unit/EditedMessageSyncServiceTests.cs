using Discord;
using Microsoft.Extensions.Logging;
using Moq;
using Tsumari.Bot.Services;
using Tsumari.Bot.Tests;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class EditedMessageSyncServiceTests
    {
        [Fact]
        public void ShouldProcessEditedMessage_ReturnsFalse_WhenCachedContentMatches()
        {
            var result = EditedMessageSyncService.ShouldProcessEditedMessage(true, "hello", "hello");

            Assert.False(result);
        }

        [Fact]
        public void ShouldProcessEditedMessage_ReturnsTrue_WhenCachedSnapshotIsMissing()
        {
            var result = EditedMessageSyncService.ShouldProcessEditedMessage(false, "hello", "hello");

            Assert.True(result);
        }

        [Fact]
        public void ShouldProcessEditedMessage_ReturnsTrue_WhenContentChanged()
        {
            var result = EditedMessageSyncService.ShouldProcessEditedMessage(true, "before", "after");

            Assert.True(result);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_LogsDebug_WhenPayloadIsNotUserMessage()
        {
            var logger = new ListLogger<EditedMessageSyncService>();
            var service = CreateService(logger);
            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(12345UL);

            await service.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "before", messageMock.Object);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1206
                    && entry.Message.Contains("12345"));
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_LogsDebug_WhenMessageIsFromBot()
        {
            var logger = new ListLogger<EditedMessageSyncService>();
            var service = CreateService(logger);
            var authorMock = new Mock<IUser>();
            authorMock.SetupGet(author => author.IsBot).Returns(true);

            var messageMock = new Mock<IUserMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(23456UL);
            messageMock.SetupGet(message => message.Author).Returns(authorMock.Object);
            messageMock.SetupGet(message => message.Source).Returns(MessageSource.User);

            await service.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "before", messageMock.Object);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1207
                    && entry.Message.Contains("23456"));
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_LogsDebug_WhenContentIsUnchanged()
        {
            var logger = new ListLogger<EditedMessageSyncService>();
            var service = CreateService(logger);
            var authorMock = new Mock<IUser>();
            authorMock.SetupGet(author => author.IsBot).Returns(false);

            var messageMock = new Mock<IUserMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(34567UL);
            messageMock.SetupGet(message => message.Author).Returns(authorMock.Object);
            messageMock.SetupGet(message => message.Source).Returns(MessageSource.User);
            messageMock.SetupGet(message => message.Content).Returns("same");

            await service.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "same", messageMock.Object);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Debug
                    && entry.EventId.Id == 1208
                    && entry.Message.Contains("34567"));
        }

        private static EditedMessageSyncService CreateService(ListLogger<EditedMessageSyncService> logger)
        {
            return new EditedMessageSyncService(
                null!,
                null!,
                null!,
                null!,
                logger);
        }
    }
}
