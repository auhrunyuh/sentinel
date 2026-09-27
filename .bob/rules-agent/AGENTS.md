# Agent Coding Rules (Non-Obvious Only)

## C# Patterns

- All data types use `sealed record` — never `class` with mutable setters for DTOs
- All regex must use `[GeneratedRegex]` on `static partial` classes, not inline `new Regex()`
- JSON parsing uses shared `Json.Options` from `Sentinel.Core.Json` (camelCase enums, indented, Web defaults)
- Safe JSON field access uses local `Str()`/`Num()` helpers (see `Bob.cs`, `Policy.cs`) — never `.GetString()` without null-check

## Tests

- **New test files only** — never modify an existing `*Tests*.cs` file; the policy hook exits 2 (denies) and the gate independently re-checks this
- Test classes need no `using Xunit;` — it is injected globally by `<Using Include="Xunit" />` in the `.csproj`
- Run a single test: `dotnet test Sentinel.slnx --filter "FullyQualifiedName~.MethodName"`
- `SENTINEL_BOB` env var overrides the `bob` executable; used in tests to inject fake shell scripts

## Gate / Fix Loop

- `Gate.RunAsync` uses `git worktree` at a temp path — new test files are **copied** into the worktree before the base run (not committed); cleanup is in `finally`
- `Fix.RunAsync` requires a **clean working tree** — throws `InvalidOperationException` if dirty
- `Fix.ReportFile` (`SENTINEL_REPORT.md`) is deleted at the start of each run because it is gitignored and a stale file would survive the clean-tree check
- Bob result parsing scans stdout lines **in reverse** for the last `{"type":"result",...}` JSON line; all preceding output is noise

## Policy / Secret Scanner

- Writing fake secret fixtures in tests: build the value at runtime (e.g. `"AKIA" + "ABCDEFGHIJKLMNOP"`) — never embed the literal; do not add scanner-suppression comments (an agent could use them to hide real secrets)
- `comments` annotated `ponytail:` document intentional shortcuts/known limitations — preserve them when editing nearby code
- Protected dirs `.bob/`, `.git/`, `.githooks/`, `.sentinel/` are blocked by the live pre-tool hook; writes to those paths from agent tools will be denied at exit 2
