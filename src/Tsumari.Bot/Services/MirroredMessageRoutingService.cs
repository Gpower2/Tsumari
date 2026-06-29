using System.Collections.Generic;
using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class MirroredMessageRoutingService
    {
        private readonly DatabaseService _dbService;
        private readonly TranslationService _translationService;
        private readonly ReplyMirroringService _replyMirroringService;
        private readonly IDiscordMessageService _discordMessageService;
        private readonly DiscordMessagePublisherService _discordMessagePublisherService;
        private readonly ILogger<MirroredMessageRoutingService> _logger;

        public MirroredMessageRoutingService(
            DatabaseService dbService,
            TranslationService translationService,
            ReplyMirroringService replyMirroringService,
            IDiscordMessageService discordMessageService,
            DiscordMessagePublisherService discordMessagePublisherService,
            ILogger<MirroredMessageRoutingService> logger)
        {
            _dbService = dbService;
            _translationService = translationService;
            _replyMirroringService = replyMirroringService;
            _discordMessageService = discordMessageService;
            _discordMessagePublisherService = discordMessagePublisherService;
            _logger = logger;
        }

        public async Task HandleMessageReceivedAsync(IMessage rawMessage)
        {
            if (rawMessage is not IUserMessage message)
            {
                _logger.LogSkippingNonUserGatewayMessage(rawMessage.Id, rawMessage.GetType().Name);
                return;
            }

            if (message.Source != MessageSource.User)
            {
                _logger.LogSkippingNonUserMessageSource(message.Id, message.Source);
                return;
            }

            var hasAttachments = message.Attachments != null && message.Attachments.Count > 0;
            if (string.IsNullOrWhiteSpace(message.Content) && !hasAttachments)
            {
                _logger.LogSkippingContentlessMessage(message.Id, message.Channel.Id);
                return;
            }

            try
            {
                await RouteMessageAsync(message, hasAttachments);
            }
            catch (Exception ex)
            {
                _logger.LogRoutingPipelineFailed(ex, message.Id);
            }
        }

        private async Task RouteMessageAsync(IUserMessage message, bool hasAttachments)
        {
            var channelId = message.Channel.Id;

            var isMaster = await _dbService.IsMasterChannelAsync(channelId);
            var isLocalized = await _dbService.IsLocalizedChannelAsync(channelId);

            if (!isMaster && !isLocalized)
            {
                _logger.LogSkippingUnlinkedChannel(message.Id, channelId);
                return;
            }

            _logger.LogProcessingMessage(message.Id, message.Channel.Name, isMaster, isLocalized);

            if (!_translationService.IsActive)
            {
                _logger.LogTranslationServiceInactive();
                return;
            }

            var content = message.Content ?? string.Empty;
            var detectedLang = await DetectLanguageAsync(content, isLocalized, channelId);
            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(channelId, message.Reference);
            var authorName = MirroredMessageFormatter.ResolveAuthorDisplayName(message.Author);
            var attachmentPlan = BuildAttachmentMirroringPlan(message, hasAttachments);
            var mediaAssets = attachmentPlan.AttachmentsToDownload.Count > 0
                ? await _discordMessagePublisherService.DownloadMediaAssetsAsync(attachmentPlan.AttachmentsToDownload)
                : [];

            if (isMaster)
            {
                var sourceLang = LanguageCodeService.NormalizeLanguageCode(detectedLang);
                await RouteMasterMessageAsync(message, content, sourceLang, authorName, replyContext, mediaAssets, attachmentPlan.HasOversizedAttachments);
                return;
            }

            await RouteLocalizedMessageAsync(message, content, detectedLang, authorName, replyContext, mediaAssets, attachmentPlan.HasOversizedAttachments);
        }

        private async Task<string> DetectLanguageAsync(string content, bool isLocalizedChannel, ulong channelId)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    return await _translationService.DetectLanguageAsync(content);
                }
                catch (Exception ex)
                {
                    _logger.LogLanguageDetectionFailedFallbackToEnglish(ex);
                    return "EN";
                }
            }

            if (isLocalizedChannel)
            {
                var localLang = await _dbService.GetTargetLanguageCodeAsync(channelId);
                if (!string.IsNullOrWhiteSpace(localLang))
                {
                    return LanguageCodeService.NormalizeLanguageCode(localLang);
                }
            }

            return "EN";
        }

        private async Task RouteMasterMessageAsync(
            IUserMessage message,
            string content,
            string sourceLang,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments)
        {
            var channelId = message.Channel.Id;
            var localizedChannels = await _dbService.GetLocalizedChannelsForMasterAsync(channelId);
            if (localizedChannels.Count == 0)
            {
                _logger.LogMasterMessageHasNoLocalizedTargets(message.Id, channelId);
            }

            var sentMessages = new Dictionary<ulong, IUserMessage>();
            var targets = new List<JumpLinkTarget>
            {
                new() { ChannelId = channelId, LanguageLabel = "Original" }
            };

            var initialComponents = new ComponentBuilder()
                .WithButton("Original", style: ButtonStyle.Link, url: MirroredMessageFormatter.BuildJumpUrl(message))
                .Build();

            foreach (var localizedChannel in localizedChannels)
            {
                var channel = await _discordMessageService.GetChannelAsync(localizedChannel.ChannelId);
                if (channel == null)
                {
                    _logger.LogMirroredTargetChannelNotResolved(message.Id, channelId, localizedChannel.ChannelId);
                    continue;
                }

                var replyReference = ReplyMirroringService.CreateReplyReference(replyContext, channel.Id);
                string textToSend;

                if (!LanguageCodeService.AreSameLanguageCode(sourceLang, localizedChannel.TargetLanguageCode)
                    && !string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var translatedText = await _translationService.TranslateTextAsync(content, localizedChannel.TargetLanguageCode);
                        textToSend = $"**{authorName}** {MirroredMessageFormatter.FormatLanguagePair(sourceLang, localizedChannel.TargetLanguageCode)}:\n{translatedText}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogMasterTranslationFailedForwardingRaw(ex, message.Id, localizedChannel.TargetLanguageCode);
                        textToSend = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, $"{content} *(Translation Failed)*");
                    }
                }
                else
                {
                    textToSend = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, content);
                }

                textToSend = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                    textToSend,
                    localizedChannel.TargetLanguageCode,
                    hasOversizedAttachments);

                var sentMessage = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                    channel,
                    textToSend,
                    mediaAssets,
                    initialComponents,
                    replyReference);

                if (sentMessage == null)
                {
                    continue;
                }

                sentMessages[channel.Id] = sentMessage;
                await _dbService.LinkMessagesAsync(message.Id, channelId, sentMessage.Id, channel.Id, localizedChannel.TargetLanguageCode);
                targets.Add(new JumpLinkTarget
                {
                    ChannelId = channel.Id,
                    LanguageLabel = LanguageCodeService.NormalizeLanguageCode(localizedChannel.TargetLanguageCode)
                });
            }

            await _discordMessagePublisherService.EditJumpButtonsIntoSentMessagesAsync(message, sentMessages, targets);
        }

        private async Task RouteLocalizedMessageAsync(
            IUserMessage message,
            string content,
            string detectedLang,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments)
        {
            var channelId = message.Channel.Id;
            var parentMasterId = await _dbService.GetParentMasterChannelIdAsync(channelId);
            var targetLang = await _dbService.GetTargetLanguageCodeAsync(channelId);

            if (parentMasterId == null || string.IsNullOrWhiteSpace(targetLang))
            {
                _logger.LogLocalizedChannelConfigurationIncomplete(channelId);
                return;
            }

            var parentChannel = await _discordMessageService.GetChannelAsync(parentMasterId.Value);
            if (parentChannel == null)
            {
                _logger.LogLocalizedParentChannelNotResolved(message.Id, channelId, parentMasterId.Value);
                return;
            }

            var sourceLang = LanguageCodeService.ResolveSourceLanguageCode(detectedLang, targetLang);
            var isMatch = LanguageCodeService.AreSameLanguageCode(sourceLang, targetLang);

            var sentMessages = new Dictionary<ulong, IUserMessage>();
            var targets = new List<JumpLinkTarget>
            {
                new() { ChannelId = channelId, LanguageLabel = LanguageCodeService.NormalizeLanguageCode(targetLang) },
                new() { ChannelId = parentMasterId.Value, LanguageLabel = "Original" }
            };

            var initialComponents = new ComponentBuilder()
                .WithButton("Original", style: ButtonStyle.Link, url: MirroredMessageFormatter.BuildJumpUrl(message))
                .Build();

            if (isMatch)
            {
                await RouteLocalizedMatchFlowAsync(
                    message,
                    content,
                    sourceLang,
                    authorName,
                    replyContext,
                    mediaAssets,
                    hasOversizedAttachments,
                    targetLang,
                    parentMasterId.Value,
                    parentChannel,
                    sentMessages,
                    targets,
                    initialComponents);
                return;
            }

            await RouteLocalizedMismatchFlowAsync(
                message,
                content,
                sourceLang,
                authorName,
                replyContext,
                mediaAssets,
                hasOversizedAttachments,
                targetLang,
                parentMasterId.Value,
                parentChannel,
                sentMessages,
                targets,
                initialComponents);
        }

        private async Task RouteLocalizedMatchFlowAsync(
            IUserMessage message,
            string content,
            string sourceLang,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments,
            string targetLang,
            ulong parentMasterId,
            IMessageChannel parentChannel,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<JumpLinkTarget> targets,
            MessageComponent initialComponents)
        {
            var parentReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, parentChannel.Id);
            var parentText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                MirroredMessageFormatter.FormatMirroredAuthorText(authorName, content),
                sourceLang,
                hasOversizedAttachments);
            var parentSentMessage = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                parentChannel,
                parentText,
                mediaAssets,
                initialComponents,
                parentReplyReference);

            if (parentSentMessage != null)
            {
                sentMessages[parentMasterId] = parentSentMessage;
                await _dbService.LinkMessagesAsync(message.Id, message.Channel.Id, parentSentMessage.Id, parentChannel.Id, "master");
            }

            var siblingChannels = await _dbService.GetSiblingChannelsAsync(message.Channel.Id);
            foreach (var sibling in siblingChannels)
            {
                var siblingChannel = await _discordMessageService.GetChannelAsync(sibling.ChannelId);
                if (siblingChannel == null)
                {
                    _logger.LogMirroredTargetChannelNotResolved(message.Id, message.Channel.Id, sibling.ChannelId);
                    continue;
                }

                var siblingReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, siblingChannel.Id);
                string siblingText;

                if (!string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var translatedText = await _translationService.TranslateTextAsync(content, sibling.TargetLanguageCode);
                        siblingText = $"**{authorName}** {MirroredMessageFormatter.FormatLanguagePair(sourceLang, sibling.TargetLanguageCode)}:\n{translatedText}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogMatchFlowSiblingTranslationFailed(ex, sibling.TargetLanguageCode);
                        siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, $"{content} *(Translation Failed)*");
                    }
                }
                else
                {
                    siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, string.Empty);
                }

                siblingText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                    siblingText,
                    sibling.TargetLanguageCode,
                    hasOversizedAttachments);

                var siblingSentMessage = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                    siblingChannel,
                    siblingText,
                    mediaAssets,
                    initialComponents,
                    siblingReplyReference);

                if (siblingSentMessage == null)
                {
                    continue;
                }

                sentMessages[sibling.ChannelId] = siblingSentMessage;
                await _dbService.LinkMessagesAsync(message.Id, message.Channel.Id, siblingSentMessage.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                targets.Add(new JumpLinkTarget
                {
                    ChannelId = sibling.ChannelId,
                    LanguageLabel = LanguageCodeService.NormalizeLanguageCode(sibling.TargetLanguageCode)
                });
            }

            await _discordMessagePublisherService.EditJumpButtonsIntoSentMessagesAsync(message, sentMessages, targets);
        }

        private async Task RouteLocalizedMismatchFlowAsync(
            IUserMessage message,
            string content,
            string sourceLang,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments,
            string targetLang,
            ulong parentMasterId,
            IMessageChannel parentChannel,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<JumpLinkTarget> targets,
            MessageComponent initialComponents)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                var nativeReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, message.Channel.Id);
                try
                {
                    var nativeTranslation = await _translationService.TranslateTextAsync(content, targetLang);
                    var nativeReplyText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                        MirroredMessageFormatter.FormatTranslatedReplyText(sourceLang, targetLang, nativeTranslation),
                        targetLang,
                        hasOversizedAttachments);
                    var nativeReply = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                        message.Channel,
                        nativeReplyText,
                        [],
                        components: null,
                        nativeReplyReference);

                    if (nativeReply != null)
                    {
                        sentMessages[message.Channel.Id] = nativeReply;
                        await _dbService.LinkMessagesAsync(message.Id, message.Channel.Id, nativeReply.Id, message.Channel.Id, targetLang);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogMismatchFlowNativeReplyTranslationFailed(ex, message.Channel.Id);
                }
            }

            var parentReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, parentChannel.Id);
            var parentText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                MirroredMessageFormatter.FormatMirroredAuthorText(authorName, content),
                sourceLang,
                hasOversizedAttachments);
            var parentSentMessage = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                parentChannel,
                parentText,
                mediaAssets,
                initialComponents,
                parentReplyReference);

            if (parentSentMessage != null)
            {
                sentMessages[parentMasterId] = parentSentMessage;
                await _dbService.LinkMessagesAsync(message.Id, message.Channel.Id, parentSentMessage.Id, parentChannel.Id, "master");
            }

            var siblingChannels = await _dbService.GetSiblingChannelsAsync(message.Channel.Id);
            foreach (var sibling in siblingChannels)
            {
                var siblingChannel = await _discordMessageService.GetChannelAsync(sibling.ChannelId);
                if (siblingChannel == null)
                {
                    _logger.LogMirroredTargetChannelNotResolved(message.Id, message.Channel.Id, sibling.ChannelId);
                    continue;
                }

                var siblingReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, siblingChannel.Id);
                var siblingIsHome = LanguageCodeService.AreSameLanguageCode(sourceLang, sibling.TargetLanguageCode);
                string siblingText;

                if (siblingIsHome)
                {
                    siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, content);
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var translatedText = await _translationService.TranslateTextAsync(content, sibling.TargetLanguageCode);
                        siblingText = $"**{authorName}** {MirroredMessageFormatter.FormatLanguagePair(sourceLang, sibling.TargetLanguageCode)}:\n{translatedText}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogMismatchFlowSiblingTranslationFailed(ex, sibling.TargetLanguageCode);
                        siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, $"{content} *(Translation Failed)*");
                    }
                }
                else
                {
                    siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, string.Empty);
                }

                siblingText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                    siblingText,
                    sibling.TargetLanguageCode,
                    hasOversizedAttachments);

                var siblingSentMessage = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                    siblingChannel,
                    siblingText,
                    mediaAssets,
                    initialComponents,
                    siblingReplyReference);

                if (siblingSentMessage == null)
                {
                    continue;
                }

                sentMessages[sibling.ChannelId] = siblingSentMessage;
                await _dbService.LinkMessagesAsync(message.Id, message.Channel.Id, siblingSentMessage.Id, siblingChannel.Id, sibling.TargetLanguageCode);
                targets.Add(new JumpLinkTarget
                {
                    ChannelId = sibling.ChannelId,
                    LanguageLabel = LanguageCodeService.NormalizeLanguageCode(sibling.TargetLanguageCode)
                });
            }

            await _discordMessagePublisherService.EditJumpButtonsIntoSentMessagesAsync(message, sentMessages, targets);
        }

        private AttachmentMirroringPlan BuildAttachmentMirroringPlan(IUserMessage message, bool hasAttachments)
        {
            if (!hasAttachments || message.Attachments == null || message.Attachments.Count == 0)
            {
                return AttachmentMirroringPlan.Empty;
            }

            if (message.Channel is not IGuildChannel guildChannel || guildChannel.Guild.MaxUploadLimit == 0)
            {
                return new AttachmentMirroringPlan(message.Attachments, HasOversizedAttachments: false);
            }

            var mirrorableAttachments = new List<IAttachment>(message.Attachments.Count);
            var oversizedFilenames = new List<string>();

            foreach (var attachment in message.Attachments)
            {
                if ((ulong)attachment.Size > guildChannel.Guild.MaxUploadLimit)
                {
                    oversizedFilenames.Add(attachment.Filename);
                    continue;
                }

                mirrorableAttachments.Add(attachment);
            }

            if (oversizedFilenames.Count > 0)
            {
                _logger.LogOversizedAttachmentsSkipped(
                    message.Id,
                    message.Channel.Id,
                    guildChannel.Guild.MaxUploadLimit,
                    string.Join(", ", oversizedFilenames));
            }

            return new AttachmentMirroringPlan(mirrorableAttachments, oversizedFilenames.Count > 0);
        }

        private readonly record struct AttachmentMirroringPlan(
            IReadOnlyCollection<IAttachment> AttachmentsToDownload,
            bool HasOversizedAttachments)
        {
            public static AttachmentMirroringPlan Empty { get; } = new([], false);
        }
    }
}
