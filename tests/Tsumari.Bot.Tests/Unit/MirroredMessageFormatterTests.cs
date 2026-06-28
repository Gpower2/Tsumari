using Discord;
using Moq;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class MirroredMessageFormatterTests
    {
        [Fact]
        public void FormatLanguagePair_Returns_Correct_Format()
        {
            var result = MirroredMessageFormatter.FormatLanguagePair("en", "de");
            Assert.Equal("(EN to DE)", result);
            result = MirroredMessageFormatter.FormatLanguagePair("EN-US", "el");
            Assert.Equal("(EN-US to EL)", result);
            result = MirroredMessageFormatter.FormatLanguagePair("pt_br", "en-us");
            Assert.Equal("(PT-BR to EN-US)", result);
        }

        [Fact]
        public void BuildJumpUrl_ReturnsGuildUrl_ForGuildMessages()
        {
            var channelMock = new Mock<IMessageChannel>();
            channelMock.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(456UL);
            channelMock.As<IGuildChannel>().SetupGet(channel => channel.GuildId).Returns(123UL);

            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(789UL);
            messageMock.SetupGet(message => message.Channel).Returns(channelMock.Object);

            var result = MirroredMessageFormatter.BuildJumpUrl(messageMock.Object);

            Assert.Equal("https://discord.com/channels/123/456/789", result);
        }

        [Fact]
        public void BuildJumpUrl_ReturnsDirectMessageUrl_ForNonGuildMessages()
        {
            var channelMock = new Mock<IMessageChannel>();
            channelMock.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(456UL);

            var messageMock = new Mock<IMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(789UL);
            messageMock.SetupGet(message => message.Channel).Returns(channelMock.Object);

            var result = MirroredMessageFormatter.BuildJumpUrl(messageMock.Object);

            Assert.Equal("https://discord.com/channels/@me/456/789", result);
        }

        [Fact]
        public void ResolveAuthorDisplayName_PrefersNickname_WhenAvailable()
        {
            var guildUserMock = new Mock<IGuildUser>();
            guildUserMock.SetupGet(user => user.Username).Returns("alice");
            guildUserMock.SetupGet(user => user.Nickname).Returns("Alice");
            guildUserMock.SetupGet(user => user.GlobalName).Returns((string)null!);

            var result = MirroredMessageFormatter.ResolveAuthorDisplayName(guildUserMock.Object);

            Assert.Equal("Alice", result);
        }

        [Fact]
        public void FormatTranslatedReplyText_ReturnsCompactReplyFormat()
        {
            var result = MirroredMessageFormatter.FormatTranslatedReplyText("en", "de", "Hallo");

            Assert.Equal("*(EN to DE):* Hallo", result);
        }

        [Fact]
        public void FormatMirroredAuthorText_OmitsBlankLine_WhenContentIsEmpty()
        {
            var result = MirroredMessageFormatter.FormatMirroredAuthorText("Alice", string.Empty);

            Assert.Equal("**Alice**:", result);
        }

        [Fact]
        public void ResolveLinkedMessageTargetLanguageCode_ReturnsNull_ForMasterLinks()
        {
            var result = MirroredMessageFormatter.ResolveLinkedMessageTargetLanguageCode("master", null);

            Assert.Null(result);
        }

        [Fact]
        public void ResolveLinkedMessageTargetLanguageCode_FallsBackToStoredLanguage_WhenChannelConfigIsMissing()
        {
            var result = MirroredMessageFormatter.ResolveLinkedMessageTargetLanguageCode("pt-br", null);

            Assert.Equal("PT-BR", result);
        }

        [Fact]
        public void ResolveLinkedMessageTargetLanguageCode_PrefersCurrentChannelConfiguration_WhenAvailable()
        {
            var result = MirroredMessageFormatter.ResolveLinkedMessageTargetLanguageCode("pt", "pt-br");

            Assert.Equal("PT-BR", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_ReturnsTranslatedCrossChannelFormat()
        {
            var result = MirroredMessageFormatter.FormatEditedLinkedMessageText(10, 20, "Alice", "en", "de", "Hallo");

            Assert.Equal("**Alice** (EN to DE):\nHallo", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_ReturnsTranslatedReplyFormat_ForSourceChannelReply()
        {
            var result = MirroredMessageFormatter.FormatEditedLinkedMessageText(10, 10, "Alice", "en", "de", "Hallo");

            Assert.Equal("*(EN to DE):* Hallo", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_ReturnsRawFormat_WhenEditedSourceNowMatchesTargetLanguage()
        {
            var result = MirroredMessageFormatter.FormatEditedLinkedMessageText(10, 20, "Alice", "de", "de", "Hallo");

            Assert.Equal("**Alice**:\nHallo", result);
        }

        [Fact]
        public void FormatEditedLinkedMessageText_SwitchesReplyToRaw_WhenEditedSourceNowMatchesSourceChannelLanguage()
        {
            var result = MirroredMessageFormatter.FormatEditedLinkedMessageText(10, 10, "Alice", "de", "de", "Hallo");

            Assert.Equal("**Alice**:\nHallo", result);
        }

        [Fact]
        public void FormatTranslationFailureText_UsesReplyFormat_ForSourceChannelReply()
        {
            var result = MirroredMessageFormatter.FormatTranslationFailureText(10, 10, "Alice", "en", "de", "Hello");

            Assert.Equal("*(EN to DE):* Hello *(Translation Failed)*", result);
        }
    }
}
