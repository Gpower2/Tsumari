using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Tsumari.Bot.TranslationProviders.Models;

namespace Tsumari.Evaluation;

internal static class Program
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    static readonly HttpClient ResidencyClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    static string Arg(string[] args, string name, string fallback) => Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    public static async Task<int> Main(string[] args)
    {
        var root = Path.GetFullPath(Arg(args, "--root", Directory.GetCurrentDirectory()));
        var endpoint = Arg(args, "--endpoint", "http://localhost:11435/api/generate");
        var output = Path.GetFullPath(Arg(args, "--output", Path.Combine(root, ".artifacts/evaluation/results.jsonl")));
        var revision = Arg(args, "--revision", "native-v1");
        var profiles = Arg(args, "--profiles", "translategemma-12b,salamandra-v3-q4,tiny-aya-global-q8").Split(',');
        var suite = Arg(args, "--suite", "full");
        var tasks = Arg(args, "--tasks", "translation");
        if (tasks is not ("translation" or "analysis" or "both")) throw new ArgumentException("Tasks must be translation, analysis or both.");
        var repeats = int.Parse(Arg(args, "--repeats", "1"));
        if (repeats < 1 || repeats > 10) throw new ArgumentException("Repeats must be 1..10.");
        var cases = LoadCases(root).ToList();
        if (suite == "smoke") cases = cases.Where(c => c.Id is "tasos-italian" or "fratello-full" or "heldout-SR" or "heldout-ZH").ToList();
        else if (suite == "heldout") cases = cases.Where(c => c.Origin == "new_heldout").ToList();
        else if (suite != "full") throw new ArgumentException("Suite must be smoke, full or heldout.");
        if (args.Contains("--trusted-hints")) cases = cases.Select(c => c with { Requests = c.Requests.Select(r => r with { Source = r.Source ?? c.Source }).Distinct().ToList() }).ToList();
        var filter = Arg(args, "--cases", "");
        if (filter.Length > 0) cases = cases.Where(c => filter.Split(',').Contains(c.Id)).ToList();
        if (tasks == "analysis") cases = cases.DistinctBy(c => c.Text).ToList();
        if (cases.Count == 0) throw new ArgumentException("No selected cases.");
        var settings = new Dictionary<string,string?>();
        AddProfile("translategemma-12b", "translategemma:12b", "translategemma");
        AddProfile("salamandra-v3-q4", "hf.co/BSC-LT/salamandraTA-7B-instruct-GGUF:Q4_K_M", "salamandra");
        AddProfile("tiny-aya-global-q8", "hf.co/CohereLabs/tiny-aya-global-GGUF:Q8_0", "tiny-aya");
        void AddProfile(string id, string model, string formatter)
        {
            var p = $"Translation:Llm:Profiles:{id}:";
            settings[p + "Model"] = model; settings[p + "Formatter"] = formatter;
            settings[p + "IntendedTasks:0"] = "Translation"; if (formatter != "salamandra") settings[p + "IntendedTasks:1"] = "Analysis"; settings[p + "Transport"] = "Ollama"; settings[p + "Endpoint"] = endpoint;
            settings[p + "Temperature"] = "0"; settings[p + "ContextLength"] = "4096";
            if (formatter != "translategemma") settings[p + "MaxOutputTokens"] = "1024"; settings[p + "KeepAlive"] = "15m"; settings[p + "PromptRevision"] = formatter == "translategemma" ? "existing-unchanged" : revision;
            settings[p + "TopP"] = formatter == "translategemma" ? "0.1" : "1";
        }
        // Optional profile overrides only apply to alternatives. Baseline cannot be tuned by this runner.
        var overridePath = Arg(args, "--prompt-overrides", "");
        if (overridePath.Length > 0)
        {
            var overrides = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(overridePath))!;
            foreach (var (id,prompt) in overrides)
            {
                if (id == "translategemma-12b") throw new ArgumentException("Baseline prompt overrides are forbidden.");
                if (!profiles.Contains(id)) throw new ArgumentException($"Override does not match selected profile: {id}");
                settings[$"Translation:Llm:Profiles:{id}:TranslationSystemPrompt"] = prompt;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output) && !args.Contains("--append")) throw new IOException("Output exists; use a new path or explicit --append.");
        using var writer = new StreamWriter(output, append:true) { AutoFlush = true };
        using var clientFactory = new ClientFactory();
        var factory = new LlmModelFactory(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), clientFactory);
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        var failures = 0;
        foreach (var profile in profiles)
        {
            var model = factory.Create(profile);
            foreach (var item in new[] { new EvaluationCase("warmup", "Hello, Alex!", "EN", "warmup", "Warmup excluded from quality and timed suite summaries.", [new("DE", "EN")]) }.Concat(cases))
            for (var repeat = 0; repeat < repeats; repeat++)
            {
                if (tasks is "analysis" or "both") await Run("analysis", null, item.Source, async ct => JsonSerializer.Serialize(await model.AnalyzeAsync(item.Text, ct), Json));
                if (tasks is "translation" or "both") foreach (var request in item.Requests) await Run("translation", request.Target, request.Source, ct => model.TranslateAsync(item.Text, request.Target, request.Source, cancellationToken:ct));
                async Task Run(string task,string? target,string? source,Func<CancellationToken,Task<string>> action)
                {
                    shutdown.Token.ThrowIfCancellationRequested();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(180));
                    using var sampling = new CancellationTokenSource();
                    var samples = new List<GpuSample>();
                    var sampler = SampleGpu(samples, sampling.Token);
                    clientFactory.LastRequest = null; clientFactory.LastResponse = null; var timer = Stopwatch.StartNew(); string? response = null; string? error = null;
                    try { response = await action(timeout.Token); }
                    catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; failures++; }
                    timer.Stop(); sampling.Cancel(); await sampler; var residency = await Residency(endpoint);
                    var checks = response is null ? new Dictionary<string,bool>() : Checks(item, task, response);
                    var row = new { timestampUtc=DateTimeOffset.UtcNow, profile, revision=profile=="translategemma-12b"?"existing-unchanged":revision, task, caseId=item.Id, origin=item.Origin, repeat, input=item.Text, source, target, expectedDominantLanguage=item.Source, semanticChecks=item.Checks, requestPayload=clientFactory.LastRequest, rawResponsePayload=clientFactory.LastResponse, response, error, durationMs=timer.Elapsed.TotalMilliseconds, residency, gpu=new { sampleCount=samples.Count, baselineMemoryMiB=samples.FirstOrDefault()?.MemoryMiB, peakMemoryMiB=samples.Count==0?(int?)null:samples.Max(x=>x.MemoryMiB), meanUtilizationPercent=samples.Count==0?(double?)null:samples.Average(x=>x.Utilization), peakUtilizationPercent=samples.Count==0?(int?)null:samples.Max(x=>x.Utilization), samples }, mechanicalChecks=checks, semanticReview="pending: curated expectations are not gold translations", effectiveSettings=settings.Where(x=>x.Key.StartsWith($"Translation:Llm:Profiles:{profile}:")).ToDictionary(x=>x.Key[($"Translation:Llm:Profiles:{profile}:").Length..],x=>x.Value) };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(row, Json));
                    Console.WriteLine($"{profile} {task} {item.Id} {source}->{target} {timer.Elapsed.TotalSeconds:F2}s {(error is null?"OK":"ERROR")}");
                }
            }
        }
        Console.WriteLine($"Saved {output}; request failures: {failures}. Semantic review is separate.");
        return failures == 0 ? 0 : 2;
    }
    static Dictionary<string,bool> Checks(EvaluationCase c,string task,string response)
    {
        var checks = new Dictionary<string,bool> { ["nonEmpty"] = !string.IsNullOrWhiteSpace(response) };
                if (task != "translation") {
            if(c.Source is not null) {
                try { using var doc=JsonDocument.Parse(response); checks["expectedDominantLanguage"] = doc.RootElement.GetProperty("PrimaryLanguageCode").GetString()==c.Source; }
                catch(JsonException) { checks["validAnalysisResult"]=false; }
            }
            return checks;
        }
        checks["noMarkdownFenceAdded"] = c.Text.Contains("```") || !response.Contains("```");
        var emoji = Regex.Matches(c.Text, @"[\uD800-\uDBFF][\uDC00-\uDFFF]").Select(x=>x.Value).Distinct();
        checks["emojiPreserved"] = emoji.All(response.Contains);
        checks["lineBreakPreserved"] = !c.Text.Contains('\n') || response.Contains('\n');
        if (c.Text.Contains("**")) checks["boldPreserved"] = response.Contains("**");
        return checks;
    }
    static IEnumerable<EvaluationCase> LoadCases(string root)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/model-evaluation/reference-data/cases.json")));
        foreach(var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var source=c.GetProperty("expected_dominant_language").GetString();
            var requests=c.GetProperty("historically_requested_translations").EnumerateArray().Select(r=>new TranslationRequest(r.GetProperty("target_language").GetString()!,r.GetProperty("historical_source_hint").GetString())).Distinct().ToList();
            if(requests.Count==0) requests.Add(new(source=="EN"?"DE":"EN",source));
            yield return new(c.GetProperty("id").GetString()!,c.GetProperty("input").GetString()!,source,c.GetProperty("origin").GetString()!,c.GetProperty("semantic_checks").GetString()!,requests);
        }
        var heldout = new (string Code,string Text)[] {
            ("DE","Verdammt, Alex! Der Zug fährt erst um 18:30.\n**Warte auf mich!** 😂"),
            ("EL","Γαμώτο, Άλεξ! Το τρένο φεύγει στις 18:30.\n**Περίμενέ με!** 😂"),
            ("EN","Damn it, Alex! The train leaves at 18:30.\n**Wait for me!** 😂"),
            ("IT","Cazzo, Alex! Il treno parte alle 18:30.\n**Aspettami!** 😂"),
            ("PT","Porra, Alex! O comboio parte às 18:30.\n**Espera por mim!** 😂"),
            ("ES","¡Joder, Alex! El tren sale a las 18:30.\n**Espérame!** 😂"),
            ("SR","Јеботе, Алекс! Воз полази у 18:30.\n**Сачекај ме!** 😂"),
            ("ZH","妈的，Alex！火车18:30发车。\n**等我！** 😂") };
        foreach(var (code,text) in heldout) yield return new("heldout-"+code,text,code,"new_heldout","Preserve frustrated register, Alex, 18:30, bold, newline and emoji; do not invent facts.",[new(code=="EN"?"SR":"EN",code)]);
        yield return new("heldout-SR-Latn","Jebote, Aleks! Voz polazi u 18:30.\n**Sačekaj me!** 😂","SR","new_heldout","Serbian Latin input; preserve meaning and profanity.",[new("EN","SR")]);
        yield return new("heldout-ZH-Hant","媽的，Alex！火車18:30發車。\n**等我！** 😂","ZH","new_heldout","Traditional Chinese input; preserve meaning and profanity.",[new("EN","ZH")]);
        foreach(var code in new[]{"DE","EL","IT","PT","ES","ZH","SR-LATN","SR-CYRL","ZH-HANT","PT-BR","PT-PT"}) yield return new("heldout-EN-to-"+code,heldout[2].Text,"EN","new_heldout","Preserve all facts, register, formatting and emoji.",[new(code,"EN")]);
    }
    static async Task<JsonElement?> Residency(string endpoint)
    {
        try {

            var uri=new UriBuilder(endpoint) { Path="/api/ps", Query="" }.Uri;
            using var doc=JsonDocument.Parse(await ResidencyClient.GetStringAsync(uri));
            return doc.RootElement.Clone();
        } catch { return null; }
    }
    static async Task SampleGpu(List<GpuSample> samples,CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                using var p=new Process { StartInfo=new ProcessStartInfo("nvidia-smi","--query-gpu=memory.used,utilization.gpu --format=csv,noheader,nounits") { RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true } };
                p.Start(); var output=await p.StandardOutput.ReadToEndAsync(ct); await p.WaitForExitAsync(ct);
                var values=output.Split('\n',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split(',');
                if(values?.Length==2 && int.TryParse(values[0].Trim(),out var memory) && int.TryParse(values[1].Trim(),out var utilization)) samples.Add(new(DateTimeOffset.UtcNow,memory,utilization));
                await Task.Delay(500,ct);
            }
        }
        catch(OperationCanceledException) { }
        catch(System.ComponentModel.Win32Exception) { /* NVIDIA metrics unavailable. Never fabricate zeros. */ }
    }
    sealed class ClientFactory : IHttpClientFactory, IDisposable
    {
                public string? LastRequest { get; set; }
        public string? LastResponse { get; set; }
        private readonly CaptureHandler handler;
        public ClientFactory() { handler = new CaptureHandler(this); }
        public HttpClient CreateClient(string name)=>new(handler, disposeHandler:false) { Timeout=TimeSpan.FromMinutes(5) };
        public void Dispose() => handler.Dispose();
        sealed class CaptureHandler(ClientFactory owner) : DelegatingHandler(new HttpClientHandler())
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            {
                owner.LastRequest = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
                var response = await base.SendAsync(request,ct);
                owner.LastResponse = await response.Content.ReadAsStringAsync(ct);
                return response;
            }
        }
    }
    sealed record GpuSample(DateTimeOffset TimestampUtc,int MemoryMiB,int Utilization);
    sealed record TranslationRequest(string Target,string? Source);
    sealed record EvaluationCase(string Id,string Text,string? Source,string Origin,string Checks,List<TranslationRequest> Requests);
}
