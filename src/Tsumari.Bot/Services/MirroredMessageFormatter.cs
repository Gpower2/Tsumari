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

        public static string FormatLanguagePair(string sourceLang, string targetLang)
        {
            sourceLang = LanguageCodeService.NormalizeLanguageCode(sourceLang);
            targetLang = LanguageCodeService.NormalizeLanguageCode(targetLang);
            return $"({sourceLang} to {targetLang})";
        }

        public static string FormatLanguagePair(SourceLanguageInfo sourceLanguageInfo, string targetLang)
        {
            ArgumentNullException.ThrowIfNull(sourceLanguageInfo);

            targetLang = LanguageCodeService.NormalizeLanguageCode(targetLang);
            if (sourceLanguageInfo.LabelLanguageCodes.Count <= 1)
            {
                return $"({sourceLanguageInfo.PrimaryLanguageCode} to {targetLang})";
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

        public static string FormatTranslatedReplyText(string sourceLang, string targetLang, string translatedText)
        {
            var sourceLanguageInfo = new SourceLanguageInfo(
                LanguageCodeService.NormalizeLanguageCode(sourceLang),
                [LanguageCodeService.NormalizeLanguageCode(sourceLang)]);
            return FormatTranslatedReplyText(sourceLanguageInfo, targetLang, translatedText);
        }

        public static string FormatTranslatedReplyText(SourceLanguageInfo sourceLanguageInfo, string targetLang, string translatedText)
        {
            var prefix = $"*{FormatLanguagePair(sourceLanguageInfo, targetLang)}:*";
            return string.IsNullOrWhiteSpace(translatedText)
                ? prefix
                : $"{prefix} {translatedText}";
        }

        public static string FormatMirroredAuthorText(string authorName, string content)
        {
            return string.IsNullOrWhiteSpace(content)
                ? $"**{authorName}**:"
                : $"**{authorName}**:\n{content}";
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
            string content)
        {
            var sourceLanguageInfo = new SourceLanguageInfo(
                LanguageCodeService.NormalizeLanguageCode(sourceLang),
                [LanguageCodeService.NormalizeLanguageCode(sourceLang)]);
            return FormatLinkedMessageText(sourceChannelId, linkedChannelId, authorName, sourceLanguageInfo, targetLang, content);
        }

        public static string FormatLinkedMessageText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            SourceLanguageInfo sourceLanguageInfo,
            string? targetLang,
            string content)
        {
            if (!ShouldTranslateLinkedMessage(sourceLanguageInfo.PrimaryLanguageCode, targetLang))
            {
                return FormatMirroredAuthorText(authorName, content);
            }

            return sourceChannelId == linkedChannelId
                ? FormatTranslatedReplyText(sourceLanguageInfo, targetLang!, content)
                : string.IsNullOrWhiteSpace(content)
                    ? FormatMirroredAuthorText(authorName, string.Empty)
                    : $"**{authorName}** {FormatLanguagePair(sourceLanguageInfo, targetLang!)}:\n{content}";
        }

        public static string FormatTranslationFailureText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            string sourceLang,
            string targetLang,
            string sourceContent)
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
                sourceContent);
        }

        public static string FormatTranslationFailureText(
            ulong sourceChannelId,
            ulong linkedChannelId,
            string authorName,
            SourceLanguageInfo sourceLanguageInfo,
            string targetLang,
            string sourceContent)
        {
            return sourceChannelId == linkedChannelId
                ? FormatTranslatedReplyText(sourceLanguageInfo, targetLang, $"{sourceContent} *(Translation Failed)*")
                : FormatMirroredAuthorText(authorName, $"{sourceContent} *(Translation Failed)*");
        }
    }
}
