# Ask Mode Context (Non-Obvious Only)

## Code Organisation

- `src/Sentinel.Core/` contains pure logic only — no CLI, no ASP.NET; it is the single engine used by both the CLI and (planned) API
- `src/Sentinel.Cli/Program.cs` is top-level statements only — all logic lives in Core; the binary is named `sentinel` (not `Sentinel.Cli`)
- `samples/OrderService/` is the "victim" app used for eval — it is intentionally buggy; do not treat it as reference code
- `.sentinel/` (gitignored) holds runtime artefacts: `audit.jsonl`, `cache/`, `review.json`, `gate.json`, `fix.json`

## Bob Integration

- `Bob.cs` wraps `bob run --mode <mode> --format json`; result is parsed by scanning stdout **in reverse** for the last `{"type":"result",...}` line — earlier lines are noise
- `SENTINEL_REVIEWER=1` env var is set inside reviewer Bob sessions to prevent the Stop hook from triggering a recursive review
- LLM reviewer verdicts must arrive as `<verdict>{...}</verdict>` XML tags; unparseable output becomes `clean@0` plus an error — never a crash
- Review verdicts are cached by SHA256(`mode + "\n" + prompt`) under `.sentinel/cache/` — identical diffs are free on retry

## Architecture Constraints

- Only `security` reviewer verdicts (`leak` or `vuln`) at confidence ≥ threshold route to the auto-fixer; `performance` is advisory only
- The `Audit` log is hash-chained (`SHA256(prev_hash + ts + eventJson)`) — backdating an entry breaks the chain because `ts` is inside the hash input
- `Policy.Evaluate` is pure (no I/O) — all decisions come from the payload JSON and the local filesystem path check only
