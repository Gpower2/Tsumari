using System.Text.Json.Nodes;
using Tsumari.Bot.Services;
using Tsumari.Repair;

public class RepairRulesTests
{
    [Fact]
    public void UnmarkedCopiesRequireExplicitReviewAndStillRequireExactSource()
    {
        Assert.Null(RepairRules.Replacement("**A**:\nBuongiorno", "Buongiorno", "en", "Good morning", false));
        Assert.Equal("**A**:\nGood morning", RepairRules.Replacement("**A**:\nBuongiorno", "Buongiorno", "en", "Good morning", false, true));
        Assert.Null(RepairRules.Replacement("**A**:\nBuongiorno", "edited", "en", "Good morning", false, true));
    }

    [Theory]
    [InlineData("de", "en", true)]
    [InlineData(null, "en", true)]
    [InlineData("de", "de", false)]
    public void FlagsProviderEchoesForLanguageReview(string? sourceLanguage, string target, bool requiresReview) =>
        Assert.Equal(requiresReview, RepairRules.RequiresEchoReview("guuuten moooorgen", "guuuten moooorgen", sourceLanguage, target));

    [Fact]
    public void RepairsExactFailureAndPreservesTimestampAuthorAndNotice()
    {
        var suffix = "\n" + MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice("de");
        var before = "<t:1789990000:f> **A Person**:\nhello\nworld" + RepairRules.Marker + suffix;
        Assert.Equal("<t:1789990000:f> **A Person**:\nHallo Welt" + suffix,
            RepairRules.Replacement(before, "hello\nworld", "de", "Hallo Welt", false));
    }
    [Theory]
    [InlineData("**A**:\nhello *(Translation Failed)*", "changed source")]
    [InlineData("**A**:\nI saw *(Translation Failed)* yesterday", "I saw")]
    [InlineData("hello *(Translation Failed)*", "hello")]
    [InlineData("**A**:\nhello *(Translation Failed)* unknown suffix", "hello")]
    public void RejectsChangedSourcesQuotationsAndUnknownFormats(string before, string source) =>
        Assert.Null(RepairRules.Replacement(before, source, "de", "Hallo", false));

    [Fact]
    public void HandlesSameChannelReplyWithoutReposting() =>
        Assert.Equal("*(EN => DE):* Hallo", RepairRules.Replacement("*(EN => DE):* hello" + RepairRules.Marker, "hello", "de", "Hallo", true));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*(Translation Failed)*")]
    public void RejectsInvalidProviderOutput(string translation) =>
        Assert.Null(RepairRules.Replacement("**A**:\nhello" + RepairRules.Marker, "hello", "de", translation, false));

    [Fact]
    public void RejectsOversizedTranslation() =>
        Assert.Null(RepairRules.Replacement("**A**:\nhello" + RepairRules.Marker, "hello", "de", new string('a', 2000), false));

    [Fact]
    public void ConvertsMirrorSnowflakeAndAthensBoundary()
    {
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T09:14:42.100Z"), RepairRules.Created("1551522047879028736"));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Athens");
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T21:00:00Z"), RepairRules.LocalBoundary("2026-09-21", zone));
        Assert.Equal(DateTimeOffset.Parse("2026-01-20T22:00:00Z"), RepairRules.LocalBoundary("2026-01-21", zone));
    }

    [Fact]
    public void RejectsAmbiguousAndNonexistentLocalTimes()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Athens");
        Assert.Throws<ArgumentException>(() => RepairRules.LocalBoundary("2026-10-25 03:30", zone));
        Assert.Throws<ArgumentException>(() => RepairRules.LocalBoundary("2026-03-29 03:30", zone));
    }

    [Fact]
    public void DetectsConcurrentEditsButIgnoresReactions()
    {
        var before = JsonNode.Parse("""{"id":"1","content":"hello","edited_timestamp":null,"reactions":[]}""")!.AsObject();
        var after = before.DeepClone().AsObject();
        after["reactions"] = new JsonArray(new JsonObject { ["count"] = 1 });
        Assert.Equal(RepairRules.Fingerprint(before), RepairRules.Fingerprint(after));
        after["edited_timestamp"] = "2026-09-21T10:00:00Z";
        Assert.NotEqual(RepairRules.Fingerprint(before), RepairRules.Fingerprint(after));
    }

    [Fact]
    public void VerificationRequiresAttachmentsComponentsReferenceAndCreationTimePreserved()
    {
        var before = JsonNode.Parse("""{"id":"1","channel_id":"2","content":"old","timestamp":"2026-01-01","attachments":[{"id":"3"}],"components":[],"message_reference":{"message_id":"4"}}""")!.AsObject();
        var after = before.DeepClone().AsObject();
        after["content"] = "new";
        Assert.True(RepairRules.Preserved(before, after));
        after["attachments"] = new JsonArray();
        Assert.False(RepairRules.Preserved(before, after));
    }

    [Fact]
    public void ResolvesOriginalButtonWithoutTrustingOtherUrls()
    {
        var message = JsonNode.Parse("""{"components":[{"components":[{"label":"Original","url":"https://discord.com/channels/1/2/3"}]}]}""")!.AsObject();
        Assert.Equal(("1", "2", "3"), RepairRules.OriginalLink(message));
    }

    [Fact]
    public async Task ReadOnlyClientCannotEdit()
    {
        using var client = new DiscordApi("fake", false, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Edit("1", "2", "new"));
    }
}
