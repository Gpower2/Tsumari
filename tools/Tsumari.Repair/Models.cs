using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tsumari.Repair;

public sealed record Link(string OriginalId, string? OriginalChannel, string MirrorId, string Channel, string Language);
public sealed class Plan
{
    public int Version { get; set; } = 1;
    public string BotId { get; set; } = "";
    public string Database { get; set; } = "";
    public string TimeZone { get; set; } = "Europe/Athens";
    public DateTimeOffset From { get; set; }
    public DateTimeOffset Until { get; set; }
    public int SelectedLinks { get; set; }
    public List<string> CompletedChannels { get; set; } = [];
    public List<string> Issues { get; set; } = [];
    public List<Candidate> Candidates { get; set; } = [];
}
public sealed class Candidate
{
    public Link Link { get; set; } = null!;
    public string LocalCreationTime { get; set; } = "";
    public string JumpUrl { get; set; } = "";
    public JsonObject Before { get; set; } = new();
    public JsonObject Original { get; set; } = new();
    public string? Replacement { get; set; }
    public string FailureKind { get; set; } = "marked";
    public string? SourceLanguageCode { get; set; }
    public string? ReviewedTranslation { get; set; }
    public string TranslationOrigin { get; set; } = "provider";
    public string Status { get; set; } = "discovered";
    public string? Error { get; set; }
    public int Attempts { get; set; }
}
public static class PlanFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static Plan Read(string path) => JsonSerializer.Deserialize<Plan>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty plan");
    public static void Save(string path, Plan plan)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(plan, Options));
        File.Move(path + ".tmp", path, overwrite: true);
    }
    public static void Journal(string path, object entry) => File.AppendAllText(path + ".journal.jsonl", JsonSerializer.Serialize(entry) + Environment.NewLine);
}
