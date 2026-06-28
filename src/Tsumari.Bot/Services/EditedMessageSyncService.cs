using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class EditedMessageSyncService
    {
        private readonly DatabaseService _dbService;
        private readonly TranslationService _translationService;
        private readonly IDiscordMessageService _discordMessageService;
        private readonly DiscordMessagePublisherService _discordMessagePublisherService;
        private readonly ILogger<EditedMessageSyncService> _logger;

        public EditedMessageSyncService(
            DatabaseService dbService,
            TranslationService translationService,
            IDiscordMessageService discordMessageService,
            DiscordMessagePublisherService discordMessagePublisherService,
            ILogger<EditedMessageSyncService> logger)
        {
            _dbService = dbService;
            _translationService = translationService;
            _discordMessageService = discordMessageService;
            _discordMessagePublisherService = discordMessagePublisherService;
            _logger = logger;
        }

        public async Task HandleMessageUpdatedAsync(bool hadCachedSnapshot, string? beforeContent, IMessage afterMessage)
        {
            if (afterMessage is not IUserMessage message)
            {
                return;
            }

            if (message.Author.IsBot || message.Source != MessageSource.User)
            {
                return;
            }

            try
            {
                var afterContent = message.Content ?? string.Empty;
                if (!ShouldProcessEditedMessage(hadCachedSnapshot, beforeContent ?? string.Empty, afterContent))
                {
                    return;
                }

                await SynchronizeEditedMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error handling edited message {MsgId}.", message.Id);
            }
        }

        public static bool ShouldProcessEditedMessage(bool hadCachedSnapshot, string? beforeContent, string? afterContent)
        {
            if (!hadCachedSnapshot)
            {
                return true;
            }

            return !string.Equals(beforeContent ?? string.Empty, afterContent ?? string.Empty, StringComparison.Ordinal);
        }

        private async Task SynchronizeEditedMessageAsync(IUserMessage message)
        {
            try
            {
                await _dbService.EnsureOriginalChannelIdAsync(message.Id, message.Channel.Id);
                var mirroredMessages = await _dbService.GetMirroredMessagesAsync(message.Id);
                if (mirroredMessages.Count == 0)
                {
                    return;
                }

                var afterContent = message.Content ?? string.Empty;
                string detectedLang = "EN";
                if (!string.IsNullOrWhiteSpace(afterContent))
                {
                    try
                    {
                        detectedLang = await _translationService.DetectLanguageAsync(afterContent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to detect language for edited message {MsgId}.", message.Id);
                    }
                }

                var authorName = MirroredMessageFormatter.ResolveAuthorDisplayName(message.Author);
                var sourceChannelLang = await _dbService.GetTargetLanguageCodeAsync(message.Channel.Id);
                var sourceLang = LanguageCodeService.ResolveSourceLanguageCode(detectedLang, sourceChannelLang);

                foreach (var link in mirroredMessages)
                {
                    var mirroredMessageId = link.MirroredMessageId;
                    var mirroredChannelId = link.ChannelId;

                    try
                    {
                        var channel = await _discordMessageService.GetChannelAsync(mirroredChannelId);
                        if (channel == null)
                        {
                            continue;
                        }

                        var configuredTargetLang = await _dbService.GetTargetLanguageCodeAsync(mirroredChannelId);
                        var targetLang = MirroredMessageFormatter.ResolveLinkedMessageTargetLanguageCode(link.LanguageCode, configuredTargetLang);
                        string newText;

                        if (!MirroredMessageFormatter.ShouldTranslateLinkedMessage(sourceLang, targetLang) || string.IsNullOrWhiteSpace(afterContent))
                        {
                            newText = MirroredMessageFormatter.FormatEditedLinkedMessageText(
                                message.Channel.Id,
                                mirroredChannelId,
                                authorName,
                                sourceLang,
                                targetLang,
                                afterContent);
                        }
                        else
                        {
                            var translationTargetLang = targetLang!;
                            try
                            {
                                var translatedText = await _translationService.TranslateTextAsync(afterContent, translationTargetLang);
                                newText = MirroredMessageFormatter.FormatEditedLinkedMessageText(
                                    message.Channel.Id,
                                    mirroredChannelId,
                                    authorName,
                                    sourceLang,
                                    translationTargetLang,
                                    translatedText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to retranslate edited message {MsgId} to {Lang}.", message.Id, translationTargetLang);
                                newText = MirroredMessageFormatter.FormatTranslationFailureText(
                                    message.Channel.Id,
                                    mirroredChannelId,
                                    authorName,
                                    sourceLang,
                                    translationTargetLang,
                                    afterContent);
                            }
                        }

                        var modified = await _discordMessagePublisherService.TryModifyMessageContentAsync(channel, mirroredMessageId, newText);
                        if (!modified)
                        {
                            _logger.LogWarning(
                                "Could not fetch mirrored IUserMessage {MirrorId} in channel {ChanId} for edited message.",
                                mirroredMessageId,
                                mirroredChannelId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error while updating mirrored message {MirrorId} for edited original {MsgId}.",
                            mirroredMessageId,
                            message.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled failure while processing edited message {MsgId}.", message.Id);
            }
        }
    }
}
