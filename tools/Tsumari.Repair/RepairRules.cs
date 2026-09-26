using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tsumari.Bot.Services;

namespace Tsumari.Repair;

public static class RepairRules
{
    public const string Marker = " *(Translation Failed)*";
    public static DateTimeOffset Created(string id) => DateTimeOffset.FromUnixTimeMilliseconds(
        checked((long)(ulong.Parse(id, CultureInfo.InvariantCulture) >> 22) + 1420070400000));

    public static DateTimeOffset LocalBoundary(string value, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(DateTime.ParseExact(value,
            ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"], CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("Boundary is ambiguous or invalid at a daylight-saving transition; choose another time.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    // Match the entire generated wrapper, including the exact current source text. Never
    // translate a loose substring match, quotation, edited source, or unknown suffix.
    public static string? Replacement(string failed, string source, string language, string translated, bool sameChannel, bool reviewedUnmarkedCopy = false)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated) || translated.Contains("*(Translation Failed)*")) return null;
        var pattern = sameChannel
            ? @"\A(?:<t:\d+:f> )?\*\([^\r\n]+ => " + Regex.Escape(LanguageCodeService.NormalizeLanguageCode(language)) + @"\):\* "
            : @"\A(?:<t:\d+:f> )?\*\*[^\r\n]+\*\*:\r?\n";
        var header = Regex.Match(failed, pattern);
        if (!header.Success) return null;
        var body = source + (reviewedUnmarkedCopy ? "" : Marker);
        var remainder = failed[header.Length..];
        if (!remainder.StartsWith(body, StringComparison.Ordinal)) return null;
        var suffix = remainder[body.Length..];
        if (suffix.Length != 0 && suffix != "\n" + MirroredMessageNoticeLocalizer.GetOversizedAttachmentNotice(language)) return null;
        var result = header.Value + translated.Trim() + suffix;
        return result.Length <= 2000 ? result : null;
    }

    public static bool RequiresEchoReview(string source, string output, string? sourceLanguage, string targetLanguage) =>
        source.Any(char.IsLetter)
        && (string.IsNullOrWhiteSpace(sourceLanguage) || !LanguageCodeService.AreSameLanguageCode(sourceLanguage, targetLanguage))
        && string.Equals(source.Trim(), output.Trim(), StringComparison.Ordinal);

    public static string Content(JsonObject message) => message["content"]?.GetValue<string>() ?? "";
    public static string Fingerprint(JsonObject message)
    {
        var selected = new JsonObject();
        foreach (var key in new[] { "id", "channel_id", "content", "edited_timestamp", "attachments", "components", "embeds", "message_reference", "flags" })
            selected[key] = message[key]?.DeepClone();
        return selected.ToJsonString();
    }
    public static bool Preserved(JsonObject before, JsonObject after) =>
        new[] { "id", "channel_id", "timestamp", "attachments", "components", "message_reference" }
        .All(key => JsonNode.DeepEquals(before[key], after[key]));

    public static (string Guild, string Channel, string Message)? OriginalLink(JsonObject message)
    {
        return Walk(message["components"]);
        static (string, string, string)? Walk(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["label"]?.ToString() == "Original" && obj["url"] is JsonValue url)
                {
                    var match = Regex.Match(url.ToString(), @"\Ahttps://discord\.com/channels/(\d+)/(\d+)/(\d+)\z");
                    if (match.Success) return (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
                }
                foreach (var pair in obj) { var found = Walk(pair.Value); if (found != null) return found; }
            }
            else if (node is JsonArray array)
                foreach (var child in array) { var found = Walk(child); if (found != null) return found; }
            return null;
        }
    }
}
