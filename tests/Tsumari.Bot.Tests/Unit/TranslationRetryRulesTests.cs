using Discord;
using Tsumari.Bot.Configuration;

namespace Tsumari.Bot.Tests.Unit;

public sealed class TranslationRetryRulesTests
{
    [Theory]
    [InlineData("<t:123:f> **Alice**:\nHello *(Translation Failed)*", "<t:123:f> **Alice**:\nBonjour")]
    [InlineData("**Alice** (EN => FR):\nHello", "**Alice** (EN => FR):\nBonjour")]
    [InlineData("<t:123:f> *(EN => FR):* Hello", "<t:123:f> *(EN => FR):* Bonjour")]
    public void ReplacementPreservesWrapper(string before, string expected) => Assert.Equal(expected, TranslationRetryRules.ReplaceBody(before, "fr", "Bonjour"));

    [Fact]
    public void AttachmentNoticeIsPreserved()
    {
        var notice = "\n" + MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice("fr");
        Assert.Equal("**Alice**:\nBonjour" + notice, TranslationRetryRules.ReplaceBody("**Alice**:\nHello" + notice, "fr", "Bonjour"));
    }

    [Theory]
    [InlineData("arbitrary user message")]
    [InlineData("**Alice** (EN => DE):\nHello")]
    public void UnknownOrWrongLanguageWrapperIsRejected(string before) => Assert.Throws<InvalidOperationException>(() => TranslationRetryRules.ReplaceBody(before, "fr", "Bonjour"));

    [Theory]
    [InlineData("https://evil.test/channels/1/2/3")]
    [InlineData("https://discord.com/channels/1/2/3/4")]
    [InlineData("https://discord.com/channels/1/2/18446744073709551616")]
    public void InvalidJumpLinkIsRejected(string link) => Assert.False(TranslationRetryRules.TryParseJumpLink(link, out _, out _, out _));

    [Fact]
    public void JumpLinkIsParsed()
    {
        Assert.True(TranslationRetryRules.TryParseJumpLink("https://discord.com/channels/1/2/3", out var guild, out var channel, out var message));
        Assert.Equal((1UL, 2UL, 3UL), (guild, channel, message));
    }

    [Fact]
    public void OptionsEnforceBoundedMonotonicSchedule()
    {
        new TranslationRetryOptions().Validate();
        Assert.Throws<ArgumentException>(() => new TranslationRetryOptions { Temperatures = [.3, .2] }.Validate());
        Assert.Throws<ArgumentException>(() => new TranslationRetryOptions { Temperatures = [double.NaN] }.Validate());
        Assert.Throws<ArgumentException>(() => new TranslationRetryOptions { TopP = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new TranslationRetryOptions { QueueCapacity = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new TranslationRetryOptions { Temperatures = [.7] }.Validate());
    }

    [Fact]
    public void FingerprintAndMetadataIncludeNestedButtons()
    {
        var message = new Mock<IUserMessage>();
        message.SetupGet(m => m.Channel).Returns(Mock.Of<IMessageChannel>());
        message.SetupGet(m => m.Attachments).Returns(Array.Empty<IAttachment>());
        message.SetupGet(m => m.Components).Returns(new ComponentBuilder().WithButton("Original", style: ButtonStyle.Link, url: "https://discord.com/channels/1/2/3").Build().Components);
        var fingerprint = TranslationRetryRules.Fingerprint(message.Object);
        var metadata = TranslationRetryRules.PreservedMetadata(message.Object);
        message.SetupGet(m => m.Components).Returns(new ComponentBuilder().WithButton("Original", style: ButtonStyle.Link, url: "https://discord.com/channels/1/2/4").Build().Components);
        Assert.NotEqual(fingerprint, TranslationRetryRules.Fingerprint(message.Object));
        Assert.NotEqual(metadata, TranslationRetryRules.PreservedMetadata(message.Object));
        message.SetupGet(m => m.Components).Returns(new ComponentBuilder().WithButton("↻", customId: TranslationRetryRules.ButtonId).Build().Components);
        Assert.NotEqual(fingerprint, TranslationRetryRules.Fingerprint(message.Object));
    }
}
