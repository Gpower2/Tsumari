using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot.Models;
using Tsumari.Bot.Modules;
using Tsumari.Bot.Services;
using Tsumari.Bot.Services.Abstractions;
using Xunit;

namespace Tsumari.Bot.Tests.Unit
{
    public class InteractionModuleTests
    {
        [Fact]
        public async Task DetectLanguageAsync_RespondsWithAnalysisSummary()
        {
            using var harness = await CreateHarnessAsync();
            harness.ProviderMock
                .Setup(provider => provider.AnalyzeLanguageAsync("Hello fratello"))
                .ReturnsAsync(new LanguageAnalysisResult(
                    "EN",
                    [
                        new DetectedLanguage("EN", 0.80),
                        new DetectedLanguage("IT", 0.20)
                    ],
                    isMixed: true,
                    hasClearDominantLanguage: true));

            await harness.Module.DetectLanguageAsync("Hello fratello");

            harness.InteractionMock.Verify(
                interaction => interaction.DeferAsync(true, It.IsAny<RequestOptions>()),
                Times.Once);
            harness.InteractionMock.Verify(
                interaction => interaction.FollowupAsync(
                    It.IsAny<string>(),
                    It.IsAny<Embed[]>(),
                    It.IsAny<bool>(),
                    true,
                    It.IsAny<AllowedMentions>(),
                    It.IsAny<MessageComponent>(),
                    It.IsAny<Embed>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<PollProperties>(),
                    It.IsAny<MessageFlags>()),
                Times.Once);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("**Dominant:** EN", response, StringComparison.Ordinal);
            Assert.Contains("**Detected:** EN (80%), IT (20%)", response, StringComparison.Ordinal);
            Assert.Contains("**Mixed:** yes", response, StringComparison.Ordinal);
            Assert.Contains("**Clear dominant:** yes", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TranslateAsync_UsesTrustedSourceHintAndFormatsResponse()
        {
            using var harness = await CreateHarnessAsync();
            harness.ProviderMock
                .Setup(provider => provider.AnalyzeLanguageAsync("Si Tasos , sanno essere divertenti"))
                .ReturnsAsync(LanguageAnalysisResult.SingleLanguage("IT"));
            harness.ProviderMock
                .Setup(provider => provider.TranslateTextAsync("Si Tasos , sanno essere divertenti", "en", "IT"))
                .ReturnsAsync("Yes, Tasos, they can be fun.");

            await harness.Module.TranslateAsync("en", "Si Tasos , sanno essere divertenti");

            harness.ProviderMock.Verify(
                provider => provider.TranslateTextAsync("Si Tasos , sanno essere divertenti", "en", "IT"),
                Times.Once);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("**Hint used:** IT", response, StringComparison.Ordinal);
            Assert.Contains("**Translation** (IT => EN):", response, StringComparison.Ordinal);
            Assert.Contains("Yes, Tasos, they can be fun.", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TranslateAsync_SkipsSourceHintWhenDominantIsUnclear()
        {
            using var harness = await CreateHarnessAsync();
            harness.ProviderMock
                .Setup(provider => provider.AnalyzeLanguageAsync("Hello ciao"))
                .ReturnsAsync(new LanguageAnalysisResult(
                    "EN",
                    [
                        new DetectedLanguage("EN", 0.5),
                        new DetectedLanguage("IT", 0.5)
                    ],
                    isMixed: true,
                    hasClearDominantLanguage: false));
            harness.ProviderMock
                .Setup(provider => provider.TranslateTextAsync("Hello ciao", "de", null))
                .ReturnsAsync("Hallo ciao");

            await harness.Module.TranslateAsync("de", "Hello ciao");

            harness.ProviderMock.Verify(
                provider => provider.TranslateTextAsync("Hello ciao", "de", null),
                Times.Once);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("**Hint used:** none (dominant unclear)", response, StringComparison.Ordinal);
            Assert.Contains("**Translation** (EN,IT => DE):", response, StringComparison.Ordinal);
            Assert.Contains("Hallo ciao", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task InteractionModule_RegistersManualDiagnosticSlashCommands()
        {
            using var harness = await CreateHarnessAsync();
            await harness.InteractionService.AddModulesAsync(typeof(Program).Assembly, harness.Services);

            var addMasterCommand = Assert.Single(
                harness.InteractionService.SlashCommands,
                command => command.Name == "add-master");
            var registerLocalCommand = Assert.Single(
                harness.InteractionService.SlashCommands,
                command => command.Name == "register-local");
            var unregisterCommand = Assert.Single(
                harness.InteractionService.SlashCommands,
                command => command.Name == "unregister");
            var statusCommand = Assert.Single(
                harness.InteractionService.SlashCommands,
                command => command.Name == "status");
            var detectLanguageCommand = Assert.Single(
                harness.InteractionService.SlashCommands,
                command => command.Name == "detect-language");
            var translateCommand = Assert.Single(
                harness.InteractionService.SlashCommands,
                command => command.Name == "translate");

            Assert.True(addMasterCommand.IsEnabledInDm);
            Assert.True(registerLocalCommand.IsEnabledInDm);
            Assert.True(unregisterCommand.IsEnabledInDm);
            Assert.True(statusCommand.IsEnabledInDm);
            Assert.True(detectLanguageCommand.IsEnabledInDm);
            Assert.True(translateCommand.IsEnabledInDm);
        }

        [Fact]
        public async Task AddMasterAsync_RejectsDmContext()
        {
            using var harness = await CreateHarnessAsync();
            var channelMock = new Mock<IChannel>();

            await harness.Module.AddMasterAsync(channelMock.Object);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("only be used inside a guild channel", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AddMasterAsync_RejectsNonAdministratorGuildUser()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL, guildAdministrator: false);
            var channelMock = new Mock<IChannel>();

            await harness.Module.AddMasterAsync(channelMock.Object);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("requires Administrator permissions", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TranslateAsync_SkipsProviderCallWhenSourceAlreadyMatchesTarget()
        {
            using var harness = await CreateHarnessAsync();
            harness.ProviderMock
                .Setup(provider => provider.AnalyzeLanguageAsync("Already in English"))
                .ReturnsAsync(LanguageAnalysisResult.SingleLanguage("EN"));

            await harness.Module.TranslateAsync("en", "Already in English");

            harness.ProviderMock.Verify(
                provider => provider.TranslateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
                Times.Never);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("**Translation skipped:** source already matches target", response, StringComparison.Ordinal);
            Assert.Contains("**Output** (EN):", response, StringComparison.Ordinal);
            Assert.Contains("Already in English", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TranslateAsync_UsesCurrentChannelLocaleWhenDecidingSkipBehavior()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL, contextChannelId: 222UL);
            await harness.DatabaseService.AddMasterChannelAsync(111UL);
            await harness.DatabaseService.RegisterLocalChannelAsync(222UL, 111UL, "pt-br");
            harness.ProviderMock
                .Setup(provider => provider.AnalyzeLanguageAsync("Ola pessoal"))
                .ReturnsAsync(LanguageAnalysisResult.SingleLanguage("PT"));

            await harness.Module.TranslateAsync("pt-br", "Ola pessoal");

            harness.ProviderMock.Verify(
                provider => provider.TranslateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
                Times.Never);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("**Translation skipped:** source already matches target", response, StringComparison.Ordinal);
            Assert.Contains("**Output** (PT-BR):", response, StringComparison.Ordinal);
            Assert.Contains("Ola pessoal", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TranslateAsync_AnalysisFailureStillAttemptsTranslationWithoutHint()
        {
            using var harness = await CreateHarnessAsync();
            harness.ProviderMock
                .Setup(provider => provider.AnalyzeLanguageAsync("No analysis available"))
                .ThrowsAsync(new InvalidOperationException("analysis failed"));
            harness.ProviderMock
                .Setup(provider => provider.TranslateTextAsync("No analysis available", "en", null))
                .ReturnsAsync("Translated anyway");

            await harness.Module.TranslateAsync("en", "No analysis available");

            harness.ProviderMock.Verify(
                provider => provider.TranslateTextAsync("No analysis available", "en", null),
                Times.Once);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("**Detected:** unavailable", response, StringComparison.Ordinal);
            Assert.Contains("**Hint used:** none (analysis failed)", response, StringComparison.Ordinal);
            Assert.Contains("**Translation** (?? => EN):", response, StringComparison.Ordinal);
            Assert.Contains("Translated anyway", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DetectLanguageAsync_RejectsNonAdministratorGuildUser()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL, guildAdministrator: false);

            await harness.Module.DetectLanguageAsync("Hello");

            harness.InteractionMock.Verify(
                interaction => interaction.DeferAsync(It.IsAny<bool>(), It.IsAny<RequestOptions>()),
                Times.Never);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("requires Administrator permissions", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StatusAsync_RespondsWithDatabaseCounts()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL, usesCharacterQuota: true);
            await harness.DatabaseService.AddMasterChannelAsync(111UL);
            await harness.DatabaseService.RegisterLocalChannelAsync(222UL, 111UL, "de");
            await harness.DatabaseService.LinkMessagesAsync(9000UL, 111UL, 9001UL, 111UL, "master");
            await harness.DatabaseService.LinkMessagesAsync(9000UL, 111UL, 9002UL, 222UL, "de");
            await harness.DatabaseService.IncrementUsageAsync(350);

            await harness.Module.StatusAsync();

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("**Translation provider:** TestProvider", response, StringComparison.Ordinal);
            Assert.Contains("**Translation provider active:** yes", response, StringComparison.Ordinal);
            Assert.Contains("**Provider Model:** test-model", response, StringComparison.Ordinal);
            Assert.Contains("**Provider Endpoint:** http://localhost:1234", response, StringComparison.Ordinal);
            Assert.Contains("**Master channels:** 1", response, StringComparison.Ordinal);
            Assert.Contains("**Localized channels:** 1", response, StringComparison.Ordinal);
            Assert.Contains("**Configured channels:** 2", response, StringComparison.Ordinal);
            Assert.Contains("**Linked message families:** 1", response, StringComparison.Ordinal);
            Assert.Contains("**Linked bot messages:** 2", response, StringComparison.Ordinal);
            Assert.Contains("**Localized message links:** 1", response, StringComparison.Ordinal);
            Assert.Contains("**Quota-tracked characters this month:** 350", response, StringComparison.Ordinal);
            Assert.Contains("**Database storage size:**", response, StringComparison.Ordinal);
            Assert.Contains("**DB last activity (UTC):**", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StatusAsync_RejectsDmContext()
        {
            using var harness = await CreateHarnessAsync();

            await harness.Module.StatusAsync();

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("only be used inside a guild channel", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SyncAsync_RejectsDmContext()
        {
            using var harness = await CreateHarnessAsync();

            var textChannel = new Mock<IChannel>();
            textChannel.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(777UL);
            textChannel.As<ITextChannel>().SetupGet(channel => channel.Id).Returns(777UL);

            await harness.Module.SyncAsync(textChannel.Object, 24);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("only be used inside a guild channel", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SyncAsync_RejectsNonAdministrator()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL, guildAdministrator: false);

            var textChannel = new Mock<IChannel>();
            textChannel.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(777UL);
            textChannel.As<ITextChannel>().SetupGet(channel => channel.Id).Returns(777UL);

            await harness.Module.SyncAsync(textChannel.Object, 24);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("requires Administrator permissions", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SyncAsync_RejectsNonTextChannel()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL);
            var nonTextChannel = new Mock<IChannel>();
            nonTextChannel.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(999UL);

            await harness.Module.SyncAsync(nonTextChannel.Object, 24);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("must be a standard Guild Text Channel", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SyncAsync_RejectsUnregisteredChannel()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL);
            var textChannel = new Mock<IChannel>();
            textChannel.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(888UL);
            textChannel.As<ITextChannel>().SetupGet(channel => channel.Id).Returns(888UL);

            await harness.Module.SyncAsync(textChannel.Object, 24);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("not registered as a Master channel", response, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(169)]
        public async Task SyncAsync_RejectsInvalidHours(int hours)
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL);
            var masterChannelId = 777UL;
            var textChannel = new Mock<IChannel>();
            textChannel.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(masterChannelId);
            textChannel.As<ITextChannel>().SetupGet(channel => channel.Id).Returns(masterChannelId);
            await harness.DatabaseService.AddMasterChannelAsync(masterChannelId);

            await harness.Module.SyncAsync(textChannel.Object, hours);

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.RespondAsync));
            Assert.Contains("Hours must be between", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SyncAsync_ReportsServiceResult()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL);
            var masterChannelId = 777UL;
            var textChannel = new Mock<IChannel>();
            textChannel.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(masterChannelId);
            textChannel.As<ITextChannel>().SetupGet(channel => channel.Id).Returns(masterChannelId);
            await harness.DatabaseService.AddMasterChannelAsync(masterChannelId);

            harness.HistoricalSyncMock
                .Setup(service => service.SyncMasterChannelAsync(masterChannelId, TimeSpan.FromHours(24), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoricalSyncResult
                {
                    Success = true,
                    ProcessedCount = 5,
                    FailedCount = 1,
                    SkippedCount = 2
                });

            await harness.Module.SyncAsync(textChannel.Object, 24);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("Sync completed", response, StringComparison.Ordinal);
            Assert.Contains("Processed: **5**", response, StringComparison.Ordinal);
            Assert.Contains("Failed: **1**", response, StringComparison.Ordinal);
            Assert.Contains("Skipped", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SyncAsync_ReportsServiceFailure()
        {
            using var harness = await CreateHarnessAsync(guildId: 12345UL);
            var masterChannelId = 777UL;
            var textChannel = new Mock<IChannel>();
            textChannel.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(masterChannelId);
            textChannel.As<ITextChannel>().SetupGet(channel => channel.Id).Returns(masterChannelId);
            await harness.DatabaseService.AddMasterChannelAsync(masterChannelId);

            harness.HistoricalSyncMock
                .Setup(service => service.SyncMasterChannelAsync(masterChannelId, TimeSpan.FromHours(24), It.IsAny<CancellationToken>()))
                .ReturnsAsync(HistoricalSyncResult.Failure("Channel resolution failed"));

            await harness.Module.SyncAsync(textChannel.Object, 24);

            var response = GetLatestFollowupText(harness.InteractionMock);
            Assert.Contains("Channel resolution failed", response, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DetectLanguageAsync_DoesNotExposeRawProviderErrors()
        {
            using var harness = await CreateHarnessAsync();
            harness.ProviderMock
                .Setup(provider => provider.AnalyzeLanguageAsync("fail"))
                .ThrowsAsync(new InvalidOperationException("Url: http://localhost:11434/api/generate Response body: sensitive details"));

            await harness.Module.DetectLanguageAsync("fail");

            var response = GetLatestInteractionText(harness.InteractionMock, nameof(IDiscordInteraction.FollowupAsync));
            Assert.DoesNotContain("http://localhost:11434", response, StringComparison.Ordinal);
            Assert.DoesNotContain("Response body", response, StringComparison.Ordinal);
            Assert.Contains("Language detection failed. Check the bot logs for details.", response, StringComparison.Ordinal);
        }

        private static string GetLatestFollowupText(Mock<IDiscordInteraction> interactionMock)
        {
            return GetLatestInteractionText(interactionMock, nameof(IDiscordInteraction.FollowupAsync));
        }

        private static string GetLatestInteractionText(Mock<IDiscordInteraction> interactionMock, string methodName)
        {
            var invocation = interactionMock.Invocations.Last(invocation => invocation.Method.Name == methodName);
            return Assert.IsType<string>(invocation.Arguments[0]);
        }

        private static async Task<InteractionModuleHarness> CreateHarnessAsync(
            ulong? guildId = null,
            ulong contextChannelId = 555UL,
            bool usesCharacterQuota = false,
            bool guildAdministrator = true)
        {
            var database = new TemporarySqliteDatabase("interaction-module");
            var dbService = database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
            await dbService.InitializeDatabaseAsync();

            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(provider => provider.IsActive).Returns(true);
            providerMock.SetupGet(provider => provider.UsesCharacterQuota).Returns(usesCharacterQuota);
            providerMock
                .Setup(provider => provider.GetConfigurationReport())
                .Returns(new TranslationProviderConfigurationReport(
                    "TestProvider",
                    "MockTranslationProvider",
                    IsActive: true,
                    UsesCharacterQuota: usesCharacterQuota,
                    [
                        new TranslationProviderConfigurationItem("Endpoint", "http://localhost:1234"),
                        new TranslationProviderConfigurationItem("Model", "test-model"),
                        new TranslationProviderConfigurationItem("Capabilities", "Mixed-language analysis and translation")
                    ]));
            var translationService = new TranslationService(
                dbService,
                providerMock.Object,
                NullLogger<TranslationService>.Instance,
                new NullLoggerFactory());

            var interactionMock = new Mock<IDiscordInteraction>();
            interactionMock
                .Setup(interaction => interaction.RespondAsync(
                    It.IsAny<string>(),
                    It.IsAny<Embed[]>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<AllowedMentions>(),
                    It.IsAny<MessageComponent>(),
                    It.IsAny<Embed>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<PollProperties>(),
                    It.IsAny<MessageFlags>()))
                .Returns(Task.CompletedTask);
            interactionMock
                .Setup(interaction => interaction.DeferAsync(It.IsAny<bool>(), It.IsAny<RequestOptions>()))
                .Returns(Task.CompletedTask);
            interactionMock
                .SetupGet(interaction => interaction.HasResponded)
                .Returns(false);
            interactionMock
                .Setup(interaction => interaction.FollowupAsync(
                    It.IsAny<string>(),
                    It.IsAny<Embed[]>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<AllowedMentions>(),
                    It.IsAny<MessageComponent>(),
                    It.IsAny<Embed>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<PollProperties>(),
                    It.IsAny<MessageFlags>()))
                .ReturnsAsync(Mock.Of<IUserMessage>());

            Mock<IUser> userMock;
            if (guildId.HasValue)
            {
                var guildUserMock = new Mock<IGuildUser>();
                guildUserMock.SetupGet(user => user.Username).Returns("tester");
                guildUserMock.SetupGet(user => user.GuildPermissions).Returns(new GuildPermissions(administrator: guildAdministrator));
                userMock = guildUserMock.As<IUser>();
            }
            else
            {
                userMock = new Mock<IUser>();
                userMock.SetupGet(user => user.Username).Returns("tester");
            }

            IGuild? guild = guildId.HasValue ? Mock.Of<IGuild>() : null;
            var channelMock = new Mock<IMessageChannel>();
            channelMock.SetupGet(channel => channel.Id).Returns(contextChannelId);
            var contextMock = new Mock<IInteractionContext>();
            contextMock.SetupGet(context => context.Guild).Returns(guild!);
            contextMock.SetupGet(context => context.Channel).Returns(channelMock.Object);
            contextMock.SetupGet(context => context.User).Returns(userMock.Object);
            contextMock.SetupGet(context => context.Interaction).Returns(interactionMock.Object);

            var historicalSyncMock = new Mock<IHistoricalMessageSyncService>();

            var module = new InteractionModule(
                dbService,
                translationService,
                historicalSyncMock.Object,
                NullLogger<InteractionModule>.Instance);
            SetModuleContext(module, contextMock.Object);

            var services = new ServiceCollection()
                .AddSingleton(dbService)
                .AddSingleton(translationService)
                .AddSingleton(providerMock.Object)
                .AddSingleton(historicalSyncMock.Object)
                .AddSingleton<ILogger<InteractionModule>>(NullLogger<InteractionModule>.Instance)
                .BuildServiceProvider();

            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.None
            });
            var interactionService = new InteractionService(client, new InteractionServiceConfig());

            return new InteractionModuleHarness(
                database,
                dbService,
                providerMock,
                interactionMock,
                historicalSyncMock,
                module,
                services,
                client,
                interactionService);
        }

        private static void SetModuleContext(InteractionModule module, IInteractionContext context)
        {
            var contextProperty = typeof(InteractionModuleBase<IInteractionContext>).GetProperty(
                "Context",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(contextProperty);
            contextProperty!.SetValue(module, context);
        }

        private sealed class InteractionModuleHarness : IDisposable
        {
            public InteractionModuleHarness(
                TemporarySqliteDatabase database,
                DatabaseService databaseService,
                Mock<ITranslationProvider> providerMock,
                Mock<IDiscordInteraction> interactionMock,
                Mock<IHistoricalMessageSyncService> historicalSyncMock,
                InteractionModule module,
                ServiceProvider services,
                DiscordSocketClient client,
                InteractionService interactionService)
            {
                Database = database;
                DatabaseService = databaseService;
                ProviderMock = providerMock;
                InteractionMock = interactionMock;
                HistoricalSyncMock = historicalSyncMock;
                Module = module;
                Services = services;
                Client = client;
                InteractionService = interactionService;
            }

            public TemporarySqliteDatabase Database { get; }

            public DatabaseService DatabaseService { get; }

            public Mock<ITranslationProvider> ProviderMock { get; }

            public Mock<IDiscordInteraction> InteractionMock { get; }

            public Mock<IHistoricalMessageSyncService> HistoricalSyncMock { get; }

            public InteractionModule Module { get; }

            public ServiceProvider Services { get; }

            public DiscordSocketClient Client { get; }

            public InteractionService InteractionService { get; }

            public void Dispose()
            {
                InteractionService.Dispose();
                Client.Dispose();
                Services.Dispose();
                Database.Dispose();
            }
        }
    }
}
