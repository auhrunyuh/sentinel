using System.Text.RegularExpressions;

namespace Sentinel.Core;

/// <summary>
/// Deterministic, free, instant. Runs on every save before any LLM reviewer.
/// ponytail: regex rules, no entropy analysis. Swap in gitleaks rules if false negatives show up.
/// </summary>
public static class SecretScanner
{
    static readonly (string Rule, Severity Severity, Regex Rx)[] LineRules =
    [
        ("aws-access-key", Severity.Critical, new(@"\bAKIA[0-9A-Z]{16}\b")),
        ("private-key", Severity.Critical, new(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----")),
        ("github-token", Severity.Critical, new(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b")),
        ("jwt", Severity.High, new(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")),
        ("connection-string-password", Severity.High, new(@"(?i)[;""']\s*(?:password|pwd)\s*=\s*([^;""'\s]{3,})")),
        ("hardcoded-secret", Severity.High,
            new(@"(?i)[\w.]*(?:password|passwd|secret|api[_-]?key|access[_-]?token|client[_-]?secret)[\w.]*""?\s*[:=]\s*[@$]?""([^""]{6,})""")),
        ("sensitive-data-logged", Severity.Medium,
            new(@"(?i)\b(?:log\w*|console\.(?:write\w*|log)|_logger\.\w+)\s*\(.*\b(?:password|ssn|cardnumber|cvv|token|secret)\b")),
    ];

    static readonly Regex Placeholder = new(@"(?i)changeme|example|placeholder|your[-_]|xxx|<[^>]+>|\$\{|\{\{|dummy|redacted");
    internal static readonly Regex SensitiveFile = new(@"(?i)(^|/)(\.env(\.[\w-]+)?|[^/]+\.(pem|pfx|p12|key)|id_rsa[^/]*|secrets\.json|appsettings\.Production\.json)$");

    public static List<Finding> Scan(IEnumerable<AddedLine> lines)
    {
        var findings = new List<Finding>();
        var seenFiles = new HashSet<string>();
        foreach (var (file, line, text) in lines)
        {
            if (seenFiles.Add(file) && SensitiveFile.IsMatch(file))
                findings.Add(new("scanner", "sensitive-file", Severity.High, file, line,
                    "Secret-bearing file type added to the change set.", "Remove it and add the pattern to .gitignore."));

            foreach (var (rule, sev, rx) in LineRules)
            {
                var m = rx.Match(text);
                if (!m.Success) continue;
                var value = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                if (Placeholder.IsMatch(value)) continue;
                findings.Add(new("scanner", rule, sev, file, line, $"Possible {rule.Replace('-', ' ')}.",
                    "Move it to user-secrets / environment / a vault and rotate it."));
            }
        }
        return findings;
    }

    public static List<Finding> ScanText(string file, string text) =>
        Scan(text.Split('\n').Select((t, i) => new AddedLine(file, i + 1, t)));
}
