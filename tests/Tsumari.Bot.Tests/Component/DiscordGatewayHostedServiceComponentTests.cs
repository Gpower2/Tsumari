using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests.Component
{
    public class DiscordGatewayHostedServiceComponentTests : IDisposable
    {
        private readonly TemporarySqliteDatabase _database;
        private readonly DatabaseService _dbService;

        public DiscordGatewayHostedServiceComponentTests()
        {
            _database = new TemporarySqliteDatabase("gateway-component");
            _dbService = _database.CreateDatabaseService(NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            _database.Dispose();
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_RoutesMasterMessageToLocalizedChannels_AndAddsJumpButtons()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "it");

            var translationProvider = CreateTranslationProvider(
                detections: new Dictionary<string, string>
                {
                    ["Hello world"] = "EN"
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Hello world", "de")] = "Hallo Welt",
                    [("Hello world", "it")] = "Ciao mondo"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var italianChannel = new ChannelCapture(30UL, 1UL, "general-it");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);
            discordMessageService.RegisterChannel(italianChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var message = CreateIncomingMessage(100UL, masterChannel.Channel, author, "Hello world");

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(germanChannel.SentMessages);
            Assert.Equal("**Alice** (EN to DE):\nHallo Welt", germanChannel.SentMessages[0].Content);
            Assert.Null(germanChannel.SentMessages[0].ReplyReference);
            Assert.Equal(1, germanChannel.SentMessages[0].ModifyCallCount);

            Assert.Single(italianChannel.SentMessages);
            Assert.Equal("**Alice** (EN to IT):\nCiao mondo", italianChannel.SentMessages[0].Content);
            Assert.Null(italianChannel.SentMessages[0].ReplyReference);
            Assert.Equal(1, italianChannel.SentMessages[0].ModifyCallCount);

            var expectedJumpButtons = new Dictionary<string, string>
            {
                ["Original"] = "https://discord.com/channels/1/10/100",
                ["DE"] = $"https://discord.com/channels/1/20/{germanChannel.SentMessages[0].Id}",
                ["IT"] = $"https://discord.com/channels/1/30/{italianChannel.SentMessages[0].Id}"
            };
            AssertJumpButtons(germanChannel.SentMessages[0].Components, expectedJumpButtons);
            AssertJumpButtons(italianChannel.SentMessages[0].Components, expectedJumpButtons);

            var links = await _dbService.GetMirroredMessagesAsync(100UL);
            Assert.Equal(2, links.Count);
            Assert.Contains(links, link => link.ChannelId == 20UL && link.LanguageCode == "DE");
            Assert.Contains(links, link => link.ChannelId == 30UL && link.LanguageCode == "IT");
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_RoutesAttachmentOnlyMasterMessage_WithHeaderOnlyContent()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");

            var translationProvider = CreateTranslationProvider();
            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                }));
            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object, httpClient);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var attachmentMock = new Mock<IAttachment>();
            attachmentMock.SetupGet(attachment => attachment.Filename).Returns("file.txt");
            attachmentMock.SetupGet(attachment => attachment.Url).Returns("https://cdn.example/file.txt");
            var message = CreateIncomingMessage(
                100UL,
                masterChannel.Channel,
                author,
                string.Empty,
                attachments: [attachmentMock.Object]);

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(germanChannel.SentMessages);
            Assert.Equal("**Alice**:", germanChannel.SentMessages[0].Content);
            Assert.Equal(1, germanChannel.SentMessages[0].AttachedFileCount);
            Assert.Equal(1, germanChannel.SentMessages[0].ModifyCallCount);

            var links = await _dbService.GetMirroredMessagesAsync(100UL);
            Assert.Single(links);
            Assert.Contains(links, link => link.ChannelId == 20UL && link.LanguageCode == "DE");
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_SkipsOversizedAttachmentsBeforeDownload_AndFallsBackToTextNotice()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");

            var translationProvider = CreateTranslationProvider();
            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general", maxUploadLimit: 10UL);
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de", maxUploadLimit: 10UL);
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var requestCount = 0;
            var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            {
                requestCount++;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                };
            }));
            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object, httpClient);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var attachmentMock = new Mock<IAttachment>();
            attachmentMock.SetupGet(attachment => attachment.Filename).Returns("large-file.mp4");
            attachmentMock.SetupGet(attachment => attachment.Url).Returns("https://cdn.example/large-file.mp4");
            attachmentMock.SetupGet(attachment => attachment.Size).Returns(11);
            var message = CreateIncomingMessage(
                100UL,
                masterChannel.Channel,
                author,
                string.Empty,
                attachments: [attachmentMock.Object]);

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(germanChannel.SentMessages);
            Assert.Equal("**Alice**:\n*(Anhang zu gross zum Spiegeln - nutze Original.)*", germanChannel.SentMessages[0].Content);
            Assert.Equal(0, germanChannel.SentMessages[0].AttachedFileCount);
            Assert.Equal(0, requestCount);
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_MirrorsRepliesAcrossChannels_ForLocalizedMismatchFlow()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "it");
            await _dbService.RegisterLocalChannelAsync(40UL, 10UL, "en");

            await _dbService.LinkMessagesAsync(500UL, 10UL, 520UL, 20UL, "de");
            await _dbService.LinkMessagesAsync(500UL, 10UL, 530UL, 30UL, "it");
            await _dbService.LinkMessagesAsync(500UL, 10UL, 540UL, 40UL, "en");

            var translationProvider = CreateTranslationProvider(
                detections: new Dictionary<string, string>
                {
                    ["Hello from Germany"] = "EN"
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Hello from Germany", "de")] = "Hallo aus Deutschland",
                    [("Hello from Germany", "it")] = "Ciao dalla Germania"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var italianChannel = new ChannelCapture(30UL, 1UL, "general-it");
            var englishChannel = new ChannelCapture(40UL, 1UL, "general-en");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);
            discordMessageService.RegisterChannel(italianChannel);
            discordMessageService.RegisterChannel(englishChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var replyReference = new MessageReference(520UL, 20UL, null, false, default);
            var message = CreateIncomingMessage(600UL, germanChannel.Channel, author, "Hello from Germany", replyReference);

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(germanChannel.SentMessages);
            Assert.Equal("*(EN to DE):* Hallo aus Deutschland", germanChannel.SentMessages[0].Content);
            Assert.NotNull(germanChannel.SentMessages[0].ReplyReference);
            Assert.Equal(520UL, germanChannel.SentMessages[0].ReplyReference!.MessageId.Value);

            Assert.Single(masterChannel.SentMessages);
            Assert.Equal("**Alice**:\nHello from Germany", masterChannel.SentMessages[0].Content);
            Assert.NotNull(masterChannel.SentMessages[0].ReplyReference);
            Assert.Equal(500UL, masterChannel.SentMessages[0].ReplyReference!.MessageId.Value);

            Assert.Single(italianChannel.SentMessages);
            Assert.Equal("**Alice** (EN to IT):\nCiao dalla Germania", italianChannel.SentMessages[0].Content);
            Assert.NotNull(italianChannel.SentMessages[0].ReplyReference);
            Assert.Equal(530UL, italianChannel.SentMessages[0].ReplyReference!.MessageId.Value);

            Assert.Single(englishChannel.SentMessages);
            Assert.Equal("**Alice**:\nHello from Germany", englishChannel.SentMessages[0].Content);
            Assert.NotNull(englishChannel.SentMessages[0].ReplyReference);
            Assert.Equal(540UL, englishChannel.SentMessages[0].ReplyReference!.MessageId.Value);

            var links = await _dbService.GetMirroredMessagesAsync(600UL);
            Assert.Equal(4, links.Count);
            Assert.Contains(links, link => link.ChannelId == 20UL && link.LanguageCode == "DE");
            Assert.Contains(links, link => link.ChannelId == 10UL && link.LanguageCode == "MASTER");
            Assert.Contains(links, link => link.ChannelId == 30UL && link.LanguageCode == "IT");
            Assert.Contains(links, link => link.ChannelId == 40UL && link.LanguageCode == "EN");
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_SkipsNativeMismatchReply_WhenSourceChannelTranslationFails()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "it");

            var translationProvider = CreateTranslationProvider(
                detections: new Dictionary<string, string>
                {
                    ["Hello from Germany"] = "EN"
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Hello from Germany", "IT")] = "Ciao dalla Germania"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var italianChannel = new ChannelCapture(30UL, 1UL, "general-it");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);
            discordMessageService.RegisterChannel(italianChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var message = CreateIncomingMessage(600UL, germanChannel.Channel, author, "Hello from Germany");

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Empty(germanChannel.SentMessages);
            Assert.Single(masterChannel.SentMessages);
            Assert.Equal("**Alice**:\nHello from Germany", masterChannel.SentMessages[0].Content);
            Assert.Single(italianChannel.SentMessages);
            Assert.False(string.IsNullOrWhiteSpace(italianChannel.SentMessages[0].Content));

            var links = await _dbService.GetMirroredMessagesAsync(600UL);
            Assert.Equal(2, links.Count);
            Assert.DoesNotContain(links, link => link.ChannelId == 20UL);
            Assert.Contains(links, link => link.ChannelId == 10UL && link.LanguageCode == "MASTER");
            Assert.Contains(links, link => link.ChannelId == 30UL && link.LanguageCode == "IT");
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_FallsBackToEnglish_WhenAnalysisFailsInLocalizedChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "it");

            var translationProvider = CreateTranslationProvider(
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Hello from Germany", "DE")] = "Hallo aus Deutschland",
                    [("Hello from Germany", "IT")] = "Ciao dalla Germania"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var italianChannel = new ChannelCapture(30UL, 1UL, "general-it");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);
            discordMessageService.RegisterChannel(italianChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var message = CreateIncomingMessage(601UL, germanChannel.Channel, author, "Hello from Germany");

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(germanChannel.SentMessages);
            Assert.Equal("*(EN to DE):* Hallo aus Deutschland", germanChannel.SentMessages[0].Content);
            Assert.Single(masterChannel.SentMessages);
            Assert.Equal("**Alice**:\nHello from Germany", masterChannel.SentMessages[0].Content);
            Assert.Single(italianChannel.SentMessages);
            Assert.Equal("**Alice** (EN to IT):\nCiao dalla Germania", italianChannel.SentMessages[0].Content);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    "Hello from Germany",
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    "EN"),
                Times.Never);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    "Hello from Germany",
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_DoesNotPassSourceHint_WhenDominantLanguageIsNotClear()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");

            const string content = "Thank you fratello! How are you doing?";
            var translationProvider = CreateTranslationProvider(
                analyses: new Dictionary<string, LanguageAnalysisResult>
                {
                    [content] = new(
                        "EN",
                        [
                            new DetectedLanguage("EN", 0.55),
                            new DetectedLanguage("IT", 0.45)
                        ],
                        isMixed: true,
                        hasClearDominantLanguage: false)
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [(content, "DE")] = "Danke Bruder! Wie geht es dir?"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var message = CreateIncomingMessage(602UL, masterChannel.Channel, author, content);

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(germanChannel.SentMessages);
            Assert.Equal("**Alice** (EN,IT => DE):\nDanke Bruder! Wie geht es dir?", germanChannel.SentMessages[0].Content);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    content,
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    "EN"),
                Times.Never);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    content,
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_TreatsFratelloExampleAsEnglishDominantInLocalizedItalianChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "it");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "de");

            const string content = "Thank you fratello! 🤗\nThings are already heating up, they are placing bets like crazy 😅";
            var translationProvider = CreateTranslationProvider(
                analyses: new Dictionary<string, LanguageAnalysisResult>
                {
                    [content] = new(
                        "EN",
                        [
                            new DetectedLanguage("EN", 0.88),
                            new DetectedLanguage("IT", 0.12)
                        ],
                        isMixed: true,
                        hasClearDominantLanguage: true)
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [(content, "IT")] = "Grazie fratello! 🤗\nLe cose si stanno gia scaldando, stanno scommettendo come matti 😅",
                    [(content, "DE")] = "Danke Bruder! 🤗\nDie Lage heizt sich schon auf, sie wetten wie verrueckt 😅"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var italianChannel = new ChannelCapture(20UL, 1UL, "general-it");
            var germanChannel = new ChannelCapture(30UL, 1UL, "general-de");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(italianChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var message = CreateIncomingMessage(650UL, italianChannel.Channel, author, content);

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(italianChannel.SentMessages);
            Assert.Equal("*(EN,IT => IT):* Grazie fratello! 🤗\nLe cose si stanno gia scaldando, stanno scommettendo come matti 😅", italianChannel.SentMessages[0].Content);
            Assert.Single(masterChannel.SentMessages);
            Assert.Equal($"**Alice**:\n{content}", masterChannel.SentMessages[0].Content);
            Assert.Single(germanChannel.SentMessages);
            Assert.Equal("**Alice** (EN,IT => DE):\nDanke Bruder! 🤗\nDie Lage heizt sich schon auf, sie wetten wie verrueckt 😅", germanChannel.SentMessages[0].Content);
        }

        [Fact]
        public async Task HandleMessageReceivedAsync_TreatsMaCherieExampleAsEnglishDominantInLocalizedFrenchChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "fr");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "el");

            const string content = "Ma cherie! How are you doing?";
            var translationProvider = CreateTranslationProvider(
                analyses: new Dictionary<string, LanguageAnalysisResult>
                {
                    [content] = new(
                        "EN",
                        [
                            new DetectedLanguage("EN", 0.67),
                            new DetectedLanguage("FR", 0.33)
                        ],
                        isMixed: true,
                        hasClearDominantLanguage: true)
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [(content, "FR")] = "Ma cherie ! Comment vas-tu ?",
                    [(content, "EL")] = "Μα σερί! Τι κάνεις;"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var frenchChannel = new ChannelCapture(20UL, 1UL, "general-fr");
            var greekChannel = new ChannelCapture(30UL, 1UL, "general-el");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(frenchChannel);
            discordMessageService.RegisterChannel(greekChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var message = CreateIncomingMessage(660UL, frenchChannel.Channel, author, content);

            await hostedService.HandleMessageReceivedAsync(message.Object);

            Assert.Single(frenchChannel.SentMessages);
            Assert.Equal("*(EN,FR => FR):* Ma cherie ! Comment vas-tu ?", frenchChannel.SentMessages[0].Content);
            Assert.Single(masterChannel.SentMessages);
            Assert.Equal($"**Alice**:\n{content}", masterChannel.SentMessages[0].Content);
            Assert.Single(greekChannel.SentMessages);
            Assert.Equal("**Alice** (EN,FR => EL):\nΜα σερί! Τι κάνεις;", greekChannel.SentMessages[0].Content);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_ModifiesExistingLinkedMessages_InPlace()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.LinkMessagesAsync(700UL, 10UL, 720UL, 20UL, "de");

            var translationProvider = CreateTranslationProvider(
                detections: new Dictionary<string, string>
                {
                    ["Updated text"] = "EN"
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Updated text", "DE")] = "Aktualisierter Text"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var mirroredMessage = germanChannel.RegisterExistingMessage(720UL, "**Alice** (EN to DE):\nOld text");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var editedMessage = CreateIncomingMessage(700UL, masterChannel.Channel, author, "Updated text");

            await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "Old text", editedMessage.Object);

            Assert.Equal("**Alice** (EN to DE):\nAktualisierter Text", mirroredMessage.Content);
            Assert.Equal(1, mirroredMessage.ModifyCallCount);
            Assert.Empty(germanChannel.SentMessages);

            var links = await _dbService.GetMirroredMessagesAsync(700UL);
            Assert.Single(links);
            Assert.Equal(720UL, links[0].MirroredMessageId);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_PreservesOversizedAttachmentNotice_WhenSourceStillHasOversizedAttachments()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.LinkMessagesAsync(704UL, 10UL, 724UL, 20UL, "de");

            var translationProvider = CreateTranslationProvider(
                detections: new Dictionary<string, string>
                {
                    ["Updated text"] = "EN"
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Updated text", "DE")] = "Aktualisierter Text"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general", maxUploadLimit: 10UL);
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de", maxUploadLimit: 10UL);
            var mirroredMessage = germanChannel.RegisterExistingMessage(
                724UL,
                "**Alice** (EN to DE):\nOld text\n*(Anhang zu gross zum Spiegeln - nutze Original.)*");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var attachmentMock = new Mock<IAttachment>();
            attachmentMock.SetupGet(attachment => attachment.Filename).Returns("large-file.mp4");
            attachmentMock.SetupGet(attachment => attachment.Size).Returns(11);
            var editedMessage = CreateIncomingMessage(
                704UL,
                masterChannel.Channel,
                author,
                "Updated text",
                attachments: [attachmentMock.Object]);

            await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "Old text", editedMessage.Object);

            Assert.Equal(
                "**Alice** (EN to DE):\nAktualisierter Text\n*(Anhang zu gross zum Spiegeln - nutze Original.)*",
                mirroredMessage.Content);
            Assert.Equal(1, mirroredMessage.ModifyCallCount);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_UsesMixedLanguageLabelForEditedMasterMessage()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.LinkMessagesAsync(701UL, 10UL, 721UL, 20UL, "de");

            const string content = "Thank you fratello! 🤗\nThings are already heating up, they are placing bets like crazy 😅";
            var translationProvider = CreateTranslationProvider(
                analyses: new Dictionary<string, LanguageAnalysisResult>
                {
                    [content] = new(
                        "EN",
                        [
                            new DetectedLanguage("EN", 0.88),
                            new DetectedLanguage("IT", 0.12)
                        ],
                        isMixed: true,
                        hasClearDominantLanguage: true)
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [(content, "DE")] = "Danke Bruder! 🤗\nDie Lage heizt sich schon auf, sie wetten wie verrueckt 😅"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var mirroredMessage = germanChannel.RegisterExistingMessage(721UL, "**Alice** (EN to DE):\nOld text");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var editedMessage = CreateIncomingMessage(701UL, masterChannel.Channel, author, content);

            await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "Old text", editedMessage.Object);

            Assert.Equal("**Alice** (EN,IT => DE):\nDanke Bruder! 🤗\nDie Lage heizt sich schon auf, sie wetten wie verrueckt 😅", mirroredMessage.Content);
            Assert.Equal(1, mirroredMessage.ModifyCallCount);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_DoesNotPassSourceHint_WhenDominantLanguageIsNotClear()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.LinkMessagesAsync(703UL, 10UL, 723UL, 20UL, "de");

            const string content = "Thank you fratello! How are you doing?";
            var translationProvider = CreateTranslationProvider(
                analyses: new Dictionary<string, LanguageAnalysisResult>
                {
                    [content] = new(
                        "EN",
                        [
                            new DetectedLanguage("EN", 0.55),
                            new DetectedLanguage("IT", 0.45)
                        ],
                        isMixed: true,
                        hasClearDominantLanguage: false)
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [(content, "DE")] = "Danke Bruder! Wie geht es dir?"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var mirroredMessage = germanChannel.RegisterExistingMessage(723UL, "**Alice** (EN to DE):\nOld text");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var editedMessage = CreateIncomingMessage(703UL, masterChannel.Channel, author, content);

            await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "Old text", editedMessage.Object);

            Assert.Equal("**Alice** (EN,IT => DE):\nDanke Bruder! Wie geht es dir?", mirroredMessage.Content);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    content,
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    "EN"),
                Times.Never);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    content,
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_ModifiesLocalizedMismatchFamily_InPlace()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "it");
            await _dbService.RegisterLocalChannelAsync(40UL, 10UL, "en");
            await _dbService.LinkMessagesAsync(700UL, 20UL, 710UL, 10UL, "master");
            await _dbService.LinkMessagesAsync(700UL, 20UL, 720UL, 20UL, "de");
            await _dbService.LinkMessagesAsync(700UL, 20UL, 730UL, 30UL, "it");
            await _dbService.LinkMessagesAsync(700UL, 20UL, 740UL, 40UL, "en");

            var translationProvider = CreateTranslationProvider(
                detections: new Dictionary<string, string>
                {
                    ["Updated text"] = "EN"
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Updated text", "DE")] = "Aktualisierter Text",
                    [("Updated text", "IT")] = "Testo aggiornato"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var italianChannel = new ChannelCapture(30UL, 1UL, "general-it");
            var englishChannel = new ChannelCapture(40UL, 1UL, "general-en");
            var masterMirror = masterChannel.RegisterExistingMessage(710UL, "**Alice**:\nOld text");
            var germanReply = germanChannel.RegisterExistingMessage(720UL, "*(EN to DE):* Alter Text");
            var italianMirror = italianChannel.RegisterExistingMessage(730UL, "**Alice** (EN to IT):\nVecchio testo");
            var englishMirror = englishChannel.RegisterExistingMessage(740UL, "**Alice**:\nOld text");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);
            discordMessageService.RegisterChannel(italianChannel);
            discordMessageService.RegisterChannel(englishChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var editedMessage = CreateIncomingMessage(700UL, germanChannel.Channel, author, "Updated text");

            await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "Old text", editedMessage.Object);

            Assert.Equal("**Alice**:\nUpdated text", masterMirror.Content);
            Assert.Equal(1, masterMirror.ModifyCallCount);
            Assert.Equal("*(EN to DE):* Aktualisierter Text", germanReply.Content);
            Assert.Equal(1, germanReply.ModifyCallCount);
            Assert.Equal("**Alice** (EN to IT):\nTesto aggiornato", italianMirror.Content);
            Assert.Equal(1, italianMirror.ModifyCallCount);
            Assert.Equal("**Alice**:\nUpdated text", englishMirror.Content);
            Assert.Equal(1, englishMirror.ModifyCallCount);
            Assert.Empty(masterChannel.SentMessages);
            Assert.Empty(germanChannel.SentMessages);
            Assert.Empty(italianChannel.SentMessages);
            Assert.Empty(englishChannel.SentMessages);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_FallsBackToEnglish_WhenAnalysisFailsInLocalizedChannel()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "it");
            await _dbService.RegisterLocalChannelAsync(40UL, 10UL, "en");
            await _dbService.LinkMessagesAsync(702UL, 20UL, 712UL, 10UL, "master");
            await _dbService.LinkMessagesAsync(702UL, 20UL, 722UL, 20UL, "de");
            await _dbService.LinkMessagesAsync(702UL, 20UL, 732UL, 30UL, "it");
            await _dbService.LinkMessagesAsync(702UL, 20UL, 742UL, 40UL, "en");

            var translationProvider = CreateTranslationProvider(
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Updated text", "DE")] = "Aktualisierter Text",
                    [("Updated text", "IT")] = "Testo aggiornato"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var italianChannel = new ChannelCapture(30UL, 1UL, "general-it");
            var englishChannel = new ChannelCapture(40UL, 1UL, "general-en");
            var masterMirror = masterChannel.RegisterExistingMessage(712UL, "**Alice**:\nOld text");
            var germanReply = germanChannel.RegisterExistingMessage(722UL, "*(EN to DE):* Alter Text");
            var italianMirror = italianChannel.RegisterExistingMessage(732UL, "**Alice** (EN to IT):\nVecchio testo");
            var englishMirror = englishChannel.RegisterExistingMessage(742UL, "**Alice**:\nOld text");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);
            discordMessageService.RegisterChannel(italianChannel);
            discordMessageService.RegisterChannel(englishChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var editedMessage = CreateIncomingMessage(702UL, germanChannel.Channel, author, "Updated text");

            await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "Old text", editedMessage.Object);

            Assert.Equal("**Alice**:\nUpdated text", masterMirror.Content);
            Assert.Equal("*(EN to DE):* Aktualisierter Text", germanReply.Content);
            Assert.Equal("**Alice** (EN to IT):\nTesto aggiornato", italianMirror.Content);
            Assert.Equal("**Alice**:\nUpdated text", englishMirror.Content);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    "Updated text",
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    "EN"),
                Times.Never);
            translationProvider.Verify(provider =>
                provider.TranslateTextAsync(
                    "Updated text",
                    It.Is<string>(targetLanguage => LanguageCodeService.AreSameLanguageCode(targetLanguage, "DE")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task HandleMessageUpdatedAsync_UsesFailureFallback_WhenLocalizedSiblingRetranslationFails()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.AddMasterChannelAsync(10UL);
            await _dbService.RegisterLocalChannelAsync(20UL, 10UL, "de");
            await _dbService.RegisterLocalChannelAsync(30UL, 10UL, "it");
            await _dbService.LinkMessagesAsync(700UL, 20UL, 710UL, 10UL, "master");
            await _dbService.LinkMessagesAsync(700UL, 20UL, 720UL, 20UL, "de");
            await _dbService.LinkMessagesAsync(700UL, 20UL, 730UL, 30UL, "it");

            var translationProvider = CreateTranslationProvider(
                detections: new Dictionary<string, string>
                {
                    ["Updated text"] = "EN"
                },
                translations: new Dictionary<(string Text, string TargetLanguage), string>
                {
                    [("Updated text", "DE")] = "Aktualisierter Text"
                });

            var discordMessageService = new ComponentDiscordMessageService();
            var masterChannel = new ChannelCapture(10UL, 1UL, "general");
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var italianChannel = new ChannelCapture(30UL, 1UL, "general-it");
            var masterMirror = masterChannel.RegisterExistingMessage(710UL, "**Alice**:\nOld text");
            var germanReply = germanChannel.RegisterExistingMessage(720UL, "*(EN to DE):* Alter Text");
            var italianMirror = italianChannel.RegisterExistingMessage(730UL, "**Alice** (EN to IT):\nVecchio testo");
            discordMessageService.RegisterChannel(masterChannel);
            discordMessageService.RegisterChannel(germanChannel);
            discordMessageService.RegisterChannel(italianChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);
            var author = CreateGuildUser("alice", nickname: "Alice");
            var editedMessage = CreateIncomingMessage(700UL, germanChannel.Channel, author, "Updated text");

            await hostedService.HandleMessageUpdatedAsync(hadCachedSnapshot: true, beforeContent: "Old text", editedMessage.Object);

            Assert.Equal("**Alice**:\nUpdated text", masterMirror.Content);
            Assert.Equal(1, masterMirror.ModifyCallCount);
            Assert.Equal("*(EN to DE):* Aktualisierter Text", germanReply.Content);
            Assert.Equal(1, germanReply.ModifyCallCount);
            Assert.Equal("**Alice**:\nUpdated text *(Translation Failed)*", italianMirror.Content);
            Assert.Equal(1, italianMirror.ModifyCallCount);
            Assert.Empty(masterChannel.SentMessages);
            Assert.Empty(germanChannel.SentMessages);
            Assert.Empty(italianChannel.SentMessages);
        }

        [Fact]
        public async Task HandleMessageDeletedAsync_DeletesLinkedMirrors_ThroughHostedServiceSurface()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(800UL, 10UL, 820UL, 20UL, "de");

            var translationProvider = CreateTranslationProvider();
            var discordMessageService = new ComponentDiscordMessageService();
            var germanChannel = new ChannelCapture(20UL, 1UL, "general-de");
            germanChannel.RegisterExistingMessage(820UL, "**Alice**:\nHallo");
            discordMessageService.RegisterChannel(germanChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);

            await hostedService.HandleMessageDeletedAsync(800UL);

            Assert.Single(discordMessageService.DeletedMessages);
            Assert.Equal((20UL, 820UL), discordMessageService.DeletedMessages[0]);
            Assert.Empty(await _dbService.GetMirroredMessagesAsync(800UL));
        }

        [Fact]
        public async Task HandleReactionAddedAsync_MirrorsStandardReactions_ThroughHostedServiceSurface()
        {
            await _dbService.InitializeDatabaseAsync();
            await _dbService.LinkMessagesAsync(900UL, 10UL, 920UL, 20UL, "de");

            var translationProvider = CreateTranslationProvider();
            var discordMessageService = new ComponentDiscordMessageService();
            var originalChannel = new ChannelCapture(10UL, 1UL, "general");
            var mirrorChannel = new ChannelCapture(20UL, 1UL, "general-de");
            var originalMessage = originalChannel.RegisterExistingMessage(900UL, "**Alice**:\nHello");
            var mirrorMessage = mirrorChannel.RegisterExistingMessage(920UL, "**Alice** (EN to DE):\nHallo");
            originalMessage.SetReaction(new Emoji("👍"), normalCount: 1, isMe: false);
            discordMessageService.SetReactionUsers(originalMessage.Message, new Emoji("👍"), [CreateUser(isBot: false)]);
            discordMessageService.RegisterChannel(originalChannel);
            discordMessageService.RegisterChannel(mirrorChannel);

            var hostedService = CreateHostedService(discordMessageService, translationProvider.Object);

            await hostedService.HandleReactionAddedAsync(new DiscordReactionEvent
            {
                MessageId = 900UL,
                ChannelId = 10UL,
                Emote = new Emoji("👍"),
                ReactionType = ReactionType.Normal,
                UserId = 12345UL
            });

            Assert.Single(discordMessageService.AddedReactions);
            Assert.Same(mirrorMessage.Message, discordMessageService.AddedReactions[0].Message);
            Assert.Equal("unicode:👍", discordMessageService.AddedReactions[0].EmoteKey);
            Assert.Empty(discordMessageService.RemovedReactions);
        }

        private DiscordGatewayHostedService CreateHostedService(
            ComponentDiscordMessageService discordMessageService,
            ITranslationProvider translationProvider,
            HttpClient? discordCdnHttpClient = null)
        {
            var translationService = new TranslationService(
                _dbService,
                translationProvider,
                NullLogger<TranslationService>.Instance,
                new NullLoggerFactory());

            var httpClientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            httpClientFactoryMock
                .Setup(factory => factory.CreateClient(HttpClientNames.DiscordCdn))
                .Returns(discordCdnHttpClient ?? new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK))));
            var discordMessagePublisherService = new DiscordMessagePublisherService(
                httpClientFactoryMock.Object,
                NullLogger<DiscordMessagePublisherService>.Instance);
            var replyMirroringService = new ReplyMirroringService(_dbService);
            var mirroredMessageRoutingService = new MirroredMessageRoutingService(
                _dbService,
                translationService,
                replyMirroringService,
                discordMessageService,
                discordMessagePublisherService,
                NullLogger<MirroredMessageRoutingService>.Instance);
            var editedMessageSyncService = new EditedMessageSyncService(
                _dbService,
                translationService,
                discordMessageService,
                discordMessagePublisherService,
                NullLogger<EditedMessageSyncService>.Instance);
            var linkedMessageDeletionService = new LinkedMessageDeletionService(
                discordMessageService,
                _dbService,
                NullLogger<LinkedMessageDeletionService>.Instance);
            var reactionMirroringService = new ReactionMirroringService(
                discordMessageService,
                _dbService,
                NullLogger<ReactionMirroringService>.Instance);
            var eventProcessor = new DiscordGatewayEventProcessorService(
                mirroredMessageRoutingService,
                editedMessageSyncService,
                linkedMessageDeletionService,
                reactionMirroringService,
                NullLogger<DiscordGatewayEventProcessorService>.Instance);

            var dispatcherMock = new Mock<IDiscordGatewayEventDispatcher>();

            return new DiscordGatewayHostedService(
                null!,
                null!,
                _dbService,
                dispatcherMock.Object,
                eventProcessor,
                null!,
                null!,
                NullLogger<DiscordGatewayHostedService>.Instance);
        }

        private static Mock<ITranslationProvider> CreateTranslationProvider(
            Dictionary<string, string>? detections = null,
            Dictionary<string, LanguageAnalysisResult>? analyses = null,
            Dictionary<(string Text, string TargetLanguage), string>? translations = null)
        {
            detections ??= [];
            analyses ??= [];
            translations ??= [];

            var providerMock = new Mock<ITranslationProvider>();
            providerMock.SetupGet(provider => provider.IsActive).Returns(true);
            providerMock.SetupGet(provider => provider.UsesCharacterQuota).Returns(false);
            providerMock
                .Setup(provider => provider.AnalyzeLanguageAsync(It.IsAny<string>()))
                .ReturnsAsync((string text) =>
                {
                    if (analyses.TryGetValue(text, out var analysis))
                    {
                        return analysis;
                    }

                    if (detections.TryGetValue(text, out var detectedLanguage))
                    {
                        return LanguageAnalysisResult.SingleLanguage(detectedLanguage);
                    }

                    throw new InvalidOperationException($"No language analysis configured for '{text}'.");
                });
            providerMock
                .Setup(provider => provider.TranslateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync((string text, string targetLanguage, string? _) =>
                {
                    var normalizedTargetLanguage = LanguageCodeService.NormalizeLanguageCode(targetLanguage);
                    if (translations.TryGetValue((text, normalizedTargetLanguage), out var translatedText))
                    {
                        return translatedText;
                    }

                    if (translations.TryGetValue((text, targetLanguage), out translatedText))
                    {
                        return translatedText;
                    }

                    throw new InvalidOperationException($"No translation configured for '{text}' -> '{targetLanguage}'.");
                });
            return providerMock;
        }

        private static Mock<IUserMessage> CreateIncomingMessage(
            ulong messageId,
            IMessageChannel channel,
            IUser author,
            string content,
            MessageReference? messageReference = null,
            IReadOnlyCollection<IAttachment>? attachments = null)
        {
            var messageMock = new Mock<IUserMessage>();
            messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(messageId);
            messageMock.SetupGet(message => message.Channel).Returns(channel);
            messageMock.SetupGet(message => message.Author).Returns(author);
            messageMock.SetupGet(message => message.Content).Returns(content);
            messageMock.SetupGet(message => message.Source).Returns(MessageSource.User);
            messageMock.SetupGet(message => message.Reference).Returns(messageReference!);
            messageMock.SetupGet(message => message.Attachments).Returns(attachments ?? Array.Empty<IAttachment>());
            messageMock.SetupGet(message => message.Reactions).Returns(new Dictionary<IEmote, ReactionMetadata>());
            return messageMock;
        }

        private static IGuildUser CreateGuildUser(string username, string? nickname = null, bool isBot = false)
        {
            var userMock = new Mock<IGuildUser>();
            userMock.SetupGet(user => user.Username).Returns(username);
            userMock.SetupGet(user => user.Nickname).Returns(nickname!);
            userMock.SetupGet(user => user.GlobalName).Returns((string)null!);
            userMock.SetupGet(user => user.IsBot).Returns(isBot);
            return userMock.Object;
        }

        private static IUser CreateUser(bool isBot)
        {
            var userMock = new Mock<IUser>();
            userMock.SetupGet(user => user.IsBot).Returns(isBot);
            return userMock.Object;
        }

        private static ReactionMetadata CreateReactionMetadata(int normalCount, bool isMe)
        {
            object boxed = default(ReactionMetadata);
            SetReactionMetadataField(boxed, "<ReactionCount>k__BackingField", normalCount);
            SetReactionMetadataField(boxed, "<IsMe>k__BackingField", isMe);
            SetReactionMetadataField(boxed, "<BurstCount>k__BackingField", 0);
            SetReactionMetadataField(boxed, "<NormalCount>k__BackingField", normalCount);
            SetReactionMetadataField(boxed, "<BurstColors>k__BackingField", Array.Empty<Color>());
            return (ReactionMetadata)boxed;
        }

        private static void SetReactionMetadataField(object boxedReactionMetadata, string fieldName, object value)
        {
            var field = typeof(ReactionMetadata).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Could not find ReactionMetadata field '{fieldName}'.");
            field.SetValue(boxedReactionMetadata, value);
        }

        private static void AssertJumpButtons(
            MessageComponent? components,
            IReadOnlyDictionary<string, string> expectedButtons)
        {
            Assert.NotNull(components);
            var actualButtons = ExtractButtonLinks(components!);
            Assert.Equal(expectedButtons.Count, actualButtons.Count);

            foreach (var expectedButton in expectedButtons)
            {
                Assert.True(actualButtons.TryGetValue(expectedButton.Key, out var actualUrl), $"Missing jump button '{expectedButton.Key}'.");
                Assert.Equal(expectedButton.Value, actualUrl);
            }
        }

        private static Dictionary<string, string> ExtractButtonLinks(MessageComponent components)
        {
            var buttons = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var topLevelComponent in components.Components)
            {
                if (topLevelComponent is ButtonComponent button)
                {
                    buttons[button.Label] = button.Url ?? string.Empty;
                    continue;
                }

                if (topLevelComponent is not ActionRowComponent row)
                {
                    continue;
                }

                foreach (var rowButton in row.Components.OfType<ButtonComponent>())
                {
                    buttons[rowButton.Label] = rowButton.Url ?? string.Empty;
                }
            }

            return buttons;
        }

        private sealed class ComponentDiscordMessageService : IDiscordMessageService
        {
            private readonly Dictionary<ulong, IMessageChannel?> _channels = [];
            private readonly Dictionary<IMessage, Dictionary<IEmote, ReactionMetadata>> _messageReactions = [];
            private readonly Dictionary<(IMessage Message, string EmoteKey), List<IReadOnlyCollection<IUser>>> _reactionUsers = [];
            private readonly IUser _botUser = CreateUser(isBot: true);

            public ulong CurrentUserId { get; set; } = 424242UL;

            public List<(ulong ChannelId, ulong MessageId)> DeletedMessages { get; } = [];

            public List<ReactionOperation> AddedReactions { get; } = [];

            public List<ReactionOperation> RemovedReactions { get; } = [];

            public Task<IMessageChannel?> GetChannelAsync(ulong channelId)
            {
                _channels.TryGetValue(channelId, out var channel);
                return Task.FromResult(channel);
            }

            public Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId)
            {
                DeletedMessages.Add((channelId, messageId));
                return Task.FromResult(true);
            }

            public Task AddReactionAsync(IMessage message, IEmote emote)
            {
                AddedReactions.Add(new ReactionOperation(message, ReactionMirroringService.GetReactionKey(emote)));
                if (_messageReactions.TryGetValue(message, out var reactions))
                {
                    reactions[emote] = CreateReactionMetadata(1, isMe: true);
                }

                return Task.CompletedTask;
            }

            public Task RemoveOwnReactionAsync(IMessage message, IEmote emote)
            {
                RemovedReactions.Add(new ReactionOperation(message, ReactionMirroringService.GetReactionKey(emote)));
                if (_messageReactions.TryGetValue(message, out var reactions))
                {
                    var matchedEmote = reactions.Keys.FirstOrDefault(key => ReactionMirroringService.GetReactionKey(key) == ReactionMirroringService.GetReactionKey(emote));
                    if (matchedEmote != null)
                    {
                        reactions.Remove(matchedEmote);
                    }
                }

                return Task.CompletedTask;
            }

            public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(IMessage message, IEmote emote, int limit)
            {
                var emoteKey = ReactionMirroringService.GetReactionKey(emote);
                if (_reactionUsers.TryGetValue((message, emoteKey), out var pages))
                {
                    return YieldPagesAsync(pages);
                }

                if (_messageReactions.TryGetValue(message, out var reactions))
                {
                    var matchedEmote = reactions.Keys.FirstOrDefault(key => ReactionMirroringService.GetReactionKey(key) == emoteKey);
                    if (matchedEmote != null && reactions[matchedEmote].IsMe)
                    {
                        return YieldPagesAsync([[ _botUser ]]);
                    }
                }

                return YieldPagesAsync([]);
            }

            public void RegisterChannel(ChannelCapture channelCapture)
            {
                _channels[channelCapture.Id] = channelCapture.Channel;
                foreach (var message in channelCapture.AllMessages)
                {
                    _messageReactions[message.Message] = message.Reactions;
                }
            }

            public void SetReactionUsers(IMessage message, IEmote emote, params IReadOnlyCollection<IUser>[] pages)
            {
                _reactionUsers[(message, ReactionMirroringService.GetReactionKey(emote))] = pages.ToList();
            }

            private static async IAsyncEnumerable<IReadOnlyCollection<IUser>> YieldPagesAsync(IEnumerable<IReadOnlyCollection<IUser>> pages)
            {
                foreach (var page in pages)
                {
                    yield return page;
                    await Task.Yield();
                }
            }
        }

        private sealed class ChannelCapture
        {
            private readonly Dictionary<ulong, TrackedUserMessage> _messages = [];
            private ulong _nextMessageId;

            public ChannelCapture(ulong channelId, ulong guildId, string name, ulong maxUploadLimit = 26214400UL)
            {
                Id = channelId;
                Name = name;
                GuildId = guildId;
                _nextMessageId = channelId * 1000UL;

                var guildMock = new Mock<IGuild>();
                guildMock.As<ISnowflakeEntity>().SetupGet(guild => guild.Id).Returns(guildId);
                guildMock.SetupGet(guild => guild.MaxUploadLimit).Returns(maxUploadLimit);

                var channelMock = new Mock<IMessageChannel>();
                channelMock.As<ISnowflakeEntity>().SetupGet(channel => channel.Id).Returns(channelId);
                channelMock.As<IChannel>().SetupGet(channel => channel.Name).Returns(name);
                channelMock.As<IGuildChannel>().SetupGet(channel => channel.Guild).Returns(guildMock.Object);
                channelMock.As<IGuildChannel>().SetupGet(channel => channel.GuildId).Returns(guildId);
                channelMock
                    .Setup(channel => channel.GetMessageAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                    .ReturnsAsync((ulong messageId, CacheMode _, RequestOptions? _) =>
                        _messages.TryGetValue(messageId, out var trackedMessage) ? trackedMessage.Message : null);
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
                    .ReturnsAsync((
                        string text,
                        bool _,
                        Embed? __,
                        RequestOptions? ___,
                        AllowedMentions? ____,
                        MessageReference? messageReference,
                        MessageComponent? components,
                        ISticker[]? _____,
                        Embed[]? ______,
                        MessageFlags _______,
                        PollProperties? ________) =>
                    {
                        var trackedMessage = RegisterExistingMessage(++_nextMessageId, text);
                        trackedMessage.ReplyReference = messageReference;
                        trackedMessage.Components = components;
                        SentMessages.Add(trackedMessage);
                        return trackedMessage.Message;
                    });
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
                    .ReturnsAsync((
                        IEnumerable<FileAttachment> files,
                        string text,
                        bool _,
                        Embed? __,
                        RequestOptions? ___,
                        AllowedMentions? ____,
                        MessageReference? messageReference,
                        MessageComponent? components,
                        ISticker[]? _____,
                        Embed[]? ______,
                        MessageFlags _______,
                        PollProperties? ________) =>
                    {
                        var trackedMessage = RegisterExistingMessage(++_nextMessageId, text);
                        trackedMessage.ReplyReference = messageReference;
                        trackedMessage.Components = components;
                        trackedMessage.AttachedFileCount = files.Count();
                        SentMessages.Add(trackedMessage);
                        return trackedMessage.Message;
                    });

                Channel = channelMock.Object;
            }

            public ulong Id { get; }

            public ulong GuildId { get; }

            public string Name { get; }

            public IMessageChannel Channel { get; }

            public List<TrackedUserMessage> SentMessages { get; } = [];

            public IEnumerable<TrackedUserMessage> AllMessages => _messages.Values;

            public TrackedUserMessage RegisterExistingMessage(ulong messageId, string content)
            {
                var trackedMessage = new TrackedUserMessage(messageId, Channel, content);
                _messages[messageId] = trackedMessage;
                return trackedMessage;
            }
        }

        private sealed class TrackedUserMessage
        {
            public TrackedUserMessage(ulong messageId, IMessageChannel channel, string content)
            {
                Id = messageId;
                Content = content;
                Reactions = [];

                var messageMock = new Mock<IUserMessage>();
                messageMock.As<ISnowflakeEntity>().SetupGet(message => message.Id).Returns(messageId);
                messageMock.SetupGet(message => message.Channel).Returns(channel);
                messageMock.SetupGet(message => message.Content).Returns(() => Content);
                messageMock.SetupGet(message => message.Reactions).Returns(() => Reactions);
                messageMock
                    .Setup(message => message.ModifyAsync(It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions?>()))
                    .Callback<Action<MessageProperties>, RequestOptions?>((action, _) =>
                    {
                        var properties = new MessageProperties();
                        action(properties);

                        if (properties.Content.IsSpecified)
                        {
                            Content = properties.Content.Value ?? string.Empty;
                        }

                        if (properties.Components.IsSpecified)
                        {
                            Components = properties.Components.Value;
                        }

                        ModifyCallCount++;
                    })
                    .Returns(Task.CompletedTask);

                Message = messageMock.Object;
            }

            public ulong Id { get; }

            public IUserMessage Message { get; }

            public string Content { get; private set; }

            public MessageReference? ReplyReference { get; set; }

            public MessageComponent? Components { get; set; }

            public int AttachedFileCount { get; set; }

            public int ModifyCallCount { get; private set; }

            public Dictionary<IEmote, ReactionMetadata> Reactions { get; }

            public void SetReaction(IEmote emote, int normalCount, bool isMe)
            {
                Reactions[emote] = CreateReactionMetadata(normalCount, isMe);
            }
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

        private sealed record ReactionOperation(IMessage Message, string EmoteKey);
    }
}
