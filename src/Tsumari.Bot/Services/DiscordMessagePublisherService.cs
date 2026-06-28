using System.Collections.Generic;
using System.IO;
using Discord;
using Microsoft.Extensions.Logging;

namespace Tsumari.Bot.Services
{
    public class DiscordMessagePublisherService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DiscordMessagePublisherService> _logger;

        public DiscordMessagePublisherService(
            IHttpClientFactory httpClientFactory,
            ILogger<DiscordMessagePublisherService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<List<MediaAsset>> DownloadMediaAssetsAsync(IReadOnlyCollection<IAttachment>? attachments)
        {
            var mediaAssets = new List<MediaAsset>();
            if (attachments == null || attachments.Count == 0)
            {
                return mediaAssets;
            }

            using var discordCdnClient = _httpClientFactory.CreateClient(HttpClientNames.DiscordCdn);
            foreach (var attachment in attachments)
            {
                try
                {
                    using var response = await discordCdnClient.GetAsync(attachment.Url, HttpCompletionOption.ResponseHeadersRead);
                    var bytes = await response.ReadBytesWithStatusCheckAsync(
                        _logger,
                        $"downloading Discord attachment '{attachment.Filename}'");
                    mediaAssets.Add(new MediaAsset
                    {
                        Filename = attachment.Filename,
                        Bytes = bytes
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogAttachmentDownloadFailed(ex, attachment.Filename, attachment.Url);
                }
            }

            return mediaAssets;
        }

        public async Task EditJumpButtonsIntoSentMessagesAsync(
            IMessage originalMessage,
            IReadOnlyDictionary<ulong, IUserMessage> sentMessages,
            IReadOnlyCollection<JumpLinkTarget> targets)
        {
            var finalBuilder = new ComponentBuilder()
                .WithButton("Original", style: ButtonStyle.Link, url: MirroredMessageFormatter.BuildJumpUrl(originalMessage));

            foreach (var target in targets)
            {
                if (target.LanguageLabel == "Original")
                {
                    continue;
                }

                if (sentMessages.TryGetValue(target.ChannelId, out var sentMessage))
                {
                    finalBuilder.WithButton(
                        target.LanguageLabel,
                        style: ButtonStyle.Link,
                        url: MirroredMessageFormatter.BuildJumpUrl(sentMessage));
                }
            }

            var finalComponents = finalBuilder.Build();
            foreach (var sentMessage in sentMessages)
            {
                try
                {
                    await sentMessage.Value.ModifyAsync(properties => properties.Components = finalComponents);
                }
                catch (Exception ex)
                {
                    _logger.LogJumpButtonEditFailed(ex, sentMessage.Value.Id, sentMessage.Key);
                }
            }
        }

        public async Task<IUserMessage?> SendMessageWithFilesAsync(
            IMessageChannel channel,
            string text,
            IReadOnlyCollection<MediaAsset> mediaAssets,
            MessageComponent? components,
            MessageReference? replyReference = null)
        {
            if (channel == null)
            {
                return null;
            }

            var fileAttachments = new List<FileAttachment>();
            var streams = new List<MemoryStream>();

            try
            {
                foreach (var asset in mediaAssets)
                {
                    var stream = new MemoryStream(asset.Bytes);
                    streams.Add(stream);
                    fileAttachments.Add(new FileAttachment(stream, asset.Filename));
                }

                if (fileAttachments.Count == 0)
                {
                    return await channel.SendMessageAsync(text, components: components, messageReference: replyReference);
                }

                return await channel.SendFilesAsync(fileAttachments, text, components: components, messageReference: replyReference);
            }
            catch (Exception ex)
            {
                _logger.LogSendWithFilesFailed(ex, channel.Id);
                try
                {
                    return await channel.SendMessageAsync(
                        text + "\n*(Media attachments failed to mirror)*",
                        components: components,
                        messageReference: replyReference);
                }
                catch (Exception sendEx)
                {
                    _logger.LogFallbackSendFailed(sendEx, channel.Id);
                    return null;
                }
            }
            finally
            {
                foreach (var stream in streams)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogMemoryStreamDisposeFailed(ex);
                    }
                }
            }
        }

        public async Task<bool> TryModifyMessageContentAsync(IMessageChannel channel, ulong messageId, string newText)
        {
            if (channel == null)
            {
                return false;
            }

            var fetched = await channel.GetMessageAsync(messageId);
            if (fetched is IUserMessage userMessage)
            {
                await userMessage.ModifyAsync(properties => properties.Content = newText);
                return true;
            }

            return false;
        }
    }
}
