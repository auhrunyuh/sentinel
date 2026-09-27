using System.Text;
using System.Text.RegularExpressions;

namespace Sentinel.Core;

public sealed record AddedLine(string File, int Line, string Text);

public sealed record DiffLine(string File, char Sign, int Line, string Text);

public static partial class Diff
{
    // git's well-known empty tree: lets us diff a repo that has no commits yet.
    const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex Hunk();

    [GeneratedRegex(@"(^|/)(tests?|[^/]+\.tests)/", RegexOptions.IgnoreCase)]
    private static partial Regex TestDirRx();

    [GeneratedRegex(@"Tests?\.cs$")]
    private static partial Regex TestFileRx();

    /// <summary>Dir segment tests?/ or *.Tests/ (ci), or filename ...Tests?.cs (capital T).</summary>
    public static bool IsTestFile(string relPath) =>
        TestDirRx().IsMatch(relPath.Replace('\\', '/')) || TestFileRx().IsMatch(Path.GetFileName(relPath));

    /// <summary>Every +/- line of a unified diff, with its line number in the new (for '+') or old (for '-') file.</summary>
    public static IEnumerable<DiffLine> Lines(string diff)
    {
        string? oldFile = null;
        string? newFile = null;
        var oldLine = 0;
        var newLine = 0;
        var inHunk = false;
        foreach (var raw in diff.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("diff --git")) { oldFile = null; newFile = null; inHunk = false; continue; }

            var h = Hunk().Match(l);
            if (h.Success) { oldLine = int.Parse(h.Groups[1].Value); newLine = int.Parse(h.Groups[2].Value); inHunk = true; continue; }

            if (!inHunk)
            {
                if (l.StartsWith("--- ")) oldFile = Strip(l[4..]);
                else if (l.StartsWith("+++ ")) newFile = Strip(l[4..]);
                continue;
            }

            if (l.StartsWith('+'))
            {
                if (newFile is not null) yield return new(newFile, '+', newLine, l[1..]);
                newLine++;
            }
            else if (l.StartsWith('-'))
            {
                // File deleted (+++ /dev/null): removed lines belong to the old file name.
                var file = newFile ?? oldFile;
                if (file is not null) yield return new(file, '-', oldLine, l[1..]);
                oldLine++;
            }
            else if (l.StartsWith(' ')) { oldLine++; newLine++; }
        }

        static string? Strip(string p) => p == "/dev/null" ? null : p.StartsWith("a/") || p.StartsWith("b/") ? p[2..] : p;
    }

    /// <summary>Lines added by a unified diff, with their line number in the new file.</summary>
    public static IEnumerable<AddedLine> AddedLines(string diff) =>
        Lines(diff).Where(l => l.Sign == '+').Select(l => new AddedLine(l.File, l.Line, l.Text));

    public static string Root(string from)
    {
        var r = Proc.Run("git", ["rev-parse", "--show-toplevel"], from);
        return r.Code == 0 ? r.Stdout.Trim() : from;
    }

    /// <summary>Staged diff, diff against a ref, or (default) everything uncommitted incl. untracked files.</summary>
    public static string Working(string root, string? baseRef = null, bool staged = false, string? path = null)
    {
        string Git(params string[] a)
        {
            var r = Proc.Run("git", a, root);
            // `diff --no-index` exits 1 when files differ; that's success for us.
            if (r.Code > 1) throw new InvalidOperationException($"git {string.Join(' ', a)}: {r.Stderr.Trim()}");
            return r.Stdout;
        }

        string[] scope = path is null ? [] : ["--", path];
        if (staged) return Git(["diff", "--staged", "-U5", .. scope]);

        var head = Proc.Run("git", ["rev-parse", "--verify", "-q", "HEAD"], root).Code == 0 ? "HEAD" : EmptyTree;
        var sb = new StringBuilder(Git(["diff", baseRef ?? head, "-U5", .. scope]));
        var untracked = Git(["ls-files", "--others", "--exclude-standard", .. scope])
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var f in untracked) sb.Append(Git("diff", "--no-index", "-U5", "--", "/dev/null", f));
        return sb.ToString();
    }
}
