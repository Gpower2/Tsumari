using System.Reflection;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tsumari.Bot.Configuration;

namespace Tsumari.Bot.Tests.Component;

public sealed class TranslationRetryServiceTests
{
    [Fact]
    public async Task Retry_UsesOriginalAndConfiguredProfile_EditsOnlyContent_VerifiesAndSurvivesRestart()
    {
        using var h = await Harness.Create();
        var beforeMetadata = TranslationRetryRules.PreservedMetadata(h.Mirror.Object);
        var result = await h.Run();
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Attempt);
        Assert.Equal("<t:123:f> **Alice**:\nBonjour", h.Content);
        Assert.Equal(beforeMetadata, TranslationRetryRules.PreservedMetadata(h.Mirror.Object));
        Assert.Equal(1, TranslationRetryRules.ReadCount(h.Mirror.Object));
        Assert.Single(h.Profiles);
        Assert.Equal(new TranslationGenerationOptions(.15, .8), h.Profiles[0]);
        Assert.NotNull(h.Edit);
        Assert.False(h.Edit!.Attachments.IsSpecified);
        Assert.False(h.Edit.Components.IsSpecified);
        Assert.True(h.Edit.AllowedMentions.IsSpecified);
        h.Service = h.NewService();
        Assert.True((await h.Run()).Success);
        Assert.Equal(new TranslationGenerationOptions(.25, .8), h.Profiles[1]);
        Assert.Equal(2, TranslationRetryRules.ReadCount(h.Mirror.Object));
        Assert.Single(h.Reactions);
        Assert.Single(await h.Db.GetMirroredMessagesAsync(100));
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("")]
    [InlineData(" *(Translation Failed)*")]
    public async Task InvalidOutput_PreservesMessageAndRecordsAttempt(string output)
    {
        using var h = await Harness.Create();
        h.Output = output;
        var before = h.Content;
        var result = await h.Run();
        Assert.False(result.Success);
        Assert.Equal(1, result.Attempt);
        Assert.Equal(before, h.Content);
        Assert.Null(h.Edit);
    }

    [Fact]
    public async Task OversizedOutput_IsNotApplied()
    {
        using var h = await Harness.Create();
        h.Output = new string('x', 2001);
        Assert.False((await h.Run()).Success);
        Assert.Null(h.Edit);
    }

    [Fact]
    public async Task SameLanguageEcho_IsAllowed()
    {
        using var h = await Harness.Create();
        h.Provider.Setup(p => p.AnalyzeLanguageAsync(It.IsAny<string>())).ReturnsAsync(LanguageAnalysisResult.SingleLanguage("FR"));
        h.Output = "Hello";
        Assert.True((await h.Run()).Success);
    }

    [Fact]
    public async Task ProviderTransportRetries_KeepOneCounterAndSameProfile()
    {
        using var h = await Harness.Create();
        var attempts = 0;
        h.Provider.Setup(p => p.TranslateWithOptionsAsync("Hello", "FR", "EN", It.IsAny<TranslationGenerationOptions>()))
            .Returns(() => ++attempts < 2 ? Task.FromException<string>(new HttpRequestException("offline")) : Task.FromResult("Bonjour"));
        Assert.True((await h.Run()).Success);
        Assert.Equal(2, attempts);
        Assert.Equal(1, TranslationRetryRules.ReadCount(h.Mirror.Object));
    }

    [Fact]
    public async Task AnalysisFailure_DoesNotCountOrFallbackToEnglish()
    {
        using var h = await Harness.Create();
        h.Provider.Setup(p => p.AnalyzeLanguageAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("offline"));
        Assert.False((await h.Run()).Success);
        Assert.Empty(h.Reactions);
        Assert.Empty(h.Profiles);
        Assert.Null(h.Edit);
    }

    [Fact]
    public async Task CounterLimit_UsesHighestBotOwnedEmoji_IgnoresHumanCount()
    {
        using var h = await Harness.Create();
        h.Reactions[new Emoji("1️⃣")] = Metadata(true, 99);
        h.Reactions[new Emoji("🔟")] = Metadata(false, 42);
        Assert.Equal(1, TranslationRetryRules.ReadCount(h.Mirror.Object));
        h.Reactions[new Emoji("🔟")] = Metadata(true, 42);
        Assert.False((await h.Run()).Success);
        Assert.Empty(h.Profiles);
        Assert.Null(h.Edit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentSourceOrMirrorChange_PreventsOverwrite(bool source)
    {
        using var h = await Harness.Create();
        h.OnTranslate = () => { if (source) h.SourceContent = "new source"; else h.Content = "new translation"; };
        var result = await h.Run();
        Assert.False(result.Success);
        Assert.Contains("changed", result.Message);
        Assert.Null(h.Edit);
    }

    [Fact]
    public async Task DuplicateClicksAndFullQueue_DoNotStartExtraRequests()
    {
        using var h = await Harness.Create();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.Setup(p => p.TranslateWithOptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TranslationGenerationOptions>()))
            .Returns(() => { started.SetResult(); return release.Task; });
        var first = h.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Contains("already", (await h.Run()).Message);
            await h.Db.LinkMessagesAsync(101, 10, 201, 20, "FR");
            h.Discord.Setup(d => d.GetFreshMessageAsync(10, 101)).ReturnsAsync(h.Source.Object);
            Assert.Contains("queue", (await h.Service.RetryAsync(1, 20, 201, h.User.Object)).Message);
        }
        finally { release.TrySetResult("Bonjour"); }
        Assert.True((await first).Success);
    }

    [Fact]
    public async Task CooldownAndAuthorization_AreEnforced()
    {
        using var h = await Harness.Create();
        h.Options.CooldownSeconds = 30;
        h.Service = h.NewService();
        h.User.SetupGet(u => u.Id).Returns(999);
        Assert.Contains("Only", (await h.Run()).Message);
        Assert.Empty(h.Reactions);
        h.User.SetupGet(u => u.Id).Returns(7);
        Assert.True((await h.Run()).Success);
        Assert.Contains("cooldown", (await h.Run()).Message);
    }

    [Fact]
    public async Task RoleCanRetry_ButCannotBypassSourceVisibility()
    {
        using var h = await Harness.Create();
        h.Options.RoleId = 123;
        h.Service = h.NewService();
        h.User.SetupGet(u => u.Id).Returns(999);
        h.User.SetupGet(u => u.RoleIds).Returns(new ulong[] {123});
        h.User.Setup(u => u.GetPermissions(h.SourceChannel.Object)).Returns(ChannelPermissions.None);
        Assert.Contains("access", (await h.Run()).Message);
        Assert.Empty(h.Reactions);
        h.User.Setup(u => u.GetPermissions(h.SourceChannel.Object)).Returns(new ChannelPermissions(viewChannel: true, readMessageHistory: true));
        Assert.True((await h.Run()).Success);
    }

    [Fact]
    public async Task MissingAndMasterCopies_AreNeverRecreated()
    {
        using var h = await Harness.Create();
        h.Discord.Setup(d => d.GetFreshMessageAsync(20, 200)).ReturnsAsync((IMessage?)null);
        Assert.False((await h.Run()).Success);
        await h.Db.LinkMessagesAsync(100, 10, 300, 30, "MASTER");
        h.Discord.Setup(d => d.GetChannelAsync(30)).ReturnsAsync(h.TargetChannel.Object);
        Assert.False((await h.Service.RetryAsync(1, 30, 300, h.User.Object)).Success);
        h.Discord.Verify(d => d.SendMessageAsync(It.IsAny<ulong>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UncertainPatch_IsNotBlindlyRetried()
    {
        using var h = await Harness.Create();
        h.Mirror.Setup(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()))
            .Callback<Action<MessageProperties>, RequestOptions>((_, options) =>
            {
                Assert.NotNull(options);
                Assert.Equal(RetryMode.RetryRatelimit, options.RetryMode);
                Assert.Equal(10000, options.Timeout);
            })
            .ThrowsAsync(new HttpRequestException("lost response"));
        var result = await h.Run();
        Assert.False(result.Success);
        Assert.Contains("may have reached", result.Message);
        h.Mirror.Verify(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()), Times.Once);
    }

    [Fact]
    public async Task ChangedButtonsWithinExistingRow_PreventRetryCommit()
    {
        using var h = await Harness.Create();
        h.OnTranslate = () => h.Mirror.SetupGet(m => m.Components).Returns(
            new ComponentBuilder().WithButton("Changed", style: ButtonStyle.Link, url: "https://discord.com/channels/1/10/101").Build().Components);
        var result = await h.Run();
        Assert.False(result.Success);
        Assert.Contains("changed", result.Message);
        Assert.Null(h.Edit);
    }

    [Fact]
    public async Task SourceEditServiceAndRetry_ShareCommitLockAndKeepNewSourceTranslation()
    {
        using var h = await Harness.Create();
        h.Source.SetupGet(m => m.Source).Returns(MessageSource.User);
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerRelease = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var editStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var editRelease = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.Setup(p => p.TranslateWithOptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TranslationGenerationOptions>()))
            .Returns(() => { providerStarted.SetResult(); return providerRelease.Task; });
        h.Provider.Setup(p => p.TranslateTextAsync("new source", "FR", "EN"))
            .Returns(() => { editStarted.SetResult(); return editRelease.Task; });
        h.TargetChannel.Setup(c => c.GetMessageAsync(200, It.IsAny<CacheMode>(), It.IsAny<RequestOptions>())).ReturnsAsync(h.Mirror.Object);
        var translationService = new TranslationService(h.Db, h.Provider.Object, NullLogger<TranslationService>.Instance, NullLoggerFactory.Instance);
        var publisher = new DiscordMessagePublisherService(Mock.Of<IHttpClientFactory>(), new PassThroughDiscordRestRetryPolicy(),
            Microsoft.Extensions.Options.Options.Create(new DiscordRestOptions()), NullLogger<DiscordMessagePublisherService>.Instance);
        var editor = new EditedMessageSyncService(h.Db, translationService, h.Discord.Object, publisher,
            NullLogger<EditedMessageSyncService>.Instance, h.Mutations);
        var retry = h.Run();
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.SourceContent = "new source";
        var edit = editor.HandleMessageUpdatedAsync(true, "Hello", h.Source.Object);
        try
        {
            await editStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            providerRelease.SetResult("Bonjour");
            // Edit holds the shared family lock; the retry may generate but cannot commit.
            Assert.False(retry.IsCompleted);
            editRelease.SetResult("nouvelle source");
            await edit.WaitAsync(TimeSpan.FromSeconds(5));
            var result = await retry.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(result.Success);
            Assert.Contains("changed", result.Message);
            Assert.Contains("nouvelle source", h.Content);
            h.Mirror.Verify(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()), Times.Once);
        }
        finally
        {
            providerRelease.TrySetResult("Bonjour");
            editRelease.TrySetResult("nouvelle source");
            await Task.WhenAll(retry, edit);
        }
    }

    [Fact]
    public async Task ReservedCounters_AreNotMirroredOrRemovedOnClear()
    {
        using var h = await Harness.Create();
        h.Reactions[new Emoji("2️⃣")] = Metadata(true, 1);
        var service = new ReactionMirroringService(h.Discord.Object, h.Db, NullLogger<ReactionMirroringService>.Instance);
        await service.MirrorReactionAddedAsync(200, 20, new Emoji("2️⃣"), ReactionType.Normal, 7);
        await service.MirrorReactionRemovedAsync(200, 20, new Emoji("2️⃣"), ReactionType.Normal, 7);
        await service.MirrorReactionsRemovedForEmoteAsync(200, 20, new Emoji("2️⃣"));
        await service.MirrorReactionsClearedAsync(200, 20);
        h.Discord.Verify(d => d.AddReactionAsync(It.IsAny<IMessage>(), It.IsAny<IEmote>()), Times.Never);
        h.Discord.Verify(d => d.RemoveOwnReactionAsync(It.IsAny<IMessage>(), It.IsAny<IEmote>()), Times.Never);
    }

    internal static ReactionMetadata Metadata(bool me, int count)
    {
        object boxed = default(ReactionMetadata);
        foreach (var pair in new Dictionary<string, object> { ["IsMe"] = me, ["NormalCount"] = count, ["ReactionCount"] = count })
            typeof(ReactionMetadata).GetField($"<{pair.Key}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(boxed, pair.Value);
        return (ReactionMetadata)boxed;
    }

    private sealed class Harness : IDisposable
    {
        private readonly TemporarySqliteDatabase _database = new("retry-integration");
        public DatabaseService Db = null!;
        public Mock<IDiscordMessageService> Discord = new();
        public Mock<ITranslationProvider> Provider = new();
        public Mock<IGuildUser> User = new();
        public Mock<ITextChannel> SourceChannel = new(), TargetChannel = new();
        public Mock<IUserMessage> Source = new(), Mirror = new();
        public TranslationRetryOptions Options = new() { CooldownSeconds = 0, QueueCapacity = 1 };
        public TranslationRetryService Service = null!;
        public MessageMutationCoordinator Mutations = new();
        public string Content = "<t:123:f> **Alice**:\nHello *(Translation Failed)*", SourceContent = "Hello", Output = "Bonjour";
        public Action? OnTranslate;
        public MessageProperties? Edit;
        public Dictionary<IEmote, ReactionMetadata> Reactions = [];
        public List<TranslationGenerationOptions> Profiles = [];
        public static async Task<Harness> Create()
        {
            var h = new Harness();
            h.Db = h._database.CreateDatabaseService();
            await h.Db.InitializeDatabaseAsync();
            await h.Db.LinkMessagesAsync(100, 10, 200, 20, "FR");
            h.User.SetupGet(u => u.Id).Returns(7);
            h.User.SetupGet(u => u.GuildId).Returns(1);
            h.User.SetupGet(u => u.RoleIds).Returns(Array.Empty<ulong>());
            h.User.Setup(u => u.GetPermissions(It.IsAny<IGuildChannel>())).Returns(new ChannelPermissions(viewChannel: true, readMessageHistory: true));
            h.SourceChannel.SetupGet(c => c.Id).Returns(10);
            h.TargetChannel.SetupGet(c => c.Id).Returns(20);
            h.SourceChannel.SetupGet(c => c.GuildId).Returns(1);
            h.TargetChannel.SetupGet(c => c.GuildId).Returns(1);
            var author = new Mock<IUser>(); author.SetupGet(u => u.Id).Returns(7);
            var bot = new Mock<IUser>(); bot.SetupGet(u => u.Id).Returns(9); bot.SetupGet(u => u.IsBot).Returns(true);
            h.Source.SetupGet(m => m.Id).Returns(100); h.Source.SetupGet(m => m.Author).Returns(author.Object);
            h.Source.SetupGet(m => m.Channel).Returns(h.SourceChannel.Object);
            h.Source.SetupGet(m => m.Content).Returns(() => h.SourceContent);
            h.Source.SetupGet(m => m.Attachments).Returns(Array.Empty<IAttachment>());
            h.Source.SetupGet(m => m.Reactions).Returns(new Dictionary<IEmote, ReactionMetadata>());
            h.Mirror.SetupGet(m => m.Id).Returns(200); h.Mirror.SetupGet(m => m.Author).Returns(bot.Object);
            h.Mirror.SetupGet(m => m.Channel).Returns(h.TargetChannel.Object);
            h.Mirror.SetupGet(m => m.Content).Returns(() => h.Content);
            h.Mirror.SetupGet(m => m.Attachments).Returns(Array.Empty<IAttachment>());
            h.Mirror.SetupGet(m => m.Reactions).Returns(h.Reactions);
            h.Mirror.SetupGet(m => m.Components).Returns(new ComponentBuilder().WithButton("Original", style: ButtonStyle.Link, url: "https://discord.com/channels/1/10/100").Build().Components);
            h.Mirror.Setup(m => m.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()))
                .Callback<Action<MessageProperties>, RequestOptions>((action, _) => { h.Edit = new(); action(h.Edit); h.Content = h.Edit.Content.Value; }).Returns(Task.CompletedTask);
            h.Discord.SetupGet(d => d.CurrentUserId).Returns(9);
            h.Discord.Setup(d => d.GetChannelAsync(10)).ReturnsAsync(h.SourceChannel.Object);
            h.Discord.Setup(d => d.GetChannelAsync(20)).ReturnsAsync(h.TargetChannel.Object);
            h.Discord.Setup(d => d.GetFreshMessageAsync(10, 100)).ReturnsAsync(h.Source.Object);
            h.Discord.Setup(d => d.GetFreshMessageAsync(20, 200)).ReturnsAsync(h.Mirror.Object);
            h.Discord.Setup(d => d.AddReactionAsync(It.IsAny<IMessage>(), It.IsAny<IEmote>()))
                .Callback<IMessage, IEmote>((_, emoji) => h.Reactions[emoji] = Metadata(true, 1)).Returns(Task.CompletedTask);
            h.Discord.Setup(d => d.RemoveOwnReactionAsync(It.IsAny<IMessage>(), It.IsAny<IEmote>()))
                .Callback<IMessage, IEmote>((_, emoji) => h.Reactions.Remove(emoji)).Returns(Task.CompletedTask);
            h.Provider.SetupGet(p => p.IsActive).Returns(true);
            h.Provider.SetupGet(p => p.SupportsGenerationOptions).Returns(true);
            h.Provider.Setup(p => p.AnalyzeLanguageAsync(It.IsAny<string>())).ReturnsAsync(LanguageAnalysisResult.SingleLanguage("EN"));
            h.Provider.Setup(p => p.TranslateWithOptionsAsync(It.IsAny<string>(), "FR", "EN", It.IsAny<TranslationGenerationOptions>()))
                .Returns<string, string, string?, TranslationGenerationOptions>((source, _, _, profile) => { Assert.Equal("Hello", source); h.Profiles.Add(profile); h.OnTranslate?.Invoke(); return Task.FromResult(h.Output); });
            // Same-language case also exercises the ordinary validation path.
            h.Provider.Setup(p => p.TranslateWithOptionsAsync(It.IsAny<string>(), "FR", "FR", It.IsAny<TranslationGenerationOptions>())).Returns(() => Task.FromResult(h.Output));
            h.Service = h.NewService();
            return h;
        }
        public TranslationRetryService NewService() => new(Db, Discord.Object,
            new TranslationService(Db, Provider.Object, NullLogger<TranslationService>.Instance, NullLoggerFactory.Instance),
            Mutations, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<TranslationRetryService>.Instance);
        public Task<TranslationRetryResult> Run() => Service.RetryAsync(1, 20, 200, User.Object);
        public void Dispose() => _database.Dispose();
    }
}
