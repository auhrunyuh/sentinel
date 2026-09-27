using System.Diagnostics;

namespace Sentinel.Core;

public sealed record ProcResult(int Code, string Stdout, string Stderr);

public static class Proc
{
    public static async Task<ProcResult> RunAsync(
        string file, IEnumerable<string> args, string workdir,
        string? stdin = null, IDictionary<string, string>? env = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {file}");
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        if (stdin is not null)
        {
            await p.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            p.StandardInput.Close();
        }
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { p.Kill(entireProcessTree: true); throw; }
        return new(p.ExitCode, await outTask, await errTask);
    }

    public static ProcResult Run(string file, IEnumerable<string> args, string workdir) =>
        RunAsync(file, args, workdir).GetAwaiter().GetResult();
}
