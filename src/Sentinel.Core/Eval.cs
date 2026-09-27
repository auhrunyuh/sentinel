using System.Text;
using System.Text.Json;

namespace Sentinel.Core;

/// <summary>Aggregate stats for one kind of check (e.g. scanner findings, gate checks).</summary>
public sealed record EvalMetric(string Name, int Total, int Pass, int Fail)
{
    public double PassRate => Total == 0 ? 0 : (double)Pass / Total;
}

/// <summary>Per-attempt cost and gate outcome for a single fix run.</summary>
public sealed record EvalFixSummary(string? AlertId, bool Pass, double TotalCost, int Attempts);

/// <summary>Aggregate report produced by <see cref="Eval.Compute"/>.</summary>
public sealed record EvalReport(
    EvalMetric Scanner,
    EvalMetric ReviewerAgreement,
    EvalMetric GateChecks,
    EvalMetric Fixes,
    double MeanCostPerFix,
    double TotalSpend,
    int AuditEntries,
    bool AuditIntegrity
);

/// <summary>
/// Offline evaluation: reads saved artefacts from <c>.sentinel/</c> and computes quality metrics
/// without calling Bob or running tests.
/// <para>Metrics:</para>
/// <list type="bullet">
///   <item><b>Scanner</b> — findings classified High+ vs lower-severity, as a proxy for signal density.</item>
///   <item><b>ReviewerAgreement</b> — fraction of LLM verdicts that are non-clean (the reviewers found something).</item>
///   <item><b>GateChecks</b> — pass/fail breakdown across all individual gate checks ever run.</item>
///   <item><b>Fixes</b> — fraction of fix attempts that ultimately passed the gate.</item>
///   <item><b>MeanCostPerFix</b> — average USD spent per fix (successful or not).</item>
/// </list>
/// </summary>
public static class Eval
{
    /// <summary>
    /// Compute metrics purely from in-memory data — no I/O, fully testable.
    /// </summary>
    /// <param name="reviews">Deserialised <c>review.json</c> entries.</param>
    /// <param name="fixes">Deserialised <c>fix.json</c> entries.</param>
    /// <param name="auditEntries">Number of lines in <c>audit.jsonl</c>.</param>
    /// <param name="auditIntegrity">Result of <see cref="Audit.Verify"/>.</param>
    public static EvalReport Compute(
        IReadOnlyList<ReviewReport> reviews,
        IReadOnlyList<FixResult> fixes,
        int auditEntries,
        bool auditIntegrity)
    {
        // Scanner: count High+ findings (signal) vs lower-severity ones (noise proxy)
        var allFindings = reviews.SelectMany(r => r.Findings).ToList();
        var highPlus = allFindings.Count(f => f.Severity >= Severity.High);
        var lowerSev = allFindings.Count - highPlus;
        var scanner = new EvalMetric("scanner", allFindings.Count, highPlus, lowerSev);

        // ReviewerAgreement: non-clean verdicts = the reviewer found something worth flagging
        var allVerdicts = reviews.SelectMany(r => r.Verdicts).ToList();
        var nonClean = allVerdicts.Count(v => v.Kind != "clean");
        var clean = allVerdicts.Count - nonClean;
        var agreement = new EvalMetric("reviewer-agreement", allVerdicts.Count, nonClean, clean);

        // GateChecks: raw pass/fail across every individual GateCheck
        var allChecks = fixes.SelectMany(f => f.Gate?.Checks ?? []).ToList();
        var gPass = allChecks.Count(c => c.Pass);
        var gFail = allChecks.Count - gPass;
        var gateChecks = new EvalMetric("gate-checks", allChecks.Count, gPass, gFail);

        // Fixes: how many fix runs ultimately passed
        var fixPass = fixes.Count(f => f.Pass);
        var fixFail = fixes.Count - fixPass;
        var fixMetric = new EvalMetric("fixes", fixes.Count, fixPass, fixFail);

        var totalSpend = fixes.Sum(f => f.TotalCost);
        var meanCost = fixes.Count == 0 ? 0 : totalSpend / fixes.Count;

        return new(scanner, agreement, gateChecks, fixMetric, meanCost, totalSpend, auditEntries, auditIntegrity);
    }

    /// <summary>
    /// Load artefacts from <paramref name="root"/>/<c>.sentinel/</c> and call <see cref="Compute"/>.
    /// Returns <c>null</c> for each artefact that doesn't exist yet (first run).
    /// </summary>
    public static async Task<EvalReport> RunAsync(string root, CancellationToken ct = default)
    {
        var dir = Path.Combine(root, ".sentinel");

        var reviews = await LoadAllAsync<ReviewReport>(dir, "review.json", ct);
        var fixes = await LoadAllAsync<FixResult>(dir, "fix.json", ct);

        // Autofix results live in autofix.json as {verdict, fix} wrapper
        var autofixResults = (await LoadAllAsync<AutofixWrapper>(dir, "autofix.json", ct))
            .Select(w => w.Fix)
            .Where(f => f is not null)
            .Cast<FixResult>()
            .ToList();

        fixes = [.. fixes, .. autofixResults];

        var auditFile = Audit.PathFor(root);
        var auditEntries = 0;
        if (File.Exists(auditFile))
            auditEntries = (await File.ReadAllLinesAsync(auditFile, ct)).Count(l => l.Length > 0);

        var (auditOk, _) = Audit.Verify(root);
        return Compute(reviews, fixes, auditEntries, auditOk);
    }

    /// <summary>
    /// Loads a JSON file as a single T, or a JSON-array of T (if the file holds a list), or returns empty.
    /// </summary>
    static async Task<List<T>> LoadAllAsync<T>(string dir, string name, CancellationToken ct)
    {
        var path = Path.Combine(dir, name);
        if (!File.Exists(path)) return [];
        try
        {
            var text = await File.ReadAllTextAsync(path, ct);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return doc.RootElement.Deserialize<List<T>>(Json.Options) ?? [];
            var item = doc.RootElement.Deserialize<T>(Json.Options);
            return item is null ? [] : [item];
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Thin wrapper that matches the shape of autofix.json.</summary>
    private sealed record AutofixWrapper(Verdict? Verdict, FixResult? Fix);

    public static string ToMarkdown(EvalReport r)
    {
        var sb = new StringBuilder("# Sentinel eval\n\n");

        sb.AppendLine("## Metrics\n");
        sb.AppendLine("| Metric | Total | Pass | Fail | Pass% |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var m in new[] { r.Scanner, r.ReviewerAgreement, r.GateChecks, r.Fixes })
            sb.AppendLine($"| {m.Name} | {m.Total} | {m.Pass} | {m.Fail} | {m.PassRate:P1} |");

        sb.AppendLine();
        sb.AppendLine("## Cost\n");
        sb.AppendLine($"- Total spend: **{r.TotalSpend:0.####} Bobcoins**");
        sb.AppendLine($"- Mean cost per fix: **{r.MeanCostPerFix:0.####} Bobcoins**");

        sb.AppendLine();
        sb.AppendLine("## Audit\n");
        sb.AppendLine($"- Entries: {r.AuditEntries}");
        sb.AppendLine($"- Integrity: {(r.AuditIntegrity ? "✓ ok" : "✗ TAMPERED")}");

        return sb.ToString();
    }
}
