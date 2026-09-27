using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sentinel.Core;

public sealed record PolicyDecision(bool Allow, string? Rule = null, string? Reason = null);

/// <summary>PreToolUse rules for an agent working in <c>root</c>. First matching deny wins.</summary>
public static partial class Policy
{
    const RegexOptions I = RegexOptions.IgnoreCase;
    static readonly string[] ProtectedDirs = [".git", ".bob", ".githooks", ".sentinel"];

    static readonly (string Rule, Regex Rx, string Reason)[] CommandRules =
    [
        ("no-network", new(@"\b(curl|wget|nc|ssh|scp)\b|invoke-webrequest", I), "Network access from agent commands is blocked."),
        ("no-env-dump", new(@"\bprintenv\b|(^|[;&|(]\s*)(env|set)\s*($|[;&|)])|BOB_API_KEY|/environ\b", I), "Dumping the environment can leak secrets."),
        ("no-git-rewrite", new(@"(^|[;&|])\s*git\s+(?:(?:push|rebase|config)\b|reset\s+--hard\b|commit\b[^;&|]*--amend\b)", I), "Git history/remote/config changes are reserved for humans."),
        ("no-hook-bypass", new(@"(^|[;&|])\s*git\s+(?:-c\s+core\.hookspath\s*=|(?:commit|push|merge)\b[^;&|]*(?:--no-verify\b|\s-[a-z]*n[a-z]*(?=[\s;&|]|$)))", I), "Bypassing git hooks is not allowed."),
        ("no-destructive-rm", new(@"\brm\s+(-[\w-]*\s+)*(-\w*(rf|fr)\w*|--recursive\s+--force|--force\s+--recursive|-r\s+-f|-f\s+-r)\s+(-[\w-]*\s+)*[""']?(/|~|\.|\.\.)/?\*?[""']?(\s|$|[;&|])"),
            "Recursive force-delete of root, home or the workspace is blocked."),
        ("no-new-deps", new(@"\bdotnet\s+add\b.*\bpackage\b|\bnpm\s+(i|install|add)\b|\b(yarn|pnpm)\s+add\b|\bpip3?\s+install\b", I), "Adding dependencies needs human review."),
        ("protected-path", new(@"(^|[\s'""=/:])\.(git|bob|githooks|sentinel)(/|\s|$|['"";&|])", I), "Command touches a protected directory."),
        // ponytail: shell parsing is heuristic — real boundary is a sandbox; these rules catch the common agent patterns.
        ("no-shell-write",
            new(@"
                # output redirection to a file (allow 2>&1 and >/dev/null)
                (?<![2-9&])>{1,2}(?!\s*/dev/null)(?!\s*&)         # > or >> not preceded by fd digit/& and not to /dev/null or &N
                |
                # tee writing to a file
                \btee\b(?!.*--help)
                |
                # in-place sed/perl
                \bsed\s+(-[a-z]*i[a-z]*|--in-place)\b
                |
                \bperl\s+(-[a-z]*i[a-z]*|--in-place)\b
                |
                # dd writing to a file/device
                \bdd\b.*\bof=
                |
                # file-copy/move/install
                \b(cp|mv|install)\s
            ", RegexOptions.IgnorePatternWhitespace | I),
            "Writing files via shell redirection or copy commands is not allowed; use edit tools."),
    ];

    static readonly Regex WriteTool = new("write|edit|diff|insert|replace", I);

    public static JsonElement Input(JsonElement p) =>
        p.ValueKind == JsonValueKind.Object && (p.TryGetProperty("input", out var i) || p.TryGetProperty("tool_input", out i)) ? i : default;

    public static string ToolName(JsonElement p) =>
        new[] { "tool_name", "tool", "name" }.Select(k => Str(p, k)).FirstOrDefault(s => s is not null) ?? "";

    public static PolicyDecision Evaluate(JsonElement payload, string root)
    {
        var input = Input(payload);
        var tool = ToolName(payload);
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

        if (Str(input, "command") is { } cmd)
        {
            foreach (var (rule, rx, reason) in CommandRules)
                if (rx.IsMatch(cmd)) return new(false, rule, reason);

            // outside-workspace-exec: deny commands that reference /tmp, /private/tmp or /var/folders
            // unless every matched path is a prefix of (or inside) the workspace root.
            // ponytail: shell parsing is heuristic — real boundary is a sandbox.
            if (OutsideTempPaths(cmd, rootFull) is { } oteReason)
                return new(false, "outside-workspace-exec", oteReason);
        }

        var path = Str(input, "path") ?? Str(input, "file_path");
        if (path is null) return new(true);

        if (path.StartsWith('~')) return new(false, "outside-workspace", $"{path} is outside the workspace.");
        var full = Path.GetFullPath(Path.Combine(rootFull, path));
        // ponytail: lexical check, symlinks inside the repo can still escape; resolve LinkTarget if that matters.
        if (full != rootFull && !full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return new(false, "outside-workspace", $"{path} is outside the workspace.");

        var rel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
        if (rel.Split('/').Any(s => ProtectedDirs.Contains(s, StringComparer.OrdinalIgnoreCase))
            || SecretScanner.SensitiveFile.IsMatch(rel))
            return new(false, "protected-path", $"{rel} is protected.");

        var content = Str(input, "content");
        if (content is null && !WriteTool.IsMatch(tool)) return new(true);

        if (File.Exists(full) && Diff.IsTestFile(rel))
            return new(false, "no-test-tamper", $"{rel} is an existing test; agents may add tests, not change them.");

        // Edit tools carry the new text under varying keys (content, diff, new_str...): scan every string field.
        var text = content ?? string.Join('\n', input.ValueKind == JsonValueKind.Object
            ? input.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Name != "path").Select(p => p.Value.GetString())
            : []);
        if (SecretScanner.ScanText(rel, text).FirstOrDefault(f => f.Severity >= Severity.High) is { } hit)
            return new(false, "no-secret-write", $"Write to {rel} contains a possible secret ({hit.Rule}, line {hit.Line}).");

        return new(true);
    }

    // Returns a denial reason if cmd references a temp path that is NOT inside rootFull; null if clean.
    static string? OutsideTempPaths(string cmd, string rootFull)
    {
        // Extract all /tmp/…, /private/tmp/…, /var/folders/… tokens from the command.
        var matches = TempPathTokenRx().Matches(cmd);
        foreach (Match m in matches)
        {
            var p = m.Value.TrimEnd('/', '\\');
            // Allow if the token is a prefix of the workspace root or the root is inside the token path.
            if (rootFull.StartsWith(p, StringComparison.Ordinal) ||
                rootFull.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                p == rootFull ||
                p.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            return $"Command references {p} which is outside the workspace.";
        }
        return null;
    }

    // Matches /tmp/, /private/tmp/, /var/folders/ path tokens (up to whitespace or shell metachar).
    // ponytail: shell parsing is heuristic — real boundary is a sandbox.
    [GeneratedRegex(@"(/tmp|/private/tmp|/var/folders)/[^\s'"";&|]*", RegexOptions.IgnoreCase)]
    private static partial Regex TempPathTokenRx();

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Append-only, hash-chained JSONL log at root/.sentinel/audit.jsonl.</summary>
public static class Audit
{
    const string Genesis = "GENESIS";
    static readonly JsonSerializerOptions Compact = new(Json.Options) { WriteIndented = false };

    public static string PathFor(string root) => Path.Combine(root, ".sentinel", "audit.jsonl");

    // Largest audit entry is ~1 KB; read the last 4 KB to find the previous hash without scanning the whole file.
    const int TailBytes = 4096;

    public static void Append(string root, object evt)
    {
        var file = PathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var eventJson = JsonSerializer.Serialize(evt, Compact);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                string? prev = null;
                if (fs.Length > 0)
                {
                    var readFrom = Math.Max(0, fs.Length - TailBytes);
                    fs.Seek(readFrom, SeekOrigin.Begin);
                    var tail = new StreamReader(fs, leaveOpen: true).ReadToEnd();
                    var last = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    if (last is not null)
                        prev = JsonDocument.Parse(last).RootElement.GetProperty("hash").GetString();
                }
                prev ??= Genesis;
                var ts = JsonSerializer.Serialize(DateTimeOffset.UtcNow);
                var line = $"{{\"ts\":{ts},\"event\":{eventJson},\"prev\":\"{prev}\",\"hash\":\"{Hash(prev, ts, eventJson)}\"}}\n";
                fs.Seek(0, SeekOrigin.End);
                fs.Write(Encoding.UTF8.GetBytes(line));
                return;
            }
            catch (IOException) when (attempt < 40) { Thread.Sleep(25); }
        }
    }

    /// <returns>Ok, and the 1-based line number of the first broken link (0 when Ok).</returns>
    public static (bool Ok, int BadLine) Verify(string root)
    {
        var file = PathFor(root);
        if (!File.Exists(file)) return (true, 0);
        var prev = Genesis;
        var n = 0;
        foreach (var line in File.ReadLines(file))
        {
            n++;
            if (line.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                var hash = e.GetProperty("hash").GetString();
                var ts = e.GetProperty("ts").GetRawText();
                if (e.GetProperty("prev").GetString() != prev || hash != Hash(prev, ts, e.GetProperty("event").GetRawText())) return (false, n);
                prev = hash!;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return (false, n); }
        }
        return (true, 0);
    }

    static string Hash(string prev, string ts, string eventJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prev + ts + eventJson)));
}
