using System.Collections.Generic;
using System.IO;
using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tsumari.Bot.Configuration;

namespace Tsumari.Bot.Services
{
    public class DiscordMessagePublisherService
    {
        private const int MaxParallelAttachmentDownloads = 4;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDiscordRestRetryPolicy _retryPolicy;
        private readonly DiscordRestOptions _options;
        private readonly ILogger<DiscordMessagePublisherService> _logger;

        public DiscordMessagePublisherService(
            IHttpClientFactory httpClientFactory,
            IDiscordRestRetryPolicy retryPolicy,
            IOptions<DiscordRestOptions> options,
            ILogger<DiscordMessagePublisherService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _retryPolicy = retryPolicy;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<List<MediaAsset>> DownloadMediaAssetsAsync(IReadOnlyCollection<IAttachment>? attachments)
        {
            if (attachments == null || attachments.Count == 0)
            {
                return [];
            }

            var attachmentList = attachments.ToArray();
            var downloadedAssets = new MediaAsset?[attachmentList.Length];
            using var discordCdnClient = _httpClientFactory.CreateClient(HttpClientNames.DiscordCdn);
            using var throttler = new SemaphoreSlim(Math.Min(MaxParallelAttachmentDownloads, attachmentList.Length));

            var downloadTasks = attachmentList.Select((attachment, index) => DownloadAttachmentAsync(
                discordCdnClient,
                throttler,
                attachment,
                index,
                downloadedAssets));

            await Task.WhenAll(downloadTasks);

            var mediaAssets = new List<MediaAsset>(attachmentList.Length);
            foreach (var asset in downloadedAssets)
            {
                if (asset != null)
                {
                    mediaAssets.Add(asset);
                }
            }

            return mediaAssets;
        }

        private async Task DownloadAttachmentAsync(
            HttpClient discordCdnClient,
            SemaphoreSlim throttler,
            IAttachment attachment,
            int index,
            MediaAsset?[] downloadedAssets)
        {
            await throttler.WaitAsync();
            try
            {
                using var response = await discordCdnClient.GetAsync(attachment.Url, HttpCompletionOption.ResponseHeadersRead);
                var bytes = await response.ReadBytesWithStatusCheckAsync(
                    _logger,
                    $"downloading Discord attachment '{attachment.Filename}'");
                downloadedAssets[index] = new MediaAsset
                {
                    Filename = attachment.Filename,
                    Bytes = bytes
                };
            }
            catch (Exception ex)
            {
                _logger.LogAttachmentDownloadFailed(ex, attachment.Filename, attachment.Url);
            }
            finally
            {
                throttler.Release();
            }
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
            var editOptions = DiscordRequestOptionsFactory.MessageOptions(_options);
            foreach (var sentMessage in sentMessages)
            {
                try
                {
                    var channelId = sentMessage.Key;
                    var message = sentMessage.Value;
                    await _retryPolicy.ExecuteAsync(
                        () => message.ModifyAsync(properties => properties.Components = finalComponents, options: editOptions),
                        $"edit jump buttons in channel {channelId}");
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
                    var textOptions = DiscordRequestOptionsFactory.MessageOptions(_options);
                    return await _retryPolicy.ExecuteAsync(
                        () => channel.SendMessageAsync(text, components: components, messageReference: replyReference, options: textOptions),
                        $"send message to channel {channel.Id}");
                }

                var fileOptions = DiscordRequestOptionsFactory.FileUploadOptions(_options);
                return await _retryPolicy.ExecuteAsync(
                    () => channel.SendFilesAsync(fileAttachments, text, components: components, messageReference: replyReference, options: fileOptions),
                    $"send files to channel {channel.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogSendWithFilesFailed(ex, channel.Id);
                try
                {
                    var fallbackOptions = DiscordRequestOptionsFactory.MessageOptions(_options);
                    return await _retryPolicy.ExecuteAsync(
                        () => channel.SendMessageAsync(
                            text + "\n*(Media attachments failed to mirror)*",
                            components: components,
                            messageReference: replyReference,
                            options: fallbackOptions),
                        $"fallback send to channel {channel.Id}");
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

            var messageOptions = DiscordRequestOptionsFactory.MessageOptions(_options);
            var fetched = await _retryPolicy.ExecuteAsync(
                () => channel.GetMessageAsync(messageId, options: messageOptions),
                $"fetch message {messageId} from channel {channel.Id}");

            if (fetched is IUserMessage userMessage)
            {
                await _retryPolicy.ExecuteAsync(
                    () => userMessage.ModifyAsync(properties => properties.Content = newText, options: messageOptions),
                    $"modify message {messageId} in channel {channel.Id}");
                return true;
            }

            return false;
        }
    }
}
