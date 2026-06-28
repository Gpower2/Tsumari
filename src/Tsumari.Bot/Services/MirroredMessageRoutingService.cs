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
                return;
            }

            if (message.Source != MessageSource.User)
            {
                return;
            }

            var hasAttachments = message.Attachments != null && message.Attachments.Count > 0;
            if (string.IsNullOrWhiteSpace(message.Content) && !hasAttachments)
            {
                return;
            }

            try
            {
                await RouteMessageAsync(message, hasAttachments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error inside routing pipeline for message {MsgId}.", message.Id);
            }
        }

        private async Task RouteMessageAsync(IUserMessage message, bool hasAttachments)
        {
            var channelId = message.Channel.Id;

            var isMaster = await _dbService.IsMasterChannelAsync(channelId);
            var isLocalized = await _dbService.IsLocalizedChannelAsync(channelId);

            if (!isMaster && !isLocalized)
            {
                return;
            }

            _logger.LogInformation(
                "Processing message {Id} in channel {ChanName} (Master: {Master}, Local: {Local})",
                message.Id,
                message.Channel.Name,
                isMaster,
                isLocalized);

            if (!_translationService.IsActive)
            {
                _logger.LogWarning("Translation service is currently inactive. Outbound routing aborted.");
                return;
            }

            var content = message.Content ?? string.Empty;
            var detectedLang = await DetectLanguageAsync(content, isLocalized, channelId);
            var replyContext = await _replyMirroringService.ResolveReplyContextAsync(channelId, message.Reference);
            var mediaAssets = hasAttachments
                ? await _discordMessagePublisherService.DownloadMediaAssetsAsync(message.Attachments)
                : [];
            var authorName = MirroredMessageFormatter.ResolveAuthorDisplayName(message.Author);

            if (isMaster)
            {
                var sourceLang = LanguageCodeService.NormalizeLanguageCode(detectedLang);
                await RouteMasterMessageAsync(message, content, sourceLang, authorName, replyContext, mediaAssets);
                return;
            }

            await RouteLocalizedMessageAsync(message, content, detectedLang, authorName, replyContext, mediaAssets);
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
                    _logger.LogError(ex, "Failed to run language detection. Fallback to EN.");
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
            IReadOnlyCollection<MediaAsset> mediaAssets)
        {
            var channelId = message.Channel.Id;
            var localizedChannels = await _dbService.GetLocalizedChannelsForMasterAsync(channelId);

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
                        _logger.LogError(ex, "Failed to translate message {MsgId} to {Lang}. Forwarding raw.", message.Id, localizedChannel.TargetLanguageCode);
                        textToSend = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, $"{content} *(Translation Failed)*");
                    }
                }
                else
                {
                    textToSend = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, content);
                }

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
            IReadOnlyCollection<MediaAsset> mediaAssets)
        {
            var channelId = message.Channel.Id;
            var parentMasterId = await _dbService.GetParentMasterChannelIdAsync(channelId);
            var targetLang = await _dbService.GetTargetLanguageCodeAsync(channelId);

            if (parentMasterId == null || string.IsNullOrWhiteSpace(targetLang))
            {
                _logger.LogError("Channel {Id} has incomplete configuration in localized table.", channelId);
                return;
            }

            var parentChannel = await _discordMessageService.GetChannelAsync(parentMasterId.Value);
            if (parentChannel == null)
            {
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
            string targetLang,
            ulong parentMasterId,
            IMessageChannel parentChannel,
            Dictionary<ulong, IUserMessage> sentMessages,
            List<JumpLinkTarget> targets,
            MessageComponent initialComponents)
        {
            var parentReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, parentChannel.Id);
            var parentText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, content);
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
                        _logger.LogError(ex, "Match Flow: Failed to translate to sibling {Lang}.", sibling.TargetLanguageCode);
                        siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, $"{content} *(Translation Failed)*");
                    }
                }
                else
                {
                    siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, string.Empty);
                }

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
                    var nativeReply = await _discordMessagePublisherService.SendMessageWithFilesAsync(
                        message.Channel,
                        MirroredMessageFormatter.FormatTranslatedReplyText(sourceLang, targetLang, nativeTranslation),
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
                    _logger.LogError(ex, "Mismatch Flow: Failed native translation reply in channel {Id}", message.Channel.Id);
                }
            }

            var parentReplyReference = ReplyMirroringService.CreateReplyReference(replyContext, parentChannel.Id);
            var parentText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, content);
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
                        _logger.LogError(ex, "Mismatch Flow: Failed sibling translation to {Lang}", sibling.TargetLanguageCode);
                        siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, $"{content} *(Translation Failed)*");
                    }
                }
                else
                {
                    siblingText = MirroredMessageFormatter.FormatMirroredAuthorText(authorName, string.Empty);
                }

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
    }
}
