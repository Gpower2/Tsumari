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
            var channelRoutingContext = await _dbService.GetChannelRoutingContextAsync(message.Channel.Id);
            var isMaster = channelRoutingContext.IsMaster;
            var isLocalized = channelRoutingContext.IsLocalized;

            if (!isMaster && !isLocalized)
            {
                _logger.LogSkippingUnlinkedChannel(message.Id, message.Channel.Id);
                return;
            }

            _logger.LogProcessingMessage(message.Id, message.Channel.Name, isMaster, isLocalized);

            if (!_translationService.IsActive)
            {
                _logger.LogTranslationServiceInactive();
                return;
            }

            var content = message.Content ?? string.Empty;
            var detectedLang = await DetectLanguageAsync(content, channelRoutingContext.TargetLanguageCode);
            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(message.Channel.Id, message.Reference);
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

            await RouteLocalizedMessageAsync(
                message,
                content,
                detectedLang,
                authorName,
                replyContext,
                mediaAssets,
                attachmentPlan.HasOversizedAttachments,
                channelRoutingContext);
        }

        private async Task<string> DetectLanguageAsync(string content, string? localizedChannelLanguageCode)
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

            if (!string.IsNullOrWhiteSpace(localizedChannelLanguageCode))
            {
                return LanguageCodeService.NormalizeLanguageCode(localizedChannelLanguageCode);
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

            var initialComponents = BuildInitialComponents(message);

            foreach (var localizedChannel in localizedChannels)
            {
                var channel = await _discordMessageService.GetChannelAsync(localizedChannel.ChannelId);
                if (channel == null)
                {
                    _logger.LogMirroredTargetChannelNotResolved(message.Id, channelId, localizedChannel.ChannelId);
                    continue;
                }

                var replyReference = ReplyMirroringService.CreateReplyReference(replyContext, channel.Id);
                var textToSend = await BuildLinkedMessageTextAsync(
                    channelId,
                    channel.Id,
                    authorName,
                    sourceLang,
                    localizedChannel.TargetLanguageCode,
                    content,
                    ex => _logger.LogMasterTranslationFailedForwardingRaw(ex, message.Id, localizedChannel.TargetLanguageCode));

                textToSend = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                    textToSend,
                    localizedChannel.TargetLanguageCode,
                    hasOversizedAttachments);

                await SendAndTrackMirrorAsync(
                    message,
                    channelId,
                    localizedChannel.TargetLanguageCode,
                    channel,
                    textToSend,
                    mediaAssets,
                    initialComponents,
                    replyReference,
                    sentMessages,
                    targets,
                    LanguageCodeService.NormalizeLanguageCode(localizedChannel.TargetLanguageCode));
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
            bool hasOversizedAttachments,
            ChannelRoutingContext channelRoutingContext)
        {
            var channelId = message.Channel.Id;
            var parentMasterId = channelRoutingContext.ParentMasterChannelId;
            var targetLang = channelRoutingContext.TargetLanguageCode;

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

            var initialComponents = BuildInitialComponents(message);

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
            IMessageChannel parentChannel,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<JumpLinkTarget> targets,
            MessageComponent initialComponents)
        {
            var parentReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, parentChannel.Id);
            var parentText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                MirroredMessageFormatter.FormatLinkedMessageText(
                    message.Channel.Id,
                    parentChannel.Id,
                    authorName,
                    sourceLang,
                    targetLang: null,
                    content),
                sourceLang,
                hasOversizedAttachments);
            await SendAndTrackMirrorAsync(
                message,
                message.Channel.Id,
                "master",
                parentChannel,
                parentText,
                mediaAssets,
                initialComponents,
                parentReplyReference,
                sentMessages,
                targets);

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
                var siblingText = await BuildLinkedMessageTextAsync(
                    message.Channel.Id,
                    siblingChannel.Id,
                    authorName,
                    sourceLang,
                    sibling.TargetLanguageCode,
                    content,
                    ex => _logger.LogMatchFlowSiblingTranslationFailed(ex, sibling.TargetLanguageCode));

                siblingText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                    siblingText,
                    sibling.TargetLanguageCode,
                    hasOversizedAttachments);

                await SendAndTrackMirrorAsync(
                    message,
                    message.Channel.Id,
                    sibling.TargetLanguageCode,
                    siblingChannel,
                    siblingText,
                    mediaAssets,
                    initialComponents,
                    siblingReplyReference,
                    sentMessages,
                    targets,
                    LanguageCodeService.NormalizeLanguageCode(sibling.TargetLanguageCode));
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
                        MirroredMessageFormatter.FormatLinkedMessageText(
                            message.Channel.Id,
                            message.Channel.Id,
                            authorName,
                            sourceLang,
                            targetLang,
                            nativeTranslation),
                        targetLang,
                        hasOversizedAttachments);
                    await SendAndTrackMirrorAsync(
                        message,
                        message.Channel.Id,
                        targetLang,
                        message.Channel,
                        nativeReplyText,
                        [],
                        components: null,
                        nativeReplyReference,
                        sentMessages,
                        targets);
                }
                catch (Exception ex)
                {
                    _logger.LogMismatchFlowNativeReplyTranslationFailed(ex, message.Channel.Id);
                }
            }

            var parentReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, parentChannel.Id);
            var parentText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                MirroredMessageFormatter.FormatLinkedMessageText(
                    message.Channel.Id,
                    parentChannel.Id,
                    authorName,
                    sourceLang,
                    targetLang: null,
                    content),
                sourceLang,
                hasOversizedAttachments);
            await SendAndTrackMirrorAsync(
                message,
                message.Channel.Id,
                "master",
                parentChannel,
                parentText,
                mediaAssets,
                initialComponents,
                parentReplyReference,
                sentMessages,
                targets);

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
                var siblingText = await BuildLinkedMessageTextAsync(
                    message.Channel.Id,
                    siblingChannel.Id,
                    authorName,
                    sourceLang,
                    sibling.TargetLanguageCode,
                    content,
                    ex => _logger.LogMismatchFlowSiblingTranslationFailed(ex, sibling.TargetLanguageCode));

                siblingText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                    siblingText,
                    sibling.TargetLanguageCode,
                    hasOversizedAttachments);

                await SendAndTrackMirrorAsync(
                    message,
                    message.Channel.Id,
                    sibling.TargetLanguageCode,
                    siblingChannel,
                    siblingText,
                    mediaAssets,
                    initialComponents,
                    siblingReplyReference,
                    sentMessages,
                    targets,
                    LanguageCodeService.NormalizeLanguageCode(sibling.TargetLanguageCode));
            }

            await _discordMessagePublisherService.EditJumpButtonsIntoSentMessagesAsync(message, sentMessages, targets);
        }

        private static MessageComponent BuildInitialComponents(IMessage message)
        {
            return new ComponentBuilder()
                .WithButton("Original", style: ButtonStyle.Link, url: MirroredMessageFormatter.BuildJumpUrl(message))
                .Build();
        }

        private async Task<string> BuildLinkedMessageTextAsync(
            ulong sourceChannelId,
            ulong destinationChannelId,
            string authorName,
            string sourceLang,
            string? targetLang,
            string content,
            Action<Exception> logTranslationFailure)
        {
            if (!MirroredMessageFormatter.ShouldTranslateLinkedMessage(sourceLang, targetLang) || string.IsNullOrWhiteSpace(content))
            {
                return MirroredMessageFormatter.FormatLinkedMessageText(
                    sourceChannelId,
                    destinationChannelId,
                    authorName,
                    sourceLang,
                    targetLang,
                    content);
            }

            var translationTargetLanguage = targetLang!;
            try
            {
                var translatedText = await _translationService.TranslateTextAsync(content, translationTargetLanguage);
                return MirroredMessageFormatter.FormatLinkedMessageText(
                    sourceChannelId,
                    destinationChannelId,
                    authorName,
                    sourceLang,
                    translationTargetLanguage,
                    translatedText);
            }
            catch (Exception ex)
            {
                logTranslationFailure(ex);
                return MirroredMessageFormatter.FormatTranslationFailureText(
                    sourceChannelId,
                    destinationChannelId,
                    authorName,
                    sourceLang,
                    translationTargetLanguage,
                    content);
            }
        }

        private async Task SendAndTrackMirrorAsync(
            IUserMessage sourceMessage,
            ulong originalChannelId,
            string languageCode,
            IMessageChannel destinationChannel,
            string text,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            MessageComponent? components,
            MessageReference? replyReference,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<JumpLinkTarget> targets,
            string? jumpLanguageLabel = null)
        {
            var sentMessage = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                destinationChannel,
                text,
                mediaAssets,
                components,
                replyReference);

            if (sentMessage == null)
            {
                return;
            }

            sentMessages[destinationChannel.Id] = sentMessage;
            await _dbService.LinkMessagesAsync(sourceMessage.Id, originalChannelId, sentMessage.Id, destinationChannel.Id, languageCode);

            if (!string.IsNullOrWhiteSpace(jumpLanguageLabel))
            {
                targets.Add(new JumpLinkTarget
                {
                    ChannelId = destinationChannel.Id,
                    LanguageLabel = jumpLanguageLabel
                });
            }
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
