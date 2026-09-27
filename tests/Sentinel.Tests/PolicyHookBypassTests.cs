using System.Text.Json;
using Sentinel.Core;

/// <summary>
/// Focused regression tests for the no-hook-bypass policy rule.
/// False-positive cases (should be allowed) and true-positive cases (should be denied).
/// </summary>
public class PolicyHookBypassTests
{
    static PolicyDecision Eval(string cmd)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var json = $"{{\"tool_name\":\"execute_command\",\"input\":{{\"command\":{JsonSerializer.Serialize(cmd)}}}}}";
        return Policy.Evaluate(JsonDocument.Parse(json).RootElement, root);
    }

    // ── False positives: these must be ALLOWED ──────────────────────────────

    [Fact]
    public void Allow_CommitMessageContainingNonNull()
    {
        // "non-null" inside a quoted message used to trigger -[a-z]*n[a-z]*
        var d = Eval(@"git commit -m ""add non-null check""");
        Assert.True(d.Allow, $"Expected Allow but got rule={d.Rule}: {d.Reason}");
    }

    [Fact]
    public void Allow_CommitSignoff()
    {
        // --signoff (long flag) must not be mistaken for a hook-bypass flag
        var d = Eval("git commit --signoff -m msg");
        Assert.True(d.Allow, $"Expected Allow but got rule={d.Rule}: {d.Reason}");
    }

    [Fact]
    public void Allow_MergeNoFf()
    {
        // --no-ff contains "-no" inside it; the single-dash cluster inside "--no-ff"
        // must not trigger the rule
        var d = Eval("git merge --no-ff feature");
        Assert.True(d.Allow, $"Expected Allow but got rule={d.Rule}: {d.Reason}");
    }

    // ── True positives: these must be DENIED ────────────────────────────────

    [Fact]
    public void Deny_CommitShortFlagN()
    {
        // git commit -n is the short form of --no-verify
        var d = Eval("git commit -n -m x");
        Assert.Equal("no-hook-bypass", d.Rule);
    }

    [Fact]
    public void Deny_CommitNoVerify()
    {
        var d = Eval("git commit --no-verify -m msg");
        Assert.Equal("no-hook-bypass", d.Rule);
    }

    [Fact]
    public void Deny_GitCCoreHooksPath()
    {
        var d = Eval("git -c core.hooksPath=/dev/null commit -m msg");
        Assert.Equal("no-hook-bypass", d.Rule);
    }
}
