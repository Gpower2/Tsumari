using System;
using System.Threading.Tasks;
using Discord;
using Moq;
using Tsumari.Bot;
using Xunit;

namespace Tsumari.Bot.Tests
{
    public class WorkerEditTests
    {
        [Fact]
        public void FormatLanguagePair_Returns_Correct_Format()
        {
            var result = Worker.FormatLanguagePair("en", "de");
            Assert.Equal("(EN to DE)", result);
            result = Worker.FormatLanguagePair("EN-US", "el");
            Assert.Equal("(EN-US to EL)", result);
            result = Worker.FormatLanguagePair("pt_br", "en-us");
            Assert.Equal("(PT-BR to EN-US)", result);
        }

        [Fact]
        public async Task TryModifyMessageContentAsync_Modifies_UserMessage()
        {
            var channelMock = new Mock<IMessageChannel>();
            var userMsgMock = new Mock<IUserMessage>();

            string? capturedContent = null;

            userMsgMock
                .Setup(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions?>()))
                .Callback<Action<MessageProperties>, RequestOptions?>((action, opts) =>
                {
                    var props = new MessageProperties();
                    action(props);
                    capturedContent = props.Content.IsSpecified ? props.Content.Value : null;
                })
                .Returns(Task.CompletedTask);

            channelMock
                .Setup(c => c.GetMessageAsync(It.IsAny<ulong>()))
                .ReturnsAsync(userMsgMock.Object);

            var result = await Worker.TryModifyMessageContentAsync(channelMock.Object, 12345UL, "new content");

            Assert.True(result);
            Assert.Equal("new content", capturedContent);
        }
    }
}
