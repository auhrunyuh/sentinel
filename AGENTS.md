# AGENTS.md

This file provides guidance to agents when working with code in this repository.

## Stack

- **Language/Runtime**: C# / .NET 10 (`net10.0`), `ImplicitUsings` + `Nullable` enabled in all projects
- **Solution**: `Sentinel.slnx` (new `.slnx` format, not `.sln`)
- **CLI binary name**: `sentinel` (not `Sentinel.Cli`) — set via `<AssemblyName>sentinel</AssemblyName>`
- **Test framework**: xUnit v2; `xunit` global using is injected by the `.csproj` (`<Using Include="Xunit" />`), so test files do **not** need `using Xunit;`

## Commands

```bash
# Build
dotnet build Sentinel.slnx -c Release

# Run all tests
dotnet test Sentinel.slnx -c Release --no-build

# Run a single test by name (substring match)
dotnet test Sentinel.slnx --filter "FullyQualifiedName~.TestMethodName"

# Run tests in the test project directly (faster, skips solution traversal)
dotnet test tests/Sentinel.Tests -c Release

# Build/test the sample OrderService (separate solution)
dotnet build samples/OrderService/OrderService.slnx -c Release
dotnet test samples/OrderService/OrderService.slnx -c Release --no-build
```

## Policy / Protected Paths

The running `sentinel hook pre-tool` enforces policies that **block agents from**:

- Reading/writing `.bob/`, `.git/`, `.githooks/`, `.sentinel/` (protected dirs)
- Modifying any existing `*Tests*.cs` file (new test files are allowed)
- Writing files containing secrets (AWS keys, JWTs, hardcoded passwords, etc.)
- Running `curl`, `wget`, `nc`, `ssh`, `dotnet add package`, or git history rewrites
- Accessing paths outside the workspace (including `~`)

`SENTINEL_POLICY=audit` → log-only (non-blocking). Default is enforce.

## Architecture

- **`Sentinel.Core`**: pure logic library — `Bob`, `Diff`, `Fix`, `Gate`, `Policy`, `Review`, `SecretScanner`, `Audit`
- **`Sentinel.Cli/Program.cs`**: top-level statements dispatch subcommands: `review`, `hook pre-tool|post-tool|stop`, `watch`, `audit verify`, `gate`, `fix`
- **`Bob.cs`**: wraps `bob run --mode <mode> --format json` via `Proc`; reads result by scanning stdout lines in reverse for the last `{"type":"result",...}` JSON line — earlier lines are noise
- **`Gate.cs`**: fail-to-pass gate — uses `git worktree` to run new tests on the base commit (must fail) then on HEAD (must pass); test files are copied into the worktree before running
- **`Policy.cs`** + **`Audit.cs`**: `Policy.Evaluate()` is the hook engine; `Audit.Append()` writes to `.sentinel/audit.jsonl` (hash-chained, SHA256 of `prev_hash + ts + eventJson`)
- **`Review.cs`**: LLM reviewers (`sentinel-sec-review`, `sentinel-perf-review`) run via Bob with `--max-turns 1`, output must be `<verdict>{...}</verdict>`; results cached by SHA256 of `mode + prompt` in `.sentinel/cache/`
- **`SecretScanner`**: always-on regex scanner, no LLM; runs before any LLM reviewer

## Code Style

- `sealed record` for all DTOs/value objects (no mutable classes for data)
- `static partial class` with `[GeneratedRegex]` for all regex (source-generated, not `new Regex(...)` at class level)
- `System.Text.Json` throughout (not Newtonsoft except as a test runner transitive dep); shared `Json.Options` in `Findings.cs`
- `string?` null returns preferred over throwing when a value may be absent
- Internal helpers named `Str()` / `Num()` for safe JSON element property access (see `Bob.cs`, `Policy.cs`)
- Comments annotated `ponytail:` document known limitations/shortcuts intentionally left for future improvement

## Testing Conventions

- Test class names end in `Tests` (file pattern `*Tests*.cs` is how `Gate.IsTestFile` / policy identifies test files)
- New test files must be *new* files — agents must never edit existing `*Tests*.cs` files (enforced by policy hook and the gate)
- `SENTINEL_BOB` env var overrides the `bob` executable path (used in tests to inject a fake shell script)
- Integration tests that need a real git repo create a temp dir via `Directory.CreateTempSubdirectory()`

## Key Environment Variables

| Variable | Purpose |
|---|---|
| `SENTINEL_BOB` | Override `bob` executable path (default: `bob`) |
| `SENTINEL_POLICY` | `enforce` (default) or `audit` (log-only) |
| `SENTINEL_REVIEWER` | Set to `"1"` inside reviewer Bob sessions to prevent recursive stop-hook reviews |
| `BOB_API_KEY` | IBM Bob API key with Inference scope |
