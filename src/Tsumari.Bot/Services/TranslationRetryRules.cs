using Discord;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tsumari.Bot.Services;

public static class TranslationRetryRules
{
    public const string ButtonId = "translation-retry:v1";
    public static readonly IReadOnlyList<string> Counters = Array.AsReadOnly(new[] { "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣", "🔟" });
    public static bool IsCounter(IEmote emote) => emote is Emoji && Counters.Contains(emote.Name);
    public static int ReadCount(IMessage message) => message.Reactions
        .Where(r => r.Value.IsMe && IsCounter(r.Key))
        .Select(r => Enumerable.Range(0, Counters.Count).First(i => Counters[i] == r.Key.Name) + 1).DefaultIfEmpty(0).Max();

    public static bool TryParseJumpLink(string text, out ulong guild, out ulong channel, out ulong message)
    {
        guild = channel = message = 0;
        var match = Regex.Match(text.Trim(), @"\Ahttps://discord\.com/channels/(\d+)/(\d+)/(\d+)\z");
        return match.Success && ulong.TryParse(match.Groups[1].Value, out guild)
            && ulong.TryParse(match.Groups[2].Value, out channel) && ulong.TryParse(match.Groups[3].Value, out message);
    }

    // Only replace the body inside a recognized bot wrapper. Preserve author and displayed timestamp verbatim.
    public static string ReplaceBody(string before, string target, string translation)
    {
        if (string.IsNullOrWhiteSpace(translation) || translation.Contains("*(Translation Failed)*", StringComparison.Ordinal))
            throw new InvalidOperationException("The provider returned empty or invalid translation text.");
        var lang = Regex.Escape(LanguageCodeService.NormalizeLanguageCode(target));
        var match = Regex.Match(before, @"\A(?:<t:\d+:f> )?(?:\*\*[^\r\n]+\*\*(?: \([^\r\n]+ => " + lang + @"\))?:\r?\n|\*\([^\r\n]+ => " + lang + @"\):\* )");
        if (!match.Success) throw new InvalidOperationException("This message does not have a recognized translation wrapper.");
        var notice = "\n" + MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice(target);
        var suffix = before.EndsWith(notice, StringComparison.Ordinal) ? notice : "";
        var result = match.Value + translation.Trim() + suffix;
        if (result.Length > 2000) throw new InvalidOperationException("The translation exceeds Discord's message length limit.");
        return result;
    }

    public static string Fingerprint(IUserMessage message) => JsonSerializer.Serialize(new
    {
        message.Id, Channel = message.Channel.Id, message.Content, message.EditedTimestamp,
        Attachments = message.Attachments.Select(a => new { a.Id, a.Filename, a.Url }),
        Components = SnapshotComponents(message.Components), message.Reference, message.Flags
    });

    public static string PreservedMetadata(IUserMessage message) => JsonSerializer.Serialize(new
    {
        message.Id, Channel = message.Channel.Id, message.Timestamp,
        Attachments = message.Attachments.Select(a => new { a.Id, a.Filename, a.Url }),
        Components = SnapshotComponents(message.Components), message.Reference
    });

    private static object[] SnapshotComponents(IEnumerable<IMessageComponent>? components) => (components ?? []).Select<IMessageComponent, object>(c => c switch
    {
        ActionRowComponent row => new { row.Type, row.Id, Components = SnapshotComponents(row.Components) },
        ButtonComponent button => new { button.Type, button.Id, button.Style, button.Label, button.Url, button.CustomId,
            Emote = button.Emote?.ToString(), button.IsDisabled, button.SkuId },
        _ => throw new InvalidOperationException("This message contains unsupported components and needs review.")
    }).ToArray();
}
