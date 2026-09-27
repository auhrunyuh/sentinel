# Plan Mode Architecture Rules (Non-Obvious Only)

## Invariants That Must Not Break

- `SecretScanner` is always-on and free (regex, no LLM) — it runs before any LLM reviewer and on every PostToolUse write; do not route around it
- The gate is **deterministic C#**, not an LLM check — gate pass is the source of truth for "fix is correct"; LLM reviewer confidence is advisory
- `Policy.Evaluate` must stay **pure** (no async, no I/O beyond the path existence check) — it is called synchronously inside a hook with a 10 s timeout
- `Audit.Append` is **append-only and hash-chained** — there is no delete or update path by design; new events must be appended, never rewritten

## Hidden Coupling

- `Gate.IsTestFile` (via `Diff.IsTestFile`) and `Policy` both use the same pattern to identify test files (`*Tests*.cs` or `tests?/` / `*.Tests/` dir); if you change one, change both
- `Fix.RunAsync` calls `Gate.RunAsync` with `baseSha` as the base ref — `baseSha` is captured at the start of the fix run; the working tree must be clean at that point or the gate will use the wrong base
- `Review.RunOne` caches `BobResult` by `mode + "\n" + prompt` hash — cache is keyed on the full prompt, so any change to `Review.Prompt()` invalidates all existing cache entries

## Structural Constraints

- New commands added to `Program.cs` must follow the `args switch` dispatch pattern — no argument-parsing library is used; `Opt(string k)` is a local closure
- `Json.Options` (in `Findings.cs`) is the single shared serialiser config — do not create new `JsonSerializerOptions` instances in Core; share this one
- The `.slnx` solution format (not `.sln`) is used throughout — CI references `Sentinel.slnx`; adding new projects requires updating this file
