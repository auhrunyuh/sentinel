using System.Text.Json;
using Sentinel.Core;

var root = Diff.Root(Directory.GetCurrentDirectory());

return args switch
{
    ["review", .. var rest] => await ReviewCmd(rest),
    ["hook", "pre-tool"] => PreTool(),
    ["hook", "post-tool"] => PostTool(),
    ["hook", "stop"] => await Stop(),
    ["watch", .. var rest] => await Watch(rest.Contains("--llm")),
    ["audit", "verify"] => VerifyAudit(),
    ["gate", .. var rest] => await GateCmd(rest),
    ["fix", .. var rest] => await FixCmd(rest),
    ["eval"] => await EvalCmd(),
    _ => Usage(),
};

int Usage()
{
    Console.Error.WriteLine("""
        usage: sentinel review [--staged] [--base <ref>] [--llm] [--threshold 0.8] [--no-cache]
                      [--autofix --project <test project|sln> [--max-cost 2] [--max-turns 40] [--attempts 2]]
               sentinel hook pre-tool|post-tool|stop   (hook payload on stdin)
               sentinel watch [--llm]
               sentinel audit verify
               sentinel gate --base <ref> [--project <dir>] [--max-lines 150] [--max-files 5]
               sentinel fix --alert <alert.json> --project <test project|sln> [--max-cost 2] [--max-turns 40] [--attempts 2]
               sentinel eval
        """);
    return 64;
}

async Task<int> ReviewCmd(string[] a)
{
    string? Opt(string k) { var i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    var ic = System.Globalization.CultureInfo.InvariantCulture;
    var autofix = a.Contains("--autofix");
    var project = Opt("--project");
    if (autofix && project is null) { Console.Error.WriteLine("sentinel: review --autofix needs --project <test project|sln>"); return 64; }
    var threshold = double.Parse(Opt("--threshold") ?? "0.8", ic);
    var diff = Diff.Working(root, Opt("--base"), a.Contains("--staged"));
    var report = await RunReview(diff, a.Contains("--llm"), threshold, forceRefresh: a.Contains("--no-cache"));
    Console.Write(Review.ToMarkdown(report, threshold));

    if (!autofix || report.Verdicts.FirstOrDefault(v => Review.ShouldRoute(v, threshold)) is not { } routed) return report.Blocking ? 1 : 0;

    Console.Error.WriteLine($"sentinel: routing {routed.Reviewer} {routed.Kind}@{routed.Confidence:0.##} {routed.File}:{routed.Line} to sentinel-fixer");
    var r = await Fix.RunFromVerdictAsync(root, routed, diff.Length > 60000 ? diff[..60000] : diff, new(project!,
        double.Parse(Opt("--max-cost") ?? "2", ic), int.Parse(Opt("--max-turns") ?? "40"), int.Parse(Opt("--attempts") ?? "2")));
    Console.Write(Fix.ToMarkdown(r));
    Save("autofix.json", JsonSerializer.Serialize(new { verdict = routed, fix = r }, Json.Options));
    Audit.Append(root, new { kind = "autofix", verdict = routed, r.Pass, r.TotalCost, attempts = r.Attempts.Count });
    return r.Pass ? 0 : 1;
}

int PreTool()
{
    var enforce = Environment.GetEnvironmentVariable("SENTINEL_POLICY") != "audit";
    try
    {
        using var doc = JsonDocument.Parse(Console.In.ReadToEnd());
        var p = doc.RootElement;
        var d = Policy.Evaluate(p, root);
        var input = Policy.Input(p);
        Audit.Append(root, new
        {
            kind = "pre-tool",
            tool = Policy.ToolName(p),
            path = Policy.Str(input, "path") ?? Policy.Str(input, "file_path"),
            command = Policy.Str(input, "command") is { } c ? c[..Math.Min(c.Length, 300)] : null,
            allow = d.Allow, rule = d.Rule, enforce,
        });
        if (d.Allow) return 0;
        Console.Error.WriteLine($"sentinel: {(enforce ? "DENIED" : "would deny (audit-only)")} [{d.Rule}] {d.Reason}");
        return enforce ? 2 : 0;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"sentinel: policy error: {e.Message}");
        return enforce ? 2 : 0; // fail closed only when enforcing
    }
}

int PostTool()
{
    var enforce = Environment.GetEnvironmentVariable("SENTINEL_POLICY") != "audit";
    try
    {
        using var doc = JsonDocument.Parse(Console.In.ReadToEnd());
        var input = Policy.Input(doc.RootElement);
        if ((Policy.Str(input, "path") ?? Policy.Str(input, "file_path")) is not { } p) return 0;
        var findings = SecretScanner.Scan(Diff.AddedLines(Diff.Working(root, path: p)));
        if (findings.Count == 0) return 0;
        Audit.Append(root, new { kind = "post-tool", path = p, findings });
        var blocking = findings.Any(f => f.Severity >= Severity.High);
        foreach (var f in findings) Console.Error.WriteLine($"sentinel: {(blocking && enforce ? "DENIED" : "WARNING")} [{f.Rule}] {f.Severity} {f.File}:{f.Line} {f.Message}");
        if (blocking) Console.Error.WriteLine($"sentinel: post-tool: {(enforce ? "DENIED" : "would deny (audit-only)")} — secret found in written file; rotate it and move to env/vault");
        return blocking && enforce ? 2 : 0;
    }
    catch (Exception e) { Console.Error.WriteLine($"sentinel: post-tool scan failed: {e.Message}"); }
    return 0;
}

async Task<int> Stop()
{
    if (Environment.GetEnvironmentVariable("SENTINEL_REVIEWER") == "1") return 0; // reviewer's own bob session: don't recurse
    try
    {
        var report = await RunReview(Diff.Working(root), llm: false);
        var md = Review.ToMarkdown(report);
        Save("review.md", md);
        Audit.Append(root, new { kind = "stop-review", findings = report.Findings.Count, report.Blocking });
        Console.Error.Write(md);
    }
    catch (Exception e) { Console.Error.WriteLine($"sentinel: stop review failed: {e.Message}"); }
    return 0;
}

async Task<int> Watch(bool llm)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    string[] ignored = ["bin", "obj", ".git", ".sentinel", "node_modules"];
    var lastChange = DateTime.MinValue;
    var pending = true; // review once at startup

    using var w = new FileSystemWatcher(root) { IncludeSubdirectories = true, EnableRaisingEvents = true };
    FileSystemEventHandler onChange = (_, e) =>
    {
        if (Path.GetRelativePath(root, e.FullPath).Split(Path.DirectorySeparatorChar).Any(ignored.Contains)) return;
        lastChange = DateTime.UtcNow;
        pending = true;
    };
    w.Changed += onChange; w.Created += onChange; w.Deleted += onChange;
    w.Renamed += (s, e) => onChange(s, e);

    Console.Error.WriteLine($"sentinel: watching {root} (llm={llm}), Ctrl+C to stop");
    while (!cts.IsCancellationRequested)
    {
        try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { break; }
        if (!pending || DateTime.UtcNow - lastChange < TimeSpan.FromSeconds(3)) continue;
        pending = false;
        try
        {
            var r = await RunReview(Diff.Working(root), llm, ct: cts.Token);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {(r.Blocking ? "BLOCKING" : "ok")}: {r.Findings.Count} finding(s)");
            foreach (var f in r.Findings.Take(10)) Console.WriteLine($"  {f.Severity,-8} {f.Source,-11} {f.File}:{f.Line} {f.Rule}: {f.Message}");
            foreach (var v in r.Verdicts.Where(v => v.Kind != "clean")) Console.WriteLine($"  {v.Kind,-8} {v.Reviewer,-11} {v.File}:{v.Line} {v.Rule} ({v.Confidence:0.##})");
        }
        catch (OperationCanceledException) { break; }
        catch (Exception e) { Console.Error.WriteLine($"sentinel: review failed: {e.Message}"); }
    }
    return 0;
}

int VerifyAudit()
{
    var (ok, bad) = Audit.Verify(root);
    Console.WriteLine(ok ? "audit log OK" : $"audit log TAMPERED at line {bad}");
    return ok ? 0 : 1;
}

async Task<ReviewReport> RunReview(string diff, bool llm, double threshold = 0.8, bool forceRefresh = false, CancellationToken ct = default)
{
    var report = await Review.RunAsync(root, diff, new(Llm: llm, Threshold: threshold, ForceRefresh: forceRefresh), ct);
    Save("review.json", JsonSerializer.Serialize(report, Json.Options));
    return report;
}

async Task<int> GateCmd(string[] a)
{
    string? Opt(string k) { var i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    if (Opt("--base") is not { } baseRef) { Console.Error.WriteLine("usage: sentinel gate --base <ref> [--project <dir>] [--max-lines 150] [--max-files 5]"); return 64; }
    GateResult r;
    try
    {
        r = await Gate.RunAsync(root, new(baseRef, Opt("--project") ?? ".",
            int.Parse(Opt("--max-lines") ?? "150"), int.Parse(Opt("--max-files") ?? "5")));
    }
    catch (Exception e) when (e is InvalidOperationException or FormatException) { Console.Error.WriteLine($"sentinel: gate: {e.Message}"); return 1; }
    Console.Write(Gate.ToMarkdown(r));
    Save("gate.json", JsonSerializer.Serialize(r, Json.Options));
    return r.Pass ? 0 : 1;
}

async Task<int> FixCmd(string[] a)
{
    string? Opt(string k) { var i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    if (Opt("--alert") is not { } alert || Opt("--project") is not { } project)
    {
        Console.Error.WriteLine("usage: sentinel fix --alert <alert.json> --project <test project|sln> [--max-cost 2] [--max-turns 40] [--attempts 2]");
        return 64;
    }
    FixResult r;
    try
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        r = await Fix.RunAsync(root, File.ReadAllText(alert), new(project,
            double.Parse(Opt("--max-cost") ?? "2", ic), int.Parse(Opt("--max-turns") ?? "40"), int.Parse(Opt("--attempts") ?? "2")));
    }
    catch (Exception e) when (e is InvalidOperationException or FormatException or JsonException or IOException)
    {
        Console.Error.WriteLine($"sentinel: fix: {e.Message}");
        return 1;
    }
    Console.Write(Fix.ToMarkdown(r));
    Save("fix.json", JsonSerializer.Serialize(r, Json.Options));
    Audit.Append(root, new { kind = "fix", alert = r.AlertId, r.BaseSha, r.Pass, r.TotalCost, attempts = r.Attempts.Count });
    return r.Pass ? 0 : 1;
}

async Task<int> EvalCmd()
{
    var r = await Eval.RunAsync(root);
    var md = Eval.ToMarkdown(r);
    Console.Write(md);
    Save("eval.json", JsonSerializer.Serialize(r, Json.Options));
    return r.AuditIntegrity ? 0 : 1;
}

void Save(string name, string content)
{
    var dir = Path.Combine(root, ".sentinel");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, name), content);
}

