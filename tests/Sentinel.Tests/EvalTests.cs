using Sentinel.Core;

public class EvalTests
{
    // Helpers to build minimal test objects without constructing full runs
    static Finding F(Severity s) => new("scanner", "test-rule", s, "f.cs", 1, "msg");
    static Verdict V(string kind, double conf = 0.9) => new("security", kind, conf, "f.cs", 1, "rule");
    static GateCheck G(bool pass) => new("check", pass, "detail");

    [Fact]
    public void Compute_EmptyInputs_ReturnsZeroMetrics()
    {
        var r = Eval.Compute([], [], auditEntries: 0, auditIntegrity: true);

        Assert.Equal(0, r.Scanner.Total);
        Assert.Equal(0, r.ReviewerAgreement.Total);
        Assert.Equal(0, r.GateChecks.Total);
        Assert.Equal(0, r.Fixes.Total);
        Assert.Equal(0, r.MeanCostPerFix);
        Assert.Equal(0, r.TotalSpend);
        Assert.True(r.AuditIntegrity);
    }

    [Fact]
    public void Compute_ScannerMetric_SplitsHighPlusVsLower()
    {
        // 2 High+, 1 lower — pass=2, fail=1
        var review = new ReviewReport(
            Findings: [F(Severity.Critical), F(Severity.High), F(Severity.Medium)],
            Verdicts: [],
            Runs: [],
            Blocking: true);

        var r = Eval.Compute([review], [], 0, true);

        Assert.Equal(3, r.Scanner.Total);
        Assert.Equal(2, r.Scanner.Pass);   // High+
        Assert.Equal(1, r.Scanner.Fail);   // lower-severity
        Assert.Equal(2.0 / 3.0, r.Scanner.PassRate, precision: 10);
    }

    [Fact]
    public void Compute_ReviewerAgreement_CountsNonCleanVerdicts()
    {
        var review = new ReviewReport(
            Findings: [],
            Verdicts: [V("leak"), V("clean"), V("vuln")],
            Runs: [],
            Blocking: false);

        var r = Eval.Compute([review], [], 0, true);

        Assert.Equal(3, r.ReviewerAgreement.Total);
        Assert.Equal(2, r.ReviewerAgreement.Pass);   // non-clean = found something
        Assert.Equal(1, r.ReviewerAgreement.Fail);   // clean
    }

    [Fact]
    public void Compute_GateChecks_AggregatesAcrossAllFixes()
    {
        var gate1 = new GateResult(false, [], [G(true), G(false)]);
        var gate2 = new GateResult(true, [], [G(true), G(true)]);
        var fix1 = new FixResult("a1", "sha1", false, 0.5, [], gate1, null);
        var fix2 = new FixResult("a2", "sha2", true, 1.0, [], gate2, null);

        var r = Eval.Compute([], [fix1, fix2], 5, true);

        Assert.Equal(4, r.GateChecks.Total);
        Assert.Equal(3, r.GateChecks.Pass);
        Assert.Equal(1, r.GateChecks.Fail);
        Assert.Equal(5, r.AuditEntries);
    }

    [Fact]
    public void Compute_FixMetric_CountsPassAndFail()
    {
        var fix1 = new FixResult("a1", "sha1", true,  1.20, [], null, null);
        var fix2 = new FixResult("a2", "sha2", false, 0.80, [], null, null);
        var fix3 = new FixResult("a3", "sha3", true,  1.00, [], null, null);

        var r = Eval.Compute([], [fix1, fix2, fix3], 0, true);

        Assert.Equal(3, r.Fixes.Total);
        Assert.Equal(2, r.Fixes.Pass);
        Assert.Equal(1, r.Fixes.Fail);
        Assert.Equal(2.0 / 3.0, r.Fixes.PassRate, precision: 10);
        Assert.Equal(3.0, r.TotalSpend, precision: 10);
        Assert.Equal(1.0, r.MeanCostPerFix, precision: 10);
    }

    [Fact]
    public void Compute_AuditIntegrity_ReflectedInReport()
    {
        var r = Eval.Compute([], [], auditEntries: 10, auditIntegrity: false);
        Assert.False(r.AuditIntegrity);
        Assert.Equal(10, r.AuditEntries);
    }

    [Theory]
    [InlineData(0, 0, 0)]   // no findings
    [InlineData(1, 1, 1.0)] // 1 High+, passRate=1
    [InlineData(2, 1, 0.5)] // 1 High+, 1 lower, passRate=0.5
    public void Compute_ScannerPassRate_IsCorrect(int total, int highPlus, double expectedRate)
    {
        var findings = Enumerable.Range(0, highPlus).Select(_ => F(Severity.High))
            .Concat(Enumerable.Range(0, total - highPlus).Select(_ => F(Severity.Low)))
            .ToList();
        var review = new ReviewReport(findings, [], [], false);

        var r = Eval.Compute([review], [], 0, true);

        Assert.Equal(total, r.Scanner.Total);
        Assert.Equal(expectedRate, r.Scanner.PassRate, precision: 10);
    }

    [Fact]
    public void ToMarkdown_ContainsAllSections()
    {
        var r = Eval.Compute([], [], auditEntries: 3, auditIntegrity: true);
        var md = Eval.ToMarkdown(r);

        Assert.Contains("# Sentinel eval", md);
        Assert.Contains("scanner", md);
        Assert.Contains("reviewer-agreement", md);
        Assert.Contains("gate-checks", md);
        Assert.Contains("fixes", md);
        Assert.Contains("Total spend", md);
        Assert.Contains("Entries: 3", md);
        Assert.Contains("✓ ok", md);
    }
}
