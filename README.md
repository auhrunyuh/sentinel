# Sentinel

Safe, autonomous incident remediation built on IBM Bob 2.0.

## The problem

When CI goes red or a prod alert fires, someone has to read the stack trace, find the
bug, write a fix, and hope it's right. That's slow, and handing the job to an AI agent
outright is risky: a headless agent given free rein can read secrets, hit the network,
or edit the failing test to fake a green build.

Sentinel closes that gap. An agent proposes the fix, but nothing ships on the agent's
word alone:

- **Fail-to-pass gate** — the agent must write a new test that reproduces the bug,
  watch it fail on the base commit, then fix `src/` until it (and the full suite) pass.
  No new failing test in the diff, no PR.
- **Agent firewall** — policy hooks block the agent from touching existing tests,
  secrets, protected paths, or the network, regardless of what the agent decides to try.
- **Diff guards** — every write is scanned for leaked secrets before it lands, on top
  of the gate.

## Architecture

```
CI failure ──▶ GitHub Action ──▶ bob run --mode sentinel-triage ──▶ PR comment (root cause)
                                          │
                                          ▼
                                  sentinel fix (CLI)
                                          │
                         ┌────────────────┼────────────────┐
                         ▼                ▼                ▼
                  bob run --mode     fail-to-pass      policy hooks
                  sentinel-fixer  ──▶   gate      ──▶  (agent firewall)
                  (repro test,       (deterministic       │
                   then fix)          pass/fail check)    ▼
                                                     allow / deny + audit
                         │
                         ▼
                  PR opened (never auto-merged)
```

## Quickstart

```bash
# build everything
dotnet build -c Release

# route local hooks through the repo's policy checks
git config core.hooksPath .githooks

# ask Bob to review your working diff (LLM diff reviewer, opt-in — costs Bobcoins)
sh scripts/sentinel review

# check whether a fix passes the fail-to-pass gate against a base ref
sentinel gate --base <ref> --project samples/OrderService

# run the fixer agent against a specific alert
sentinel fix --alert samples/OrderService/alerts/01.json --project samples/OrderService

# or: spin up a standalone demo repo with bug NN injected (agent never sees bugs/ or hidden-tests/)
sh scripts/demo-setup.sh 01
# prints the exact `sentinel fix` command to run from inside the demo dir
```

`sentinel` is the built CLI at `src/Sentinel.Cli/bin/Release/net10.0/sentinel.dll`;
`scripts/sentinel` is a thin wrapper that runs it via `dotnet` (see that script for the
`SENTINEL_POLICY=audit` escape hatch when the DLL isn't built yet).

## Policy rules

| Rule | Blocks |
|---|---|
| `no-test-tamper` | writing/editing existing `*Tests*.cs` files (new test files are fine — that's how the gate works) |
| `src-only` | writes outside `src/`, or outside new files under `tests/` |
| `protected-path` | `.git`, `.bob`, `.githooks`, `.sentinel`, `.env*`, key/cert files, `secrets.json`, `appsettings.Production.json` |
| `no-network` | `curl`, `wget`, `Invoke-WebRequest`, `dotnet add package` |
| `no-git-rewrite` | `git push`/`rebase`/`config`, `git reset --hard`, `git commit --amend` (matched as a real subcommand, not a substring) |
| `budget` | tool calls beyond the run's configured limit |

Every decision is written to an append-only, hash-chained audit log. Hooks are a
policy layer, not a security boundary — the real boundary is the sandbox and the
egress allowlist. See `sentinel.md` section 4.5 for the full threat model.

## Bob usage

- **Modes**: `sentinel-triage` (read-only, used by the GitHub Action) and
  `sentinel-fixer` (edit access limited to `src/` and new test files under `tests/`),
  defined in `.bob/custom_modes.yaml`.
- **Hooks**: `.bob/settings.json` wires PreToolUse/PostToolUse/Stop to `sentinel hook`,
  which enforces the policy rules above.
- **Rules & skills**: `.bob/rules-sentinel-*` and `.bob/skills/` encode the
  repro-test-first protocol so the fixer can't skip straight to editing `src/`.
- **Evidence**: `bob_sessions/` holds screenshots of real Bob task sessions (build,
  plan, fixer runs, reviews) used while building and running Sentinel.

## Eval results

Bug corpus: 10 seeded bugs in `samples/OrderService`.

| Metric | Value |
|---|---|
| pass@1 | TBD |
| pass@3 | TBD |
| false-fix rate | TBD |
| median time-to-PR | TBD |
| cost per fix | TBD |
| policy denials caught | TBD |

Full methodology in `sentinel.md` section 4.9; numbers land in `docs/eval-report.md`
once the eval run completes.

## Built with

Sentinel was built using **IBM Bob IDE** and **Bob Shell** (`bob run`) — plan mode for
architecture, agent mode for scaffolding, subagents for parallel work, and Bob itself
runs the fixer/triage agents that Sentinel supervises. **Claude Code** was also used
for parts of the implementation. Full transparency, no hidden tooling.

## License

MIT — see [LICENSE](LICENSE).
