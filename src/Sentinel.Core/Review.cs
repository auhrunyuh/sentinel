using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sentinel.Core;

public sealed record ReviewOptions(bool Llm = false, double MaxCost = 0.5, int MaxTurns = 1, int MaxDiffChars = 60000, double Threshold = 0.8);
public sealed record ReviewerRun(string Reviewer, bool Ok, bool Cached, double Cost, long Tokens, long DurationMs, string? Error);
/// <param name="Kind">clean | leak | vuln | slow</param>
public sealed record Verdict(string Reviewer, string Kind, double Confidence, string File, int Line, string Rule);
public sealed record ReviewReport(List<Finding> Findings, List<Verdict> Verdicts, List<ReviewerRun> Runs, bool Blocking);

public static class Review
{
    /// <summary>Verdict in, decision out. Only confident security verdicts reach the fixer; perf is advisory.</summary>
    public static bool ShouldRoute(Verdict v, double threshold = 0.8) =>
        v.Reviewer == "security" && v.Kind is "leak" or "vuln" && v.Confidence >= threshold;

    // Verdict agents: no tools, one turn, ~30-token answer. Focus lives in the mode's rules file.
    static readonly (string Name, string Mode)[] Reviewers = [("security", "sentinel-sec-review"), ("performance", "sentinel-perf-review")];
    static readonly string[] Kinds = ["clean", "leak", "vuln", "slow"];

    public static async Task<ReviewReport> RunAsync(string root, string diff, ReviewOptions? opt = null, CancellationToken ct = default)
    {
        opt ??= new();
        var added = Diff.AddedLines(diff).ToList();
        var findings = SecretScanner.Scan(added)
            .DistinctBy(f => (f.File, f.Line, f.Rule))
            .OrderByDescending(f => f.Severity).ThenBy(f => f.File).ThenBy(f => f.Line)
            .ToList();
        var verdicts = new List<Verdict>();
        var runs = new List<ReviewerRun>();

        if (opt.Llm && added.Count > 0)
        {
            var d = diff.Length > opt.MaxDiffChars ? diff[..opt.MaxDiffChars] + "\n[diff truncated]\n" : diff;
            var results = await Task.WhenAll(Reviewers.Select(r => RunOne(root, r.Name, r.Mode, Prompt(d), opt, ct)));
            foreach (var (run, v) in results) { runs.Add(run); verdicts.Add(v); }
        }

        return new(findings, verdicts, runs,
            findings.Any(f => f.Severity >= Severity.High) || verdicts.Any(v => ShouldRoute(v, opt.Threshold)));
    }

    static string Prompt(string diff) => $$"""
        Judge ONLY '+' lines of this diff; report the single worst issue.
        Reply ONLY: <verdict>{"verdict":"clean|leak|vuln|slow","confidence":0-1,"file":"path","line":N,"rule":"kebab-id"}</verdict>

        ```diff
        {{diff}}
        ```
        """;

    /// <summary>Unparseable or invalid output → clean@0 plus an error, never a crash.</summary>
    public static (Verdict Verdict, string? Error) ParseVerdict(string reviewer, string message)
    {
        var start = message.IndexOf("<verdict>", StringComparison.Ordinal);
        var end = message.IndexOf("</verdict>", StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            try
            {
                using var doc = JsonDocument.Parse(message[(start + 9)..end]);
                var e = doc.RootElement;
                var kind = e.GetProperty("verdict").GetString()?.ToLowerInvariant();
                var conf = e.GetProperty("confidence").GetDouble();
                if (Kinds.Contains(kind) && conf is >= 0 and <= 1)
                    return (new(reviewer, kind!, conf,
                        e.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()! : "",
                        e.TryGetProperty("line", out var l) && l.TryGetInt32(out var n) ? n : 0,
                        e.TryGetProperty("rule", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "unspecified"), null);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        return (new(reviewer, "clean", 0, "", 0, "unparseable-output"),
            $"unparseable verdict: {message[..Math.Min(200, message.Length)]}");
    }

    static async Task<(ReviewerRun, Verdict)> RunOne(string root, string name, string mode, string prompt, ReviewOptions opt, CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mode + "\n" + prompt)));
        var cache = Path.Combine(root, ".sentinel", "cache", hash + ".json");
        BobResult? res = null;
        var cached = false;
        try
        {
            if (File.Exists(cache)) { res = JsonSerializer.Deserialize<BobResult>(await File.ReadAllTextAsync(cache, ct), Json.Options); cached = res is not null; }
        }
        catch (JsonException) { }

        if (res is null)
        {
            res = await Bob.RunAsync(root, mode, prompt, opt.MaxCost, opt.MaxTurns,
                new Dictionary<string, string> { ["SENTINEL_REVIEWER"] = "1" }, ct: ct);
            if (res.Ok)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(res, Json.Options), ct);
            }
        }

        var (v, err) = res.Ok ? ParseVerdict(name, res.LastMessage) : (new(name, "clean", 0, "", 0, "reviewer-failed"), null);
        return (new ReviewerRun(name, res.Ok && err is null, cached, res.Cost, res.Tokens, res.DurationMs, res.Error ?? err), v);
    }

    public static string ToMarkdown(ReviewReport r, double threshold = 0.8)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Sentinel review: {(r.Blocking ? "BLOCKED" : "pass")}").AppendLine();
        if (r.Findings.Count == 0) sb.AppendLine("No scanner findings.");
        else
        {
            sb.AppendLine("| Severity | Location | Rule | Message | Fix |").AppendLine("|---|---|---|---|---|");
            foreach (var f in r.Findings)
                sb.AppendLine($"| {f.Severity} | `{f.File}:{f.Line}` | {f.Rule} | {Cell(f.Message)} | {Cell(f.Fix ?? "")} |");
        }
        if (r.Verdicts.Count > 0)
        {
            sb.AppendLine().AppendLine("## Verdicts").AppendLine().AppendLine("| Reviewer | Verdict | Confidence | Location | Rule | Route |").AppendLine("|---|---|---|---|---|---|");
            foreach (var v in r.Verdicts)
                sb.AppendLine($"| {v.Reviewer} | {v.Kind} | {v.Confidence:0.##} | `{v.File}:{v.Line}` | {v.Rule} | {(ShouldRoute(v, threshold) ? "fixer" : v.Kind == "clean" ? "-" : "advisory")} |");
        }
        if (r.Runs.Count > 0)
        {
            sb.AppendLine().AppendLine("## Reviewers").AppendLine();
            foreach (var x in r.Runs)
                sb.AppendLine($"- {x.Reviewer}: {(x.Ok ? "ok" : "FAILED")}{(x.Cached ? " (cached)" : "")}, ${x.Cost:0.####}, {x.Tokens} tokens, {x.DurationMs} ms{(x.Error is null ? "" : $" - {x.Error}")}");
        }
        return sb.ToString();
    }

    static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
