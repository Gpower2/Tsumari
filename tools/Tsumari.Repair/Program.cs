using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.TranslationProviders;

namespace Tsumari.Repair;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try { return await Run(args, stop.Token); }
        catch (OperationCanceledException) { Console.WriteLine("Stopped. Saved plan can be resumed."); return 2; }
        catch (Exception ex) { Console.Error.WriteLine($"Stopped: {ex.GetType().Name}: {ex.Message}"); return 1; }
    }

    private static async Task<int> Run(string[] args, CancellationToken cancellation)
    {
        if (args.Length == 0 || args[0] == "--help")
        {
            Console.WriteLine("Commands: scan | review-copies | prepare | apply | verify. Required: --config PATH --plan PATH. scan/review-copies also require --db PATH; review-copies requires --review PATH (reviewed message IDs and source languages). Optional --from/--until local dates and --timezone Europe/Athens. prepare/apply support --limit N, --delay-seconds N (default 3). No command creates Discord messages.");
            return 0;
        }
        var command = args[0];
        if (!new[] { "scan", "review-copies", "prepare", "apply", "verify" }.Contains(command)) throw new ArgumentException("Unknown command");
        var options = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException("Expected --option value");
            options.Add(args[i][2..], args[i + 1]);
        }
        var allowed = new[] { "config", "plan", "db", "from", "until", "timezone", "limit", "delay-seconds", "review" };
        if (options.Keys.Any(k => !allowed.Contains(k))) throw new ArgumentException("Unknown option");
        string Required(string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException($"--{key} required");
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Required("config"))).Build();
        var token = Environment.GetEnvironmentVariable("TSUMARI_REPAIR_DISCORD_TOKEN") ?? config["Discord:Token"];
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Discord token missing from config/environment");
        var path = Path.GetFullPath(Required("plan"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var planLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var api = new DiscordApi(token, command == "apply", cancellation);
        var me = (JsonObject)(await api.Get("users/@me") ?? throw new InvalidOperationException("Bot identity unavailable"));
        if (me["bot"]?.GetValue<bool>() != true) throw new InvalidOperationException("A bot token is required");
        var botId = me["id"]!.GetValue<string>();
        var limit = int.Parse(options.GetValueOrDefault("limit", int.MaxValue.ToString()), CultureInfo.InvariantCulture);
        var delay = double.Parse(options.GetValueOrDefault("delay-seconds", "3"), CultureInfo.InvariantCulture);
        if (limit < 1 || delay < 0) throw new ArgumentException("Invalid limit/delay");
        if (command == "scan")
        {
            await Scan(api, botId, Required("db"), path, options, cancellation);
            return 0;
        }
        if (command == "review-copies")
        {
            if (File.Exists(path)) throw new ArgumentException("Use a new plan path for reviewed copies.");
            var reviewed = JsonNode.Parse(File.ReadAllText(Required("review")))!.AsArray();
            var copyPlan = new Plan { BotId = botId, Database = Path.GetFullPath(Required("db")), TimeZone = options.GetValueOrDefault("timezone", "Europe/Athens") };
            var zone = TimeZoneInfo.FindSystemTimeZoneById(copyPlan.TimeZone);
            var links = new List<(Link Link, string SourceLanguage)>();
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copyPlan.Database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                await db.OpenAsync(cancellation);
                foreach (var entry in reviewed)
                {
                    using var query = db.CreateCommand();
                    query.CommandText = "SELECT OriginalMessageId,OriginalChannelId,MirroredMessageId,ChannelId,LanguageCode FROM MessageLinks WHERE MirroredMessageId=$id";
                    query.Parameters.AddWithValue("$id", entry!["MirrorId"]!.GetValue<string>());
                    using var reader = await query.ExecuteReaderAsync(cancellation);
                    if (!await reader.ReadAsync(cancellation)) throw new InvalidDataException("Reviewed mirror has no database link");
                    var link = new Link(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
                    var sourceLanguage = entry["SourceLanguageCode"]!.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(sourceLanguage) || string.Equals(link.Language, "master", StringComparison.OrdinalIgnoreCase)
                        || Tsumari.Bot.Services.LanguageCodeService.AreSameLanguageCode(sourceLanguage, link.Language))
                        throw new InvalidDataException("Reviewed copy must have a known source language different from its translation destination");
                    links.Add((link, sourceLanguage));
                }
            }
            foreach (var (storedLink, sourceLanguage) in links.DistinctBy(x => x.Link.MirrorId))
            {
                var mirror = await api.Get($"channels/{storedLink.Channel}/messages/{storedLink.MirrorId}") as JsonObject ?? throw new InvalidDataException("Reviewed mirror is missing");
                if (mirror["author"]?["id"]?.ToString() != botId) throw new InvalidDataException("Reviewed mirror is not authored by this bot");
                var jump = RepairRules.OriginalLink(mirror);
                if (jump != null && jump.Value.Message != storedLink.OriginalId) throw new InvalidDataException("Original link mismatch");
                var link = storedLink with { OriginalChannel = storedLink.OriginalChannel ?? jump?.Channel };
                if (link.OriginalChannel == null) throw new InvalidDataException("Unknown original channel");
                var original = await api.Get($"channels/{link.OriginalChannel}/messages/{link.OriginalId}") as JsonObject ?? throw new InvalidDataException("Original is missing");
                if (RepairRules.Replacement(RepairRules.Content(mirror), RepairRules.Content(original), link.Language, "validation", link.Channel == link.OriginalChannel, true) == null)
                    throw new InvalidDataException($"{link.MirrorId} is not an exact unmarked source copy in a recognized wrapper");
                copyPlan.Candidates.Add(new Candidate { Link = link, Before = mirror, Original = original, FailureKind = "reviewed-copy", SourceLanguageCode = sourceLanguage,
                    LocalCreationTime = TimeZoneInfo.ConvertTime(RepairRules.Created(link.MirrorId), zone).ToString("O"),
                    JumpUrl = $"https://discord.com/channels/{jump?.Guild ?? "@me"}/{link.Channel}/{link.MirrorId}" });
            }
            if (copyPlan.Candidates.Count == 0) throw new InvalidDataException("Review list is empty");
            copyPlan.Candidates = copyPlan.Candidates.OrderBy(c => RepairRules.Created(c.Link.MirrorId)).ToList();
            copyPlan.From = copyPlan.Candidates.Min(c => RepairRules.Created(c.Link.MirrorId));
            copyPlan.Until = copyPlan.Candidates.Max(c => RepairRules.Created(c.Link.MirrorId)).AddMilliseconds(1);
            copyPlan.SelectedLinks = copyPlan.Candidates.Count;
            PlanFile.Save(path, copyPlan);
            Summary(copyPlan);
            return 0;
        }
        var plan = PlanFile.Read(path);
        if (plan.Version != 1 || plan.BotId != botId) throw new InvalidOperationException("Plan version/bot identity mismatch");
        if (command == "prepare")
        {
            if (!string.Equals(config["Translation:Provider"], "Ollama", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This standalone repair tool currently supports Ollama only.");
            var services = new ServiceCollection().AddLogging(b => b.SetMinimumLevel(LogLevel.None));
            services.AddSingleton<IConfiguration>(config);
            services.AddHttpClient(HttpClientNames.OllamaTranslation, client => client.Timeout = TimeSpan.FromSeconds(100));
            services.AddTransient<OllamaTranslationProvider>();
            using var provider = services.BuildServiceProvider();
            var translator = provider.GetRequiredService<OllamaTranslationProvider>();
            var done = 0;
            foreach (var item in plan.Candidates.Where(c => c.Status == "discovered").Take(limit))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (!await Unchanged(api, item, botId)) { item.Status = "changed-before-prepare"; continue; }
                    var sourceText = RepairRules.Content(item.Original);
                    var text = item.ReviewedTranslation ?? await translator.TranslateTextAsync(sourceText, item.Link.Language, item.SourceLanguageCode).WaitAsync(cancellation);
                    if (RepairRules.RequiresEchoReview(sourceText, text, item.SourceLanguageCode, item.Link.Language))
                        throw new InvalidDataException("Provider returned unchanged source text without a confirmed matching source/target language; review required");
                    item.Replacement = RepairRules.Replacement(RepairRules.Content(item.Before), sourceText, item.Link.Language, text, item.Link.Channel == item.Link.OriginalChannel, item.FailureKind == "reviewed-copy");
                    if (item.Replacement == null) throw new InvalidDataException("Empty, oversized or invalid translated output/wrapper");
                    item.TranslationOrigin = item.ReviewedTranslation == null ? "provider" : "reviewed";
                    item.Error = null;
                    item.Status = "prepared";
                    Console.WriteLine($"Prepared {++done}: {item.Link.MirrorId} {item.Link.Language} ({item.LocalCreationTime})");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    item.Error = ex.GetType().Name; // Do not serialize provider response bodies or configuration.
                    Console.WriteLine($"Preparation paused after {item.Link.MirrorId}: {ex.GetType().Name}");
                    return 1;
                }
                finally { PlanFile.Save(path, plan); }
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellation);
            }
        }
        else if (command == "apply")
        {
            var done = 0;
            foreach (var item in plan.Candidates.Where(c => c.Status is "prepared" or "applying").Take(limit))
            {
                cancellation.ThrowIfCancellationRequested();
                var current = await api.Get($"channels/{item.Link.Channel}/messages/{item.Link.MirrorId}") as JsonObject;
                if (item.Status == "applying" && current != null && RepairRules.Content(current) == item.Replacement && RepairRules.Preserved(item.Before, current))
                { item.Status = "applied"; PlanFile.Save(path, plan); continue; }
                if (!await Unchanged(api, item, botId))
                { item.Status = "changed-before-apply"; PlanFile.Save(path, plan); continue; }
                // Validate the prepared output again; don't accept arbitrary text from a malformed plan.
                if (string.IsNullOrWhiteSpace(item.Replacement) || item.Replacement.Length > 2000 || item.Replacement.Contains("*(Translation Failed)*"))
                    throw new InvalidDataException("Invalid prepared output");
                item.Status = "applying";
                item.Attempts++;
                PlanFile.Save(path, plan);
                PlanFile.Journal(path, new { at = DateTimeOffset.UtcNow, action = "before-edit", item.Link, item.Before, item.Replacement });
                await api.Edit(item.Link.Channel, item.Link.MirrorId, item.Replacement);
                var after = await api.Get($"channels/{item.Link.Channel}/messages/{item.Link.MirrorId}") as JsonObject;
                if (after == null || RepairRules.Content(after) != item.Replacement || !RepairRules.Preserved(item.Before, after))
                    throw new InvalidOperationException($"Post-edit verification failed for {item.Link.MirrorId}; stopped for inspection");
                item.Status = "applied";
                PlanFile.Journal(path, new { at = DateTimeOffset.UtcNow, action = "verified", item.Link.MirrorId, after });
                PlanFile.Save(path, plan);
                Console.WriteLine($"Applied and verified {++done}: {item.Link.MirrorId} {item.Link.Language}");
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellation);
            }
        }
        else
        {
            var failed = 0;
            foreach (var item in plan.Candidates.Where(c => c.Status == "applied"))
            {
                var current = await api.Get($"channels/{item.Link.Channel}/messages/{item.Link.MirrorId}") as JsonObject;
                var valid = current != null && RepairRules.Content(current) == item.Replacement && RepairRules.Preserved(item.Before, current);
                Console.WriteLine($"Verify {item.Link.MirrorId}: {(valid ? "OK" : "CHANGED OR MISSING")}");
                if (!valid) failed++;
            }
            if (failed != 0) return 1;
        }
        Summary(plan);
        return 0;
    }

    private static async Task<bool> Unchanged(DiscordApi api, Candidate item, string botId)
    {
        var original = await api.Get($"channels/{item.Link.OriginalChannel}/messages/{item.Link.OriginalId}") as JsonObject;
        var mirror = await api.Get($"channels/{item.Link.Channel}/messages/{item.Link.MirrorId}") as JsonObject;
        return original != null && mirror != null && mirror["author"]?["id"]?.ToString() == botId
            && RepairRules.Fingerprint(original) == RepairRules.Fingerprint(item.Original)
            && RepairRules.Fingerprint(mirror) == RepairRules.Fingerprint(item.Before);
    }

    private static async Task Scan(DiscordApi api, string botId, string database, string path, Dictionary<string, string> options, CancellationToken cancellation)
    {
        database = Path.GetFullPath(database);
        var links = new List<Link>();
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            await db.OpenAsync(cancellation);
            using var query = db.CreateCommand();
            query.CommandText = "SELECT OriginalMessageId,OriginalChannelId,MirroredMessageId,ChannelId,LanguageCode FROM MessageLinks";
            using var reader = await query.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
                links.Add(new Link(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }
        if (links.Count == 0) throw new InvalidDataException("No message links");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.GetValueOrDefault("timezone", "Europe/Athens"));
        var plan = File.Exists(path) ? PlanFile.Read(path) : new Plan
        {
            BotId = botId, Database = database, TimeZone = zone.Id,
            From = options.TryGetValue("from", out var start) ? RepairRules.LocalBoundary(start, zone) : links.Min(l => RepairRules.Created(l.MirrorId)),
            Until = options.TryGetValue("until", out var end) ? RepairRules.LocalBoundary(end, zone) : links.Max(l => RepairRules.Created(l.MirrorId)).AddMilliseconds(1)
        };
        if (plan.BotId != botId || plan.Database != database || plan.TimeZone != zone.Id) throw new InvalidDataException("Existing plan configuration differs");
        if ((options.TryGetValue("from", out var requestedFrom) && RepairRules.LocalBoundary(requestedFrom, zone) != plan.From)
            || (options.TryGetValue("until", out var requestedUntil) && RepairRules.LocalBoundary(requestedUntil, zone) != plan.Until))
            throw new ArgumentException("Existing plan has different time bounds; use a new plan path.");
        if (plan.From >= plan.Until) throw new ArgumentException("from must precede until");
        var selected = links.Where(l => RepairRules.Created(l.MirrorId) >= plan.From && RepairRules.Created(l.MirrorId) < plan.Until).ToList();
        plan.SelectedLinks = selected.Count;
        PlanFile.Save(path, plan);
        Console.WriteLine($"Scanning {selected.Count} linked mirrors, {TimeZoneInfo.ConvertTime(plan.From, zone):O} through {TimeZoneInfo.ConvertTime(plan.Until, zone):O} (exclusive)");
        foreach (var group in selected.GroupBy(l => l.Channel))
        {
            if (plan.CompletedChannels.Contains(group.Key)) continue;
            var pending = group.ToDictionary(l => l.MirrorId);
            var oldest = group.Min(l => ulong.Parse(l.MirrorId));
            var newest = group.Max(l => ulong.Parse(l.MirrorId));
            var before = checked(newest + 1).ToString(CultureInfo.InvariantCulture);
            var pages = 0;
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                JsonArray? page;
                try { page = await api.Get($"channels/{group.Key}/messages?limit=100&before={before}") as JsonArray; }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                { plan.Issues.Add($"Channel {group.Key}: history inaccessible (HTTP 403)"); break; }
                if (page == null || page.Count == 0) break;
                foreach (var node in page)
                {
                    var message = (JsonObject)node!;
                    var id = message["id"]!.GetValue<string>();
                    if (!pending.Remove(id, out var link) || message["author"]?["id"]?.ToString() != botId || !RepairRules.Content(message).Contains("*(Translation Failed)*")) continue;
                    if (plan.Candidates.Any(c => c.Link.MirrorId == id)) continue;
                    if (string.Equals(link.Language, "master", StringComparison.OrdinalIgnoreCase)) { plan.Issues.Add($"{id}: marker on raw master copy; skipped"); continue; }
                    var jump = RepairRules.OriginalLink(message);
                    if (jump != null && jump.Value.Message != link.OriginalId) { plan.Issues.Add($"{id}: source-link mismatch"); continue; }
                    var sourceChannel = link.OriginalChannel ?? jump?.Channel;
                    if (string.IsNullOrEmpty(sourceChannel)) { plan.Issues.Add($"{id}: original channel unknown"); continue; }
                    JsonObject? original;
                    try { original = await api.Get($"channels/{sourceChannel}/messages/{link.OriginalId}") as JsonObject; }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    { plan.Issues.Add($"{id}: original inaccessible (HTTP 403)"); continue; }
                    if (original == null) { plan.Issues.Add($"{id}: original deleted"); continue; }
                    if (RepairRules.Replacement(RepairRules.Content(message), RepairRules.Content(original), link.Language, "validation", sourceChannel == link.Channel) == null)
                    { plan.Issues.Add($"{id}: marker present but wrapper/source differs; manual review needed"); continue; }
                    plan.Candidates.Add(new Candidate
                    {
                        Link = link with { OriginalChannel = sourceChannel }, Before = message.DeepClone().AsObject(), Original = original,
                        LocalCreationTime = TimeZoneInfo.ConvertTime(RepairRules.Created(id), zone).ToString("O"),
                        JumpUrl = jump == null ? $"https://discord.com/channels/@me/{link.Channel}/{id}" : $"https://discord.com/channels/{jump.Value.Guild}/{link.Channel}/{id}"
                    });
                    Console.WriteLine($"Found failure: {id} {link.Language} {TimeZoneInfo.ConvertTime(RepairRules.Created(id), zone):yyyy-MM-dd HH:mm:ss}");
                    PlanFile.Save(path, plan);
                }
                var minId = page.Min(n => ulong.Parse(n!["id"]!.GetValue<string>()));
                before = minId.ToString(CultureInfo.InvariantCulture);
                if (++pages % 20 == 0) Console.WriteLine($"Channel {group.Key}: {pages} pages, {pending.Count} tracked mirrors remaining, {plan.Candidates.Count} failures found");
                if (minId <= oldest) break;
            }
            if (pending.Count > 0) plan.Issues.Add($"Channel {group.Key}: {pending.Count} linked mirrors absent from history (not recreated)");
            plan.CompletedChannels.Add(group.Key);
            PlanFile.Save(path, plan);
            Console.WriteLine($"Scanned channel {group.Key}; total confirmed failures {plan.Candidates.Count}, issue entries {plan.Issues.Count}");
        }
        plan.Candidates = plan.Candidates.OrderBy(c => RepairRules.Created(c.Link.MirrorId)).ToList();
        PlanFile.Save(path, plan);
        Summary(plan);
    }

    private static void Summary(Plan plan) => Console.WriteLine($"Candidates: {plan.Candidates.Count}; " + string.Join(", ", plan.Candidates.GroupBy(c => c.Status).Select(g => $"{g.Key}={g.Count()}")) + $"; issue entries={plan.Issues.Count}");
}
