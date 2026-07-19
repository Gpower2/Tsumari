using Discord;
using Tsumari.Bot.Models;

namespace Tsumari.Bot.Services
{
    public static class MirroredMessageFormatter
    {
        public static string ResolveAuthorDisplayName(IUser author)
        {
            ArgumentNullException.ThrowIfNull(author);

            return author is IGuildUser guildUser
                ? guildUser.Nickname ?? guildUser.GlobalName ?? guildUser.Username
                : author.Username;
        }

        /// <summary>
        /// Resolves the display name for a message author, falling back to the guild member
        /// nickname when the author is not already provided as an <see cref="IGuildUser"/>
        /// (for example, messages fetched via REST during historical sync).
        /// </summary>
        public static async Task<string> ResolveAuthorDisplayNameAsync(IUserMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            var author = message.Author;
            if (author is IGuildUser || message.Channel is not IGuildChannel guildChannel)
            {
                return ResolveAuthorDisplayName(author);
            }

            var guildUser = await guildChannel.Guild.GetUserAsync(author.Id);
            return ResolveAuthorDisplayName(guildUser ?? author);
        }

        public static string BuildTimestampPrefix(DateTimeOffset? originalTimestamp)
        {
            if (!originalTimestamp.HasValue)
            {
                return string.Empty;
            }

            return $"<t:{originalTimestamp.Value.ToUnixTimeSeconds()}:f> ";
        }

        public static string FormatLanguagePair(string sourceLang, string targetLang)
        {
            sourceLang = LanguageCodeService.NormalizeLanguageCode(sourceLang);
            targetLang = LanguageCodeService.NormalizeLanguageCode(targetLang);
            return $"({sourceLang} => {targetLang})";
        }

        public static string FormatLanguagePair(SourceLanguageInfo sourceLanguageInfo, string targetLang)
        {
            ArgumentNullException.ThrowIfNull(sourceLanguageInfo);

            targetLang = LanguageCodeService.NormalizeLanguageCode(targetLang);
            if (sourceLanguageInfo.LabelLanguageCodes.Count <= 1)
            {
                return $"({sourceLanguageInfo.PrimaryLanguageCode} => {targetLang})";
            }

            return $"({string.Join(",", sourceLanguageInfo.LabelLanguageCodes)} => {targetLang})";
        }

        public static string BuildJumpUrl(IMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(message.Channel);

            return message.Channel is IGuildChannel guildChannel
                ? $"https://discord.com/channels/{guildChannel.GuildId}/{message.Channel.Id}/{message.Id}"
                : $"https://discord.com/channels/@me/{message.Channel.Id}/{message.Id}";
        }

        public static string FormatTranslatedReplyText(string sourceLang, string targetLang, string translatedText, DateTimeOffset? originalTimestamp = null)
        {
            var sourceLanguageInfo = new SourceLanguageInfo(
                LanguageCodeService.NormalizeLanguageCode(sourceLang),
                [LanguageCodeService.NormalizeLanguageCode(sourceLang)]);
            return FormatTranslatedReplyText(sourceLanguageInfo, targetLang, translatedText, originalTimestamp);
        }

        public static string FormatTranslatedReplyText(SourceLanguageInfo sourceLanguageInfo, string targetLang, string translatedText, DateTimeOffset? originalTimestamp = null)
        {
            var prefix = $"*{FormatLanguagePair(sourceLanguageInfo, targetLang)}:*";
            var timestampPrefix = BuildTimestampPrefix(originalTimestamp);
            return string.IsNullOrWhiteSpace(translatedText)
                ? $"{timestampPrefix}{prefix}"
                : $"{timestampPrefix}{prefix} {translatedText}";
        }

        public static string FormatMirroredAuthorText(string authorName, string content, DateTimeOffset? originalTimestamp = null)
        {
            var prefix = BuildTimestampPrefix(originalTimestamp);
            return string.IsNullOrWhiteSpace(content)
                ? $"{prefix}**{authorName}**:"
                : $"{prefix}**{authorName}**:\n{content}";
        }

        public static string AppendAttachmentMirrorNotice(string content, string? languageCode, bool hasOversizedAttachments)
        {
            if (!hasOversizedAttachments)
            {
                return content;
            }

            var notice = MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice(languageCode);
            return string.IsNullOrWhiteSpace(content)
                ? notice
                : $"{content}\n{notice}";
        }

        public static string? ResolveLinkedMessageTargetLanguageCode(string? storedLanguageCode, string? configuredTargetLanguageCode)
        {
            if (!string.IsNullOrWhiteSpace(configuredTargetLanguageCode))
            {
                return LanguageCodeService.NormalizeLanguageCode(configuredTargetLanguageCode);
            }

            var stored = LanguageCodeService.NormalizeLanguageCode(storedLanguageCode);
            return string.Equals(stored, "MASTER", StringComparison.Ordinal) ? null : stored;
        }

        public static bool ShouldTranslateLinkedMessage(string sourceLang, string? targetLang)
        {
            return !string.IsNullOrWhiteSpace(targetLang)
                && !LanguageCodeService.AreSameLanguageCode(sourceLang, targetLang);
        }

        public static string FormatLinkedMessageText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            string sourceLang,
            string? targetLang,
            string content,
            DateTimeOffset? originalTimestamp = null)
        {
            var sourceLanguageInfo = new SourceLanguageInfo(
                LanguageCodeService.NormalizeLanguageCode(sourceLang),
                [LanguageCodeService.NormalizeLanguageCode(sourceLang)]);
            return FormatLinkedMessageText(sourceChannelId, linkedChannelId, authorName, sourceLanguageInfo, targetLang, content, originalTimestamp);
        }

        public static string FormatLinkedMessageText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            SourceLanguageInfo sourceLanguageInfo,
            string? targetLang,
            string content,
            DateTimeOffset? originalTimestamp = null)
        {
            if (!ShouldTranslateLinkedMessage(sourceLanguageInfo.PrimaryLanguageCode, targetLang))
            {
                return FormatMirroredAuthorText(authorName, content, originalTimestamp);
            }

            return sourceChannelId == linkedChannelId
                ? FormatTranslatedReplyText(sourceLanguageInfo, targetLang!, content, originalTimestamp)
                : string.IsNullOrWhiteSpace(content)
                    ? FormatMirroredAuthorText(authorName, string.Empty, originalTimestamp)
                    : $"{BuildTimestampPrefix(originalTimestamp)}**{authorName}** {FormatLanguagePair(sourceLanguageInfo, targetLang!)}:\n{content}";
        }

        public static string FormatTranslationFailureText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            string sourceLang,
            string targetLang,
            string sourceContent,
            DateTimeOffset? originalTimestamp = null)
        {
            var sourceLanguageInfo = new SourceLanguageInfo(
                LanguageCodeService.NormalizeLanguageCode(sourceLang),
                [LanguageCodeService.NormalizeLanguageCode(sourceLang)]);
            return FormatTranslationFailureText(
                sourceChannelId,
                linkedChannelId,
                authorName,
                sourceLanguageInfo,
                targetLang,
                sourceContent,
                originalTimestamp);
        }

        public static string FormatTranslationFailureText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            SourceLanguageInfo sourceLanguageInfo,
            string targetLang,
            string sourceContent,
            DateTimeOffset? originalTimestamp = null)
        {
            return sourceChannelId == linkedChannelId
                ? FormatTranslatedReplyText(sourceLanguageInfo, targetLang, $"{sourceContent} *(Translation Failed)*", originalTimestamp)
                : FormatMirroredAuthorText(authorName, $"{sourceContent} *(Translation Failed)*", originalTimestamp);
        }
    }
}
