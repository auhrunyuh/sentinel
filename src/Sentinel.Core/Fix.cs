using System.Text;
using System.Text.Json;

namespace Sentinel.Core;

public sealed record FixOptions(string Project, double MaxCost = 2, int MaxTurns = 40, int Attempts = 2);
public sealed record FixAttempt(double Cost, long Tokens, long DurationMs, string? TaskId, string? BobError, bool GatePass, IReadOnlyList<GateCheck> Checks);
public sealed record FixResult(string? AlertId, string BaseSha, bool Pass, double TotalCost, List<FixAttempt> Attempts, GateResult? Gate, string? Report);

/// <summary>Alert in, gated fix out: Bob (sentinel-fixer) writes repro test + fix, the gate judges, failures get resumed.</summary>
public static class Fix
{
    public const string ReportFile = "SENTINEL_REPORT.md";

    public static async Task<FixResult> RunAsync(string root, string alertJson, FixOptions o, CancellationToken ct = default)
    {
        string? alertId;
        using (var doc = JsonDocument.Parse(alertJson)) alertId = Policy.Str(doc.RootElement, "id");

        if ((await Proc.RunAsync("git", ["status", "--porcelain"], root, ct: ct)).Stdout.Trim() is { Length: > 0 } dirty)
            throw new InvalidOperationException("working tree not clean:\n" + dirty);
        var head = await Proc.RunAsync("git", ["rev-parse", "HEAD"], root, ct: ct);
        if (head.Code != 0) throw new InvalidOperationException("git rev-parse HEAD: " + head.Stderr.Trim());
        var baseSha = head.Stdout.Trim();

        var report = Path.Combine(root, ReportFile);
        File.Delete(report); // gitignored, so a stale one would survive the clean check

        var (attempts, gate, spent) = await RetryAsync(root, Prompt(alertJson),
            g => Gate.ToMarkdown(g) + "\nFix these gate failures. Same sentinel-fixer rules apply.",
            c => Gate.RunAsync(root, new(baseSha, o.Project), c), o, ct);

        return new(alertId, baseSha, gate?.Pass == true, spent, attempts, gate,
            File.Exists(report) ? await File.ReadAllTextAsync(report, ct) : null);
    }

    /// <summary>Shared attempt/verify/resume loop for both fix entry points. First attempt uses firstPrompt; a gate
    /// failure with no resumable taskId falls back to firstPrompt plus the gate's failures; otherwise retryPrompt(gate).</summary>
    static async Task<(List<FixAttempt> Attempts, GateResult? Gate, double Spent)> RetryAsync(
        string root, string firstPrompt, Func<GateResult, string> retryPrompt,
        Func<CancellationToken, Task<GateResult>> verify, FixOptions o, CancellationToken ct)
    {
        var env = new Dictionary<string, string> { ["SENTINEL_POLICY"] = "enforce" };
        var attempts = new List<FixAttempt>();
        GateResult? gate = null;
        string? taskId = null;
        var spent = 0.0;

        for (var i = 0; i < o.Attempts && spent < o.MaxCost; i++)
        {
            var prompt = gate is null ? firstPrompt
                : taskId is null ? firstPrompt + "\n\nA previous attempt failed the gate:\n\n" + Gate.ToMarkdown(gate)
                : retryPrompt(gate);
            var bob = await Bob.RunAsync(root, "sentinel-fixer", prompt, o.MaxCost - spent, o.MaxTurns, env, taskId, ct);
            spent += bob.Cost;
            taskId = bob.TaskId ?? taskId;

            gate = await verify(ct);
            attempts.Add(new(bob.Cost, bob.Tokens, bob.DurationMs, bob.TaskId, bob.Error, gate.Pass, gate.Checks));
            if (gate.Pass) break;
        }

        return (attempts, gate, spent);
    }

    /// <summary>
    /// Router hand-off: one confident security verdict in, minimal fix out. No fail-to-pass (a removed secret has no repro test);
    /// verified by rescanning the file + full suite, failures resumed like the alert path.
    /// </summary>
    public static async Task<FixResult> RunFromVerdictAsync(string root, Verdict v, string diff, FixOptions o, CancellationToken ct = default)
    {
        var verdictJson = JsonSerializer.Serialize(new { verdict = v.Kind, v.Confidence, v.File, v.Line, v.Rule }, Json.Options);

        var (attempts, check, spent) = await RetryAsync(root, VerdictPrompt(verdictJson, diff),
            g => Gate.ToMarkdown(g) + "\nVerification failed. Fix only these failures, same rules.",
            c => Verify(root, v.File, o.Project, c), o, ct);

        return new($"{v.Reviewer}:{v.Rule}", "working-tree", check?.Pass == true, spent, attempts, check, null);
    }

    static async Task<GateResult> Verify(string root, string file, string project, CancellationToken ct)
    {
        // ponytail: rescans the whole file, not just the working diff, so a secret already committed at base can't slip past.
        var path = Path.Combine(root, file);
        var hits = file.Length > 0 && File.Exists(path)
            ? SecretScanner.ScanText(file, await File.ReadAllTextAsync(path, ct)).Where(f => f.Severity >= Severity.High).ToList()
            : [];
        var t = await Gate.DotnetTest(root, project, null, ct);
        GateCheck[] checks =
        [
            new("no-secret-in-file", hits.Count == 0, hits.Count == 0 ? "ok" : string.Join(", ", hits.Select(h => $"{h.Rule}@{h.File}:{h.Line}"))),
            new("full-suite", t.Code == 0, Gate.Tail(t)),
        ];
        return new(checks.All(c => c.Pass), [], checks);
    }

    public static string VerdictPrompt(string verdictJson, string diff) => $"""
        Reviewer verdict:
        ```json
        {verdictJson.Trim()}
        ```
        Fix only this issue, minimally. No test required for secret removal (read it from config/environment instead). Don't touch unrelated code. No {ReportFile} needed.

        ```diff
        {diff}
        ```
        """;

    public static string Prompt(string alertJson) => $"""
        Fix the bug described by this alert:

        ```json
        {alertJson.Trim()}
        ```

        Follow the sentinel-fixer rules:
        1. Write a NEW failing xUnit repro test in a NEW *Tests.cs file, using only the existing public API.
        2. Run it; it must fail before the fix.
        3. Apply the minimal fix.
        4. Run the full test suite; everything must pass.
        5. Never edit or delete existing tests. No new packages.
        6. Finish by writing {ReportFile} in the repository root: root cause, fix, risk.
        """;

    public static string ToMarkdown(FixResult r)
    {
        var sb = new StringBuilder($"# Sentinel fix{(r.AlertId is null ? "" : $" ({r.AlertId})")}: {(r.Pass ? "PASS" : "FAIL")}\n\n");
        if (r.Gate is not null) sb.Append(Gate.ToMarkdown(r.Gate)).Append('\n');
        sb.Append($"## Cost\n\nTotal {r.TotalCost:0.####} Bobcoins over {r.Attempts.Count} attempt(s), base `{r.BaseSha[..Math.Min(12, r.BaseSha.Length)]}`\n\n");
        for (var i = 0; i < r.Attempts.Count; i++)
        {
            var a = r.Attempts[i];
            sb.Append($"- attempt {i + 1}: {a.Cost:0.####} Bobcoins, {a.Tokens} tokens, {a.DurationMs} ms, gate {(a.GatePass ? "pass" : "FAIL")}\n");
            if (a.BobError is not null) sb.Append($"  bob error:\n  ```\n  {a.BobError.Replace("\n", "\n  ")}\n  ```\n");
        }
        if (r.Report is not null) sb.Append($"\n## {ReportFile}\n\n").Append(r.Report.TrimEnd()).Append('\n');
        return sb.ToString();
    }
}
