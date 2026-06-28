using System;
using System.Threading.Tasks;
using Discord;
using Moq;
using Tsumari.Bot;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
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
        public void ShouldProcessEditedMessage_ReturnsFalse_WhenCachedContentMatches()
        {
            var result = Worker.ShouldProcessEditedMessage(true, "hello", "hello");

            Assert.False(result);
        }

        [Fact]
        public void ShouldProcessEditedMessage_ReturnsTrue_WhenCachedSnapshotIsMissing()
        {
            var result = Worker.ShouldProcessEditedMessage(false, "hello", "hello");

            Assert.True(result);
        }

        [Fact]
        public void FormatTranslatedReplyText_ReturnsCompactReplyFormat()
        {
            var result = Worker.FormatTranslatedReplyText("en", "de", "Hallo");

            Assert.Equal("*(EN to DE):* Hallo", result);
        }

        [Fact]
        public void FormatMirroredAuthorText_OmitsBlankLine_WhenContentIsEmpty()
        {
            var result = Worker.FormatMirroredAuthorText("Alice", string.Empty);

            Assert.Equal("**Alice**:", result);
        }

        [Fact]
        public void ResolveLinkedMessageTargetLanguageCode_ReturnsNull_ForMasterLinks()
        {
            var result = Worker.ResolveLinkedMessageTargetLanguageCode("master", null);

            Assert.Null(result);
        }

        [Fact]
        public void ResolveLinkedMessageTargetLanguageCode_FallsBackToStoredLanguage_WhenChannelConfigIsMissing()
        {
            var result = Worker.ResolveLinkedMessageTargetLanguageCode("pt-br", null);

            Assert.Equal("PT-BR", result);
        }

        [Fact]
        public void ResolveLinkedMessageTargetLanguageCode_PrefersCurrentChannelConfiguration_WhenAvailable()
        {
            var result = Worker.ResolveLinkedMessageTargetLanguageCode("pt", "pt-br");

            Assert.Equal("PT-BR", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_ReturnsTranslatedCrossChannelFormat()
        {
            var result = Worker.FormatEditedLinkedMessageText(10, 20, "Alice", "en", "de", "Hallo");

            Assert.Equal("**Alice** (EN to DE):\nHallo", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_ReturnsTranslatedReplyFormat_ForSourceChannelReply()
        {
            var result = Worker.FormatEditedLinkedMessageText(10, 10, "Alice", "en", "de", "Hallo");

            Assert.Equal("*(EN to DE):* Hallo", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_ReturnsRawFormat_WhenEditedSourceNowMatchesTargetLanguage()
        {
            var result = Worker.FormatEditedLinkedMessageText(10, 20, "Alice", "de", "de", "Hallo");

            Assert.Equal("**Alice**:\nHallo", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_SwitchesReplyToRaw_WhenEditedSourceNowMatchesSourceChannelLanguage()
        {
            var result = Worker.FormatEditedLinkedMessageText(10, 10, "Alice", "de", "de", "Hallo");

            Assert.Equal("**Alice**:\nHallo", result);
        }

        [Fact]
        public void FormatTranslationFailureText_UsesReplyFormat_ForSourceChannelReply()
        {
            var result = Worker.FormatTranslationFailureText(10, 10, "Alice", "en", "de", "Hello");

            Assert.Equal("*(EN to DE):* Hello *(Translation Failed)*", result);
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
