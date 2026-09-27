using System.Text;
using System.Text.RegularExpressions;

namespace Sentinel.Core;

public sealed record GateCheck(string Name, bool Pass, string Detail);

public sealed record GateResult(bool Pass, IReadOnlyList<string> NewTests, IReadOnlyList<GateCheck> Checks);

public sealed record GateOptions(string BaseRef, string Project = ".", int MaxLines = 150, int MaxFiles = 5);

public enum TestRunOutcome { Passed, Failed, BuildError }

/// <summary>Fail-to-pass gate: the fix must add a test that fails on base and passes on the working tree.</summary>
public static partial class Gate
{
    [GeneratedRegex(@"\[(Fact|Theory)\b")]
    private static partial Regex TestAttr();

    [GeneratedRegex(@"\b(?:void|Task)\s+(\w+)\s*\(")]
    private static partial Regex MethodDecl();

    /// <summary>Method names of added [Fact]/[Theory] tests: attribute on an added line, then the next added method declaration.</summary>
    public static List<string> NewTestMethods(string diff)
    {
        var names = new List<string>();
        foreach (var file in Diff.AddedLines(diff).Where(l => Diff.IsTestFile(l.File)).GroupBy(l => l.File))
        {
            var pending = false;
            foreach (var l in file)
            {
                var a = TestAttr().Match(l.Text);
                if (a.Success) pending = true;
                if (!pending) continue;
                var m = MethodDecl().Match(l.Text, a.Success ? a.Index : 0);
                if (m.Success) { names.Add(m.Groups[1].Value); pending = false; }
            }
        }
        return names.Distinct().ToList();
    }

    /// <summary>Per file: (added, removed) line counts. A file with removed lines existed at base.</summary>
    public static Dictionary<string, (int Added, int Removed)> Stat(string diff)
    {
        var stat = new Dictionary<string, (int Added, int Removed)>();
        foreach (var l in Diff.Lines(diff))
        {
            var (a, r) = stat.GetValueOrDefault(l.File);
            stat[l.File] = l.Sign == '+' ? (a + 1, r) : (a, r + 1);
        }
        return stat;
    }

    /// <summary>Existing test files the diff changes or deletes lines in (pure additions are allowed).</summary>
    public static List<string> ModifiedExistingTests(string diff) =>
        Stat(diff).Where(kv => Diff.IsTestFile(kv.Key) && kv.Value.Removed > 0).Select(kv => kv.Key).ToList();

    public static async Task<GateResult> RunAsync(string root, GateOptions o, CancellationToken ct = default)
    {
        var diff = Diff.Working(root, o.BaseRef);
        var tests = NewTestMethods(diff);
        var modified = ModifiedExistingTests(diff);
        var stat = Stat(diff);
        var checks = new List<GateCheck>
        {
            new("new-tests", tests.Count > 0, tests.Count > 0 ? string.Join(", ", tests) : "no added [Fact]/[Theory] methods in *Tests*.cs"),
            new("existing-tests-untouched", modified.Count == 0, modified.Count == 0 ? "ok" : "changed/removed lines in " + string.Join(", ", modified)),
        };

        if (checks.All(c => c.Pass))
        {
            // ponytail: substring match on method name; collisions (Foo vs FooBar) just run extra tests.
            var filter = string.Join('|', tests.Select(t => $"FullyQualifiedName~.{t}"));
            var testFiles = stat.Keys.Where(f => Diff.IsTestFile(f) && stat[f].Added > 0).ToList();

            var tmp = Path.Combine(Path.GetTempPath(), "sentinel-gate-" + Guid.NewGuid().ToString("N")[..8]);
            var add = await Proc.RunAsync("git", ["worktree", "add", "--detach", tmp, o.BaseRef], root, ct: ct);
            if (add.Code != 0) checks.Add(new("fails-on-base", false, "git worktree add: " + add.Stderr.Trim()));
            else
            {
                try
                {
                    foreach (var f in testFiles)
                    {
                        var dst = Path.Combine(tmp, f);
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        File.Copy(Path.Combine(root, f), dst, overwrite: true);
                    }
                    var b = await DotnetTest(tmp, o.Project, filter, ct);
                    checks.Add(ClassifyTestRun(b.Stdout + b.Stderr) switch
                    {
                        TestRunOutcome.BuildError => new("fails-on-base", false, "repro test does not compile on base; write a test against existing API"),
                        TestRunOutcome.Failed => new("fails-on-base", true, "new tests fail on base: " + Tail(b)),
                        _ => new("fails-on-base", false, "new tests PASS on base, they don't reproduce the bug"),
                    });
                }
                finally
                {
                    await Proc.RunAsync("git", ["worktree", "remove", "--force", tmp], root);
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
                }
            }

            var h = await DotnetTest(root, o.Project, filter, ct);
            checks.Add(new("passes-on-head", h.Code == 0, h.Code == 0 ? Tail(h) : "new tests fail with the fix: " + Tail(h)));
            var all = await DotnetTest(root, o.Project, null, ct);
            checks.Add(new("full-suite", all.Code == 0, Tail(all)));
        }
        else
        {
            foreach (var name in new[] { "fails-on-base", "passes-on-head", "full-suite" })
                checks.Add(new(name, false, "skipped"));
        }

        var lines = stat.Values.Sum(v => v.Added + v.Removed);
        checks.Add(new("diff-budget", lines <= o.MaxLines && stat.Count <= o.MaxFiles,
            $"{lines}/{o.MaxLines} lines, {stat.Count}/{o.MaxFiles} files"));

        return new(checks.All(c => c.Pass), tests, checks);
    }

    public static string ToMarkdown(GateResult r)
    {
        var sb = new StringBuilder($"## Sentinel gate: {(r.Pass ? "PASS" : "FAIL")}\n\n| check | result | detail |\n|---|---|---|\n");
        foreach (var c in r.Checks)
            sb.Append($"| {c.Name} | {(c.Pass ? "pass" : "FAIL")} | {c.Detail.Replace('|', '/').Replace('\n', ' ')} |\n");
        return sb.ToString();
    }

    /// <summary>Distinguishes a real test failure from a build error in `dotnet test` output (a build error must not count as fails-on-base).</summary>
    public static TestRunOutcome ClassifyTestRun(string output)
    {
        if (output.Contains("error CS") || output.Contains("Build FAILED")) return TestRunOutcome.BuildError;
        var failedCount = FailedCount().Match(output);
        if (output.Contains("Failed!") || (failedCount.Success && int.Parse(failedCount.Groups[1].Value) > 0)) return TestRunOutcome.Failed;
        return TestRunOutcome.Passed;
    }

    [GeneratedRegex(@"Failed:\s*(\d+)")]
    private static partial Regex FailedCount();

    internal static Task<ProcResult> DotnetTest(string dir, string project, string? filter, CancellationToken ct) =>
        Proc.RunAsync("dotnet", ["test", project, .. filter is null ? Array.Empty<string>() : ["--filter", filter]], dir, ct: ct);

    internal static string Tail(ProcResult r)
    {
        var l = (r.Stdout + r.Stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return l.LastOrDefault(x => x.StartsWith("Passed!") || x.StartsWith("Failed!") || x.Contains("error")) ?? $"exit {r.Code}";
    }
}
