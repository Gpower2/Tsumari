using System.Collections.Concurrent;
using System.Collections.Generic;
using Discord;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.Services
{
    public class MirroredMessageRoutingService : IMirroredMessageRoutingService
    {
        // Guards against concurrent routing of the same message by live gateway events and
        // historical sync, which could otherwise produce duplicate mirrored posts.
        private readonly ConcurrentDictionary<ulong, byte> _messagesBeingRouted = new();

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

            if (!_messagesBeingRouted.TryAdd(message.Id, 0))
            {
                _logger.LogSkippingConcurrentRouting(message.Id);
                return;
            }

            try
            {
                await RouteMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogRoutingPipelineFailed(ex, message.Id);
            }
            finally
            {
                _messagesBeingRouted.TryRemove(message.Id, out _);
            }
        }

        public async Task RouteHistoricalMessageAsync(IUserMessage message, DateTimeOffset originalTimestamp)
        {
            if (!_messagesBeingRouted.TryAdd(message.Id, 0))
            {
                _logger.LogSkippingConcurrentRouting(message.Id);
                return;
            }

            try
            {
                await RouteMessageAsync(message, originalTimestamp);
            }
            finally
            {
                _messagesBeingRouted.TryRemove(message.Id, out _);
            }
        }

        private async Task RouteMessageAsync(IUserMessage message, DateTimeOffset? originalTimestamp = null)
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
            var analysisContext = await AnalyzeLanguageAsync(content, channelRoutingContext.TargetLanguageCode);
            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(message.Channel.Id, message.Reference);
            var authorName = await MirroredMessageFormatter.ResolveAuthorDisplayNameAsync(message);
            var attachmentPlan = BuildAttachmentMirroringPlan(message);
            var mediaAssets = attachmentPlan.AttachmentsToDownload.Count > 0
                ? await _discordMessagePublisherService.DownloadMediaAssetsAsync(attachmentPlan.AttachmentsToDownload)
                : [];

            if (isMaster)
            {
                var sourceLanguageInfo = LanguageCodeService.ResolveSourceLanguageInfo(analysisContext.Analysis, currentChannelLanguageCode: null);
                await RouteMasterMessageAsync(
                    message,
                    content,
                    sourceLanguageInfo,
                    authorName,
                    replyContext,
                    mediaAssets,
                    attachmentPlan.HasOversizedAttachments,
                    analysisContext.IsTranslationSourceLanguageHintTrusted ? sourceLanguageInfo.PrimaryLanguageCode : null,
                    originalTimestamp);
                return;
            }

            await RouteLocalizedMessageAsync(
                message,
                content,
                analysisContext.Analysis,
                analysisContext.IsTranslationSourceLanguageHintTrusted,
                authorName,
                replyContext,
                mediaAssets,
                attachmentPlan.HasOversizedAttachments,
                channelRoutingContext,
                originalTimestamp);
        }

        private async Task<LanguageAnalysisContext> AnalyzeLanguageAsync(string content, string? localizedChannelLanguageCode)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    var analysis = await _translationService.AnalyzeLanguageAsync(content);
                    return new LanguageAnalysisContext(
                        analysis,
                        IsTranslationSourceLanguageHintTrusted: analysis.HasClearDominantLanguage != false);
                }
                catch (Exception ex)
                {
                    _logger.LogLanguageDetectionFailedFallbackToEnglish(ex);
                    return new LanguageAnalysisContext(
                        CreateAnalysisFailureFallbackLanguageAnalysis(),
                        IsTranslationSourceLanguageHintTrusted: false);
                }
            }

            return new LanguageAnalysisContext(
                CreateAttachmentOnlyFallbackLanguageAnalysis(localizedChannelLanguageCode),
                IsTranslationSourceLanguageHintTrusted: false);
        }

        private static LanguageAnalysisResult CreateAttachmentOnlyFallbackLanguageAnalysis(string? localizedChannelLanguageCode)
        {
            return !string.IsNullOrWhiteSpace(localizedChannelLanguageCode)
                ? LanguageAnalysisResult.SingleLanguage(LanguageCodeService.NormalizeLanguageCode(localizedChannelLanguageCode))
                : LanguageAnalysisResult.SingleLanguage("EN");
        }

        private static LanguageAnalysisResult CreateAnalysisFailureFallbackLanguageAnalysis()
        {
            return LanguageAnalysisResult.SingleLanguage("EN");
        }

        private async Task RouteMasterMessageAsync(
            IUserMessage message,
            string content,
            SourceLanguageInfo sourceLanguageInfo,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments,
            string? translationSourceLanguageCode,
            DateTimeOffset? originalTimestamp = null)
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
                    sourceLanguageInfo,
                    translationSourceLanguageCode,
                    localizedChannel.TargetLanguageCode,
                    content,
                    ex => _logger.LogMasterTranslationFailedForwardingRaw(ex, message.Id, localizedChannel.TargetLanguageCode),
                    originalTimestamp);

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
            LanguageAnalysisResult languageAnalysis,
            bool isTranslationSourceLanguageHintTrusted,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments,
            ChannelRoutingContext channelRoutingContext,
            DateTimeOffset? originalTimestamp = null)
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

            var sourceLanguageInfo = LanguageCodeService.ResolveSourceLanguageInfo(languageAnalysis, targetLang);
            var translationSourceLanguageCode = isTranslationSourceLanguageHintTrusted
                ? sourceLanguageInfo.PrimaryLanguageCode
                : null;
            var isMatch = LanguageCodeService.AreSameLanguageCode(sourceLanguageInfo.PrimaryLanguageCode, targetLang);

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
                    sourceLanguageInfo,
                    translationSourceLanguageCode,
                    authorName,
                    replyContext,
                    mediaAssets,
                    hasOversizedAttachments,
                    parentChannel,
                    sentMessages,
                    targets,
                    initialComponents,
                    originalTimestamp);
                return;
            }

            await RouteLocalizedMismatchFlowAsync(
                message,
                content,
                sourceLanguageInfo,
                translationSourceLanguageCode,
                authorName,
                replyContext,
                mediaAssets,
                hasOversizedAttachments,
                targetLang,
                parentChannel,
                sentMessages,
                targets,
                initialComponents,
                originalTimestamp);
        }

        private async Task RouteLocalizedMatchFlowAsync(
            IUserMessage message,
            string content,
            SourceLanguageInfo sourceLanguageInfo,
            string? translationSourceLanguageCode,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments,
            IMessageChannel parentChannel,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<JumpLinkTarget> targets,
            MessageComponent initialComponents,
            DateTimeOffset? originalTimestamp = null)
        {
            var parentReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, parentChannel.Id);
            var parentText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                MirroredMessageFormatter.FormatLinkedMessageText(
                    message.Channel.Id,
                    parentChannel.Id,
                    authorName,
                    sourceLanguageInfo,
                    targetLang: null,
                    content,
                    originalTimestamp),
                sourceLanguageInfo.PrimaryLanguageCode,
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
                    sourceLanguageInfo,
                    translationSourceLanguageCode,
                    sibling.TargetLanguageCode,
                    content,
                    ex => _logger.LogMatchFlowSiblingTranslationFailed(ex, sibling.TargetLanguageCode),
                    originalTimestamp);

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
            SourceLanguageInfo sourceLanguageInfo,
            string? translationSourceLanguageCode,
            string authorName,
            ReplyMirroringContext? replyContext,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            bool hasOversizedAttachments,
            string targetLang,
            IMessageChannel parentChannel,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<JumpLinkTarget> targets,
            MessageComponent initialComponents,
            DateTimeOffset? originalTimestamp = null)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                var nativeReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, message.Channel.Id);
                try
                {
                    var nativeTranslation = await _translationService.TranslateTextAsync(content, targetLang, translationSourceLanguageCode);
                    var nativeReplyText = MirroredMessageFormatter.AppendAttachmentMirrorNotice(
                        MirroredMessageFormatter.FormatLinkedMessageText(
                            message.Channel.Id,
                            message.Channel.Id,
                            authorName,
                            sourceLanguageInfo,
                            targetLang,
                            nativeTranslation,
                            originalTimestamp),
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
                    sourceLanguageInfo,
                    targetLang: null,
                    content,
                    originalTimestamp),
                sourceLanguageInfo.PrimaryLanguageCode,
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
                    sourceLanguageInfo,
                    translationSourceLanguageCode,
                    sibling.TargetLanguageCode,
                    content,
                    ex => _logger.LogMismatchFlowSiblingTranslationFailed(ex, sibling.TargetLanguageCode),
                    originalTimestamp);

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
            SourceLanguageInfo sourceLanguageInfo,
            string? translationSourceLanguageCode,
            string? targetLang,
            string content,
            Action<Exception> logTranslationFailure,
            DateTimeOffset? originalTimestamp = null)
        {
            if (!MirroredMessageFormatter.ShouldTranslateLinkedMessage(sourceLanguageInfo.PrimaryLanguageCode, targetLang) || string.IsNullOrWhiteSpace(content))
            {
                return MirroredMessageFormatter.FormatLinkedMessageText(
                    sourceChannelId,
                    destinationChannelId,
                    authorName,
                    sourceLanguageInfo,
                    targetLang,
                    content,
                    originalTimestamp);
            }

            var translationTargetLanguage = targetLang!;
            try
            {
                var translatedText = await _translationService.TranslateTextAsync(content, translationTargetLanguage, translationSourceLanguageCode);
                return MirroredMessageFormatter.FormatLinkedMessageText(
                    sourceChannelId,
                    destinationChannelId,
                    authorName,
                    sourceLanguageInfo,
                    translationTargetLanguage,
                    translatedText,
                    originalTimestamp);
            }
            catch (Exception ex)
            {
                logTranslationFailure(ex);
                return MirroredMessageFormatter.FormatTranslationFailureText(
                    sourceChannelId,
                    destinationChannelId,
                    authorName,
                    sourceLanguageInfo,
                    translationTargetLanguage,
                    content,
                    originalTimestamp);
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
            await _dbService.LinkMessagesAsync(sourceMessage.Id, originalChannelId, sentMessage.Id, destinationChannel.Id, languageCode, sourceMessage.Timestamp);

            if (!string.IsNullOrWhiteSpace(jumpLanguageLabel))
            {
                targets.Add(new JumpLinkTarget
                {
                    ChannelId = destinationChannel.Id,
                    LanguageLabel = jumpLanguageLabel
                });
            }
        }

        private AttachmentMirroringPlanner.AttachmentMirroringPlan BuildAttachmentMirroringPlan(IUserMessage message)
        {
            var attachmentPlan = AttachmentMirroringPlanner.CreatePlan(message);
            if (attachmentPlan.HasOversizedAttachments && message.Channel is IGuildChannel guildChannel)
            {
                _logger.LogOversizedAttachmentsSkipped(
                    message.Id,
                    message.Channel.Id,
                    guildChannel.Guild.MaxUploadLimit,
                    string.Join(", ", attachmentPlan.OversizedFilenames));
            }

            return attachmentPlan;
        }

        private readonly record struct LanguageAnalysisContext(
            LanguageAnalysisResult Analysis,
            bool IsTranslationSourceLanguageHintTrusted);
    }
}
