using System.Globalization;
using System.Text.Json;

namespace Sentinel.Core;

public sealed record BobResult(bool Ok, string LastMessage, double Cost, long Tokens, long DurationMs, string? Error, string? TaskId = null);

/// <summary>Thin wrapper over the IBM Bob CLI in headless mode.</summary>
public static class Bob
{
    public static async Task<BobResult> RunAsync(string root, string mode, string prompt, double maxCost, int maxTurns,
        IDictionary<string, string>? env = null, string? resumeTaskId = null, CancellationToken ct = default)
    {
        var exe = Environment.GetEnvironmentVariable("SENTINEL_BOB") ?? "bob";
        string[] args = ["run", "--mode", mode, "--format", "json",
            "--max-cost", maxCost.ToString(CultureInfo.InvariantCulture),
            "--max-turns", maxTurns.ToString(CultureInfo.InvariantCulture), "--accept-license", "--trust",
            .. resumeTaskId is null ? Array.Empty<string>() : ["--resume", resumeTaskId]];
        try
        {
            var r = await Proc.RunAsync(exe, args, root, prompt, env, ct);
            var res = ParseResult(r.Stdout);
            if (res.Ok && r.Code == 0) return res;
            var diag = $"exit {r.Code}\nstderr:\n{Tail(r.Stderr)}\nstdout:\n{Tail(r.Stdout)}";
            return res with { Error = res.Error is null ? diag : $"{res.Error}\n{diag}" };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new(false, "", 0, 0, 0, e.Message);
        }
    }

    /// <summary>Last parseable JSON line with type=result wins; anything before it is noise.</summary>
    public static BobResult ParseResult(string stdout)
    {
        foreach (var line in stdout.Split('\n').Reverse())
        {
            var t = line.Trim();
            if (!t.StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(t);
                var e = doc.RootElement;
                if (Str(e, "type") != "result") continue;
                var ok = Str(e, "status") == "success";
                var stats = e.TryGetProperty("stats", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
                return new(ok, Str(e, "last_message") ?? "",
                    Num(stats, "session_costs"), (long)Num(stats, "total_tokens"), (long)Num(stats, "duration_ms"),
                    ok ? null : Str(e, "error") ?? Str(e, "last_message") ?? "bob reported error",
                    Str(stats, "task_id"));
            }
            catch (JsonException) { }
        }
        return new(false, "", 0, 0, 0, "no result line in bob output");
    }

    /// <summary>Last ~20 lines, trimmed, capped at 1500 chars — enough to diagnose a crash without dumping the world.</summary>
    static string Tail(string s)
    {
        var t = string.Join('\n', s.Split('\n').TakeLast(20)).Trim();
        return t.Length > 1500 ? t[^1500..] : t;
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : v.ToString()
            : null;

    static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;
}
