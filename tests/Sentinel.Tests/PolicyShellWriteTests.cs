using System.Text.Json;
using Sentinel.Core;

/// <summary>
/// Regression tests for the no-shell-write and outside-workspace-exec policy rules
/// introduced after the demo incident where sentinel-fixer wrote files via shell
/// redirection and created repro tests outside the workspace under /tmp.
/// </summary>
public class PolicyShellWriteTests
{
    static PolicyDecision Eval(string cmd, string? root = null)
    {
        root ??= Directory.CreateTempSubdirectory().FullName;
        var json = $"{{\"tool_name\":\"execute_command\",\"input\":{{\"command\":{JsonSerializer.Serialize(cmd)}}}}}";
        return Policy.Evaluate(JsonDocument.Parse(json).RootElement, root);
    }

    // ── no-shell-write: DENY cases ───────────────────────────────────────────

    [Fact]
    public void Deny_CatRedirectToFile()
    {
        var d = Eval("cat > src/X.cs");
        Assert.Equal("no-shell-write", d.Rule);
    }

    [Fact]
    public void Deny_EchoAppendToFile()
    {
        var d = Eval("echo hi >> a.cs");
        Assert.Equal("no-shell-write", d.Rule);
    }

    [Fact]
    public void Deny_SedInPlace()
    {
        var d = Eval("sed -i s/a/b/ f.cs");
        Assert.Equal("no-shell-write", d.Rule);
    }

    [Fact]
    public void Deny_SedInPlaceLongFlag()
    {
        var d = Eval("sed --in-place 's/foo/bar/' file.cs");
        Assert.Equal("no-shell-write", d.Rule);
    }

    [Fact]
    public void Deny_CpTwoArgs()
    {
        var d = Eval("cp a b");
        Assert.Equal("no-shell-write", d.Rule);
    }

    [Fact]
    public void Deny_MvFile()
    {
        var d = Eval("mv old.cs new.cs");
        Assert.Equal("no-shell-write", d.Rule);
    }

    [Fact]
    public void Deny_TeeToFile()
    {
        var d = Eval("echo hello | tee output.txt");
        Assert.Equal("no-shell-write", d.Rule);
    }

    [Fact]
    public void Deny_PerlInPlace()
    {
        var d = Eval("perl -pi -e 's/foo/bar/' file.cs");
        Assert.Equal("no-shell-write", d.Rule);
    }

    // ── no-shell-write: ALLOW cases ─────────────────────────────────────────

    [Fact]
    public void Allow_DotnetTestWithStderrRedirect()
    {
        // 2>&1 must NOT trigger no-shell-write
        var d = Eval("dotnet test 2>&1");
        Assert.True(d.Allow, $"Expected Allow but got rule={d.Rule}: {d.Reason}");
    }

    [Fact]
    public void Allow_DotnetBuildToDevNull()
    {
        // > /dev/null must NOT trigger no-shell-write
        var d = Eval("dotnet build > /dev/null");
        Assert.True(d.Allow, $"Expected Allow but got rule={d.Rule}: {d.Reason}");
    }

    [Fact]
    public void Allow_GitStatus()
    {
        var d = Eval("git status");
        Assert.True(d.Allow, $"Expected Allow but got rule={d.Rule}: {d.Reason}");
    }

    // ── outside-workspace-exec: DENY cases ──────────────────────────────────

    [Fact]
    public void Deny_CdIntoTmpAndDotnetTest()
    {
        // workspace root is somewhere other than /tmp
        var root = Path.Combine(Path.GetTempPath().Contains("/tmp")
            ? "/private/var/workspace" : "/workspace", "myproject");
        // Use a hardcoded non-tmp root so the /tmp reference is clearly outside
        var d = Eval("cd /tmp/x && dotnet test", "/Users/developer/myproject");
        Assert.Equal("outside-workspace-exec", d.Rule);
    }

    [Fact]
    public void Deny_ReferenceTmpPath()
    {
        var d = Eval("dotnet test /tmp/bug02repro/Repro.Tests.csproj", "/Users/developer/myproject");
        Assert.Equal("outside-workspace-exec", d.Rule);
    }

    // ── outside-workspace-exec: ALLOW cases ─────────────────────────────────

    [Fact]
    public void Allow_WorkspaceRootUnderVarFolders()
    {
        // The workspace itself lives under /var/folders (common on macOS for temp dirs in CI).
        // A command referencing a path inside that root must be allowed.
        var root = "/var/folders/abc/def/T/myworkspace";
        var d = Eval($"dotnet test {root}/tests/Sentinel.Tests", root);
        Assert.True(d.Allow, $"Expected Allow but got rule={d.Rule}: {d.Reason}");
    }
}
