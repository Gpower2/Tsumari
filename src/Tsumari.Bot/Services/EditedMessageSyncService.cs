using Discord;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Models;

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
                _logger.LogSkippingNonUserEditedMessage(afterMessage.Id, afterMessage.GetType().Name);
                return;
            }

            if (message.Author.IsBot || message.Source != MessageSource.User)
            {
                _logger.LogSkippingBotOrNonUserEdit(message.Id, message.Source);
                return;
            }

            try
            {
                var afterContent = message.Content ?? string.Empty;
                if (!ShouldProcessEditedMessage(hadCachedSnapshot, beforeContent ?? string.Empty, afterContent))
                {
                    _logger.LogSkippingUnchangedEditedMessage(message.Id);
                    return;
                }

                await SynchronizeEditedMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogEditedMessageHandlingFailed(ex, message.Id);
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
                    _logger.LogSkippingEditedMessageWithoutMirrors(message.Id);
                    return;
                }

                var afterContent = message.Content ?? string.Empty;
                var sourceChannelLang = await _dbService.GetTargetLanguageCodeAsync(message.Channel.Id);
                LanguageAnalysisResult languageAnalysis;
                var isTranslationSourceLanguageHintTrusted = false;
                if (string.IsNullOrWhiteSpace(afterContent))
                {
                    languageAnalysis = CreateAttachmentOnlyFallbackLanguageAnalysis(sourceChannelLang);
                }
                else
                {
                    try
                    {
                        languageAnalysis = await _translationService.AnalyzeLanguageAsync(afterContent);
                        isTranslationSourceLanguageHintTrusted = languageAnalysis.HasClearDominantLanguage != false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEditedMessageLanguageDetectionFailed(ex, message.Id);
                        languageAnalysis = CreateAnalysisFailureFallbackLanguageAnalysis();
                    }
                }

                var authorName = MirroredMessageFormatter.ResolveAuthorDisplayName(message.Author);
                var sourceLanguageInfo = LanguageCodeService.ResolveSourceLanguageInfo(languageAnalysis, sourceChannelLang);
                var translationSourceLanguageCode = isTranslationSourceLanguageHintTrusted
                    ? sourceLanguageInfo.PrimaryLanguageCode
                    : null;

                foreach (var link in mirroredMessages)
                {
                    var mirroredMessageId = link.MirroredMessageId;
                    var mirroredChannelId = link.ChannelId;

                    try
                    {
                        var channel = await _discordMessageService.GetChannelAsync(mirroredChannelId);
                        if (channel == null)
                        {
                            _logger.LogEditedMirroredChannelNotResolved(message.Id, mirroredChannelId, mirroredMessageId);
                            continue;
                        }

                        var configuredTargetLang = await _dbService.GetTargetLanguageCodeAsync(mirroredChannelId);
                        var targetLang = MirroredMessageFormatter.ResolveLinkedMessageTargetLanguageCode(link.LanguageCode, configuredTargetLang);
                        string newText;

                        if (!MirroredMessageFormatter.ShouldTranslateLinkedMessage(sourceLanguageInfo.PrimaryLanguageCode, targetLang) || string.IsNullOrWhiteSpace(afterContent))
                        {
                            newText = MirroredMessageFormatter.FormatLinkedMessageText(
                                message.Channel.Id,
                                mirroredChannelId,
                                authorName,
                                sourceLanguageInfo,
                                targetLang,
                                afterContent);
                        }
                        else
                        {
                            var translationTargetLang = targetLang!;
                            try
                            {
                                var translatedText = await _translationService.TranslateTextAsync(afterContent, translationTargetLang, translationSourceLanguageCode);
                                newText = MirroredMessageFormatter.FormatLinkedMessageText(
                                    message.Channel.Id,
                                    mirroredChannelId,
                                    authorName,
                                    sourceLanguageInfo,
                                    translationTargetLang,
                                    translatedText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogEditedMessageRetranslationFailed(ex, message.Id, translationTargetLang);
                                newText = MirroredMessageFormatter.FormatTranslationFailureText(
                                    message.Channel.Id,
                                    mirroredChannelId,
                                    authorName,
                                    sourceLanguageInfo,
                                    translationTargetLang,
                                    afterContent);
                            }
                        }

                        var modified = await _discordMessagePublisherService.TryModifyMessageContentAsync(channel, mirroredMessageId, newText);
                        if (!modified)
                        {
                            _logger.LogEditedMirroredMessageNotFetched(mirroredMessageId, mirroredChannelId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEditedMirroredMessageUpdateFailed(ex, mirroredMessageId, message.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogEditedMessageProcessingFailed(ex, message.Id);
            }
        }

        private static LanguageAnalysisResult CreateAttachmentOnlyFallbackLanguageAnalysis(string? sourceChannelLanguageCode)
        {
            return !string.IsNullOrWhiteSpace(sourceChannelLanguageCode)
                ? LanguageAnalysisResult.SingleLanguage(LanguageCodeService.NormalizeLanguageCode(sourceChannelLanguageCode))
                : LanguageAnalysisResult.SingleLanguage("EN");
        }

        private static LanguageAnalysisResult CreateAnalysisFailureFallbackLanguageAnalysis()
        {
            return LanguageAnalysisResult.SingleLanguage("EN");
        }
    }
}
