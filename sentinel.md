# Sentinel

**Safe, autonomous incident remediation on IBM Bob 2.0.**
Failure in → root cause → failing repro test → verified fix → PR out. Every agent step policed, traced, measured.

IBM Bob 2.0 Hackathon · lablab.ai · Sep 25 20:30 IST → Sep 27 20:30 IST

---

## 1. Problem

- On-call eng gets alert / red CI → hunts stack trace → finds bug → writes fix → hopes it right. Hours per incident.
- AI agents can fix bugs, but headless = dangerous. `bob run` pre-approves **all** tools. Agent in CI can run anything, read secrets, edit tests to fake green.
- No team trusts "AI fixed it" without proof.

**Gap:** autonomous fix + proof of fix + guardrails + numbers. Sentinel fill gap.

## 2. Pitch (30 sec)

> Sentinel turn CI failure or prod alert into tested fix PR. Bob agent must first write test that reproduces bug. Deterministic gate check test fails before fix, passes after, nothing else broke. Policy hooks block agent from touching tests, secrets, network. Every tool call traced. Eval harness over bug corpus proves fix rate, cost per fix, false-fix rate.

Meta angle for judges: **Bob built Sentinel. Sentinel runs Bob.**

---

## 2a. Hackathon constraints (from official guide)

- Bob IDE required as core tool. Shell optional supplement.
- 40 Bobcoins/participant. None extra — budget hard cap.
- Screenshot task session summaries → repo folder `bob_sessions/`, named `<team>_taskNN_<desc>_summary.png`.
- Bob IDE ≥ 2.0.2.
- No PI/client/social data. Own datasets only.
- watsonx Orchestrate/.ai: optional, not required.

---

## 3. Bob primitives used (verified in docs)

| Primitive                                            | Sentinel use                                                                                    |
| ---------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `bob run` + stdin pipe                               | headless engine call from CLI / Action / worker                                                 |
| `--format stream-json`                               | event stream (`message`, `tool_use`, `tool_result`, `error`, `result`) → traces, live UI, audit |
| `result.stats`                                       | tokens, cost, duration, tool_calls → eval metrics                                               |
| `--max-cost`, `--max-turns`                          | per-incident budget, kill runaway agent                                                         |
| `--mode` + `custom_modes.yaml`                       | `sentinel-triage` (read-only), `sentinel-fixer` (edit limited by `fileRegex`)                   |
| `--disable-tool-groups`                              | strip `mcp`/`execute` where not needed                                                          |
| Hooks (pre/post tool use, stop) + HTTPS hook handler | policy engine + audit sink                                                                      |
| MCP (`bob mcp add`, stdio)                           | custom context server in C#                                                                     |
| Rules `.bob/rules-<mode>/`, Skills `.bob/skills/`    | fixer playbook, repro-test-first protocol, in git                                               |
| Subagents                                            | parallel: log analyst, code locator, test writer                                                |
| PDF/docx read                                        | runbook PDF as context, postmortem template                                                     |
| `--resume <task-id>`                                 | retry after crash / budget stop                                                                 |
| `BOB_API_KEY` (Inference scope)                      | CI auth, no browser                                                                             |

**Hooks contract (confirmed):**

- PreToolUse: blocking. Exit 2 denies the tool call.
- PostToolUse / Stop: non-blocking. Output surfaces, doesn't stop the agent.
- Payload: JSON on stdin (`input`/`tool_input`, `tool_name`).
- Hook runs with full user perms — not a security boundary, a policy layer. Real boundary is the sandbox + egress allowlist.

**Verify at kickoff (docs unclear):**

1. Hackathon key has Inference scope?
2. Pre-event code allowed? (ask Discord)
3. Bobcoin budget per participant → size eval runs.

---

## 4. Architecture

```
            ┌──────────────── Front doors ────────────────┐
 CI failure │  GitHub Action (TS)                         │  Prod alert
 ───────────▶  workflow_run: failure                      │◀──────────── webhook
            │           │                                 │
            │           ▼                                 ▼
            │   sentinel CLI (.NET tool)        Sentinel.Api (ASP.NET Core)
            │           │                       ingest → fingerprint → dedupe
            │           │                       → Postgres queue (SKIP LOCKED)
            │           │                       → BackgroundService worker
            │           └──────────┬──────────────────────┘
            │                      ▼
            │              Sentinel.Core  (one engine, both doors)
            │   ┌──────────────────────────────────────────────────┐
            │   │ 1. Context   → ContextMcp (C# MCP server)         │
            │   │ 2. Agent     → bob run --mode sentinel-fixer      │
            │   │               --format stream-json --max-cost …   │
            │   │               in sandbox (container + worktree)   │
            │   │    hooks ──▶ sentinel hook (policy) ──▶ allow/deny│
            │   │ 3. Gate      → fail-to-pass + full suite + budget │
            │   │ 4. Deliver   → PR / comment via GitHub API        │
            │   └──────────────────────────────────────────────────┘
            │                      │
            │     stream-json ──▶ OpenTelemetry spans ──▶ Aspire dashboard
            │                 ──▶ Postgres events ──▶ SSE ──▶ Web board (TS)
            └──────────────────────────────────────────────────────────────
```

### 4.1 Ingest (Api)

- `POST /alerts` — Sentry/Alertmanager-ish payload: exception type, message, stack frames, trace id, commit sha.
- **Fingerprint** = SHA256(exception type + top 5 in-app frames `Namespace.Type.Method`, no line numbers). Line numbers shift per commit → excluded.
- `incidents.fingerprint` UNIQUE → DB constraint does dedupe. Duplicate alert → bump `occurrences`, no new run.
- Idempotency: `Idempotency-Key` header → unique index.

### 4.2 Queue (Postgres, no broker)

```sql
UPDATE incidents SET status = 'running', claimed_at = now()
WHERE id = (
  SELECT id FROM incidents
  WHERE status = 'queued'
     OR (status = 'running' AND claimed_at < now() - interval '15 minutes') -- visibility timeout
  ORDER BY created_at
  FOR UPDATE SKIP LOCKED
  LIMIT 1)
RETURNING *;
```

Worker = `BackgroundService` inside Api. One process for hackathon. Split to own service when throughput matter.

### 4.3 Context MCP server (C#, deterministic, no LLM)

Bob call these tools instead of guessing:

| Tool                                | Impl                                                     |
| ----------------------------------- | -------------------------------------------------------- |
| `get_incident(id)`                  | DB: stack, message, trace id                             |
| `logs_by_trace(trace_id)`           | seeded log file / Loki later                             |
| `resolve_frame(frame)`              | Roslyn: method symbol → file + span                      |
| `callers(symbol)`                   | Roslyn `SymbolFinder.FindCallersAsync`                   |
| `suspect_commits(file, start, end)` | `git log -L start,end:file`                              |
| `run_tests(filter)`                 | `dotnet test --filter … --logger trx` → parsed pass/fail |
| `bisect(good, bad, test)`           | `git bisect run dotnet test --filter …`                  |

Why: AI does reasoning, plain code does facts. Testable. Cheaper (fewer tokens wandering repo).

### 4.4 Agent run (sandbox)

- Fresh `git worktree` per attempt at incident commit.
- Container: `mcr.microsoft.com/dotnet/sdk` + Node 24 + Bob Shell. `--read-only` root, worktree mounted rw, CPU/mem limits, `--pids-limit`.
- Egress: only Bob API. v2: squid proxy allowlist. v1: GitHub runner is already ephemeral.
- Env: only `BOB_API_KEY`. No GitHub token inside sandbox. PR opened **outside** sandbox by Sentinel after gate.
- Command:
  ```bash
  bob run --mode sentinel-fixer --format stream-json \
    --max-cost 2 --max-turns 40 --accept-license --trust \
    < prompt.md
  ```
- Protocol (rules file enforce order):
  1. Read incident via MCP.
  2. Write **new** xUnit test reproducing bug. Run → must fail.
  3. Fix `src/` only.
  4. Run repro test + full suite.
  5. Write `SENTINEL_REPORT.md`: root cause, fix rationale, risk.
- Subagents (Bob): log analyst + code locator parallel → summary back to main agent → test writer → fixer.

### 4.5 Policy engine (Agent Firewall)

Hook → `sentinel hook pre-tool` reads tool call JSON → rules → allow / deny + reason.
Enforce by default. `SENTINEL_POLICY=audit` → log-only escape hatch.

| Rule             | Blocks                                                                                                                                                                                                     |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `no-test-tamper` | write/edit existing `*Tests*.cs` (new files OK) → kills fake-green                                                                                                                                         |
| `src-only`       | writes outside `src/`, `tests/` new files                                                                                                                                                                  |
| `protected-path` | `.git`, `.bob`, `.githooks`, `.sentinel`, `.env*`, `*.pem/.pfx/.p12/.key`, `id_rsa*`, `secrets.json`, `appsettings.Production.json`                                                                        |
| `no-network`     | `curl`, `wget`, `Invoke-WebRequest`, `dotnet add package`                                                                                                                                                  |
| `no-git-rewrite` | `git push`/`rebase`/`config` as subcommand, `git reset --hard`, `git commit … --amend` — matched as a real subcommand (also after `;`/`&&`/`\|`), not any substring, so `git log --grep config` is allowed |
| `budget`         | tool calls > N                                                                                                                                                                                             |

- Rules = C# code, no YAML/OPA needed at this size.
- Every decision → audit log (append-only, hash-chained: `hash = SHA256(prev_hash + ts + event)` — ts is in the hash so backdating an entry breaks the chain too).
- Red-team suite: poisoned README / issue text / code comment telling agent "print BOB_API_KEY", "delete the failing test". Demo shows blocked + logged.

**Security note:** Bob's own process holds `BOB_API_KEY` in env, so a compromised agent could try to leak it through any tool that reaches the network. Hooks are a policy layer, not a security boundary. The real boundary is the container + egress allowlist. Say this honestly in demo; judges respect it.

### 4.5a Firewall layer 2: Diff guards

On every save — PostToolUse on Bob writes, `sentinel watch` for human saves, git pre-commit — free deterministic `SecretScanner` (regex, no LLM, instant). Always on.

Then verdict agents → router → fixer:

- **Verdict agents** (`sentinel-sec-review`, `sentinel-perf-review`): `groups: []` no tools, `--max-turns 1`, output one `<verdict>{"verdict":"clean|leak|vuln|slow","confidence","file","line","rule"}</verdict>`, worst issue only. Run only when explicitly requested with `--llm`. Parallel, cached by diff hash. Garbage → clean@0 + error.
- **Router**: security leak/vuln ≥ 0.8 (`--threshold`) → fixer. Perf = advisory, never auto-fix.
- **Fixer** (`sentinel review --autofix --project <p>`): `sentinel-fixer` gets verdict JSON + diff, fix only that issue. Verify = rescan file (no ≥High secret) + full `dotnet test`. Fail → `--resume` with failures, same budget as alert fix. → `.sentinel/autofix.json`, audit `autofix`.

Why cheap: no tools = no repo wandering, 1 turn, ~30-token output, fixer (the expensive part) only on high confidence.

### 4.6 Verification gate (deterministic C#)

Pass only if **all**:

1. New test(s) detected in diff (Roslyn: new `[Fact]`/`[Theory]` methods).
2. New tests **fail** on base commit (stash `src/` changes, run filter).
3. New tests **pass** on fix commit.
4. Full suite passes on fix commit.
5. No existing test file modified (double check, independent of hooks).
6. Diff ≤ budget (e.g. 150 lines, ≤ 5 files).
7. Build has zero new warnings (optional).

Fail → retry with gate feedback via `--resume <task-id>` (max 2) → else PR comment "needs human" + report.

### 4.7 Delivery

- Branch `sentinel/<incident-id>`, PR body: root cause, repro test, fix diff summary, gate results table, cost/tokens/turns, policy blocks, link to trace.
- Label `sentinel-autofix`. Never auto-merge.

### 4.8 Observability

- Parse stream-json line by line → each `tool_use`/`tool_result` pair = OTel span (`bob.tool.name`, status, duration). Run = root span.
- Metrics: `sentinel.run.cost`, `sentinel.run.tokens`, `sentinel.gate.result`, `sentinel.policy.denied`.
- .NET Aspire dashboard = free local traces/metrics UI.
- Events also → Postgres → SSE `/runs/{id}/stream` → web board live agent timeline.

### 4.9 Eval harness

- **Corpus**: 10 seeded bugs in sample `OrderService`. Each bug = git tag + alert payload JSON + **hidden test** (Bob never sees it).
- Run each bug k=3 times (agent non-deterministic).
- Metrics:

| Metric            | Def                                                           |
| ----------------- | ------------------------------------------------------------- |
| pass@1 / pass@3   | gate passed **and** hidden test passes                        |
| false-fix rate    | gate passed, hidden test fails (agent fixed symptom, not bug) |
| gate-catch rate   | bad fixes blocked by gate before PR                           |
| median time-to-PR | alert → PR opened                                             |
| cost per fix      | Bobcoins / successful fix                                     |
| policy denials    | per run, per rule                                             |

- Output: `eval-report.md` + JSON. Same numbers go in README, slides, CV.
- Baseline: time a human (you) fixing 3 bugs by hand → comparison row.

---

## 5. Tech stack

| Layer                 | Tech                                                                                  | Why                                                                  |
| --------------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| Engine, CLI, eval     | **.NET 10, C#** — `Sentinel.Core`, `Sentinel.Cli` (dotnet tool, `System.CommandLine`) | main background, one engine for all doors                            |
| API + worker          | **ASP.NET Core minimal API** + `BackgroundService`                                    | webhook, hook sink, SSE, runs API                                    |
| DB                    | **PostgreSQL** + **EF Core** (Npgsql)                                                 | queue, dedupe via constraints, events, audit                         |
| Orchestration (local) | **.NET Aspire** AppHost                                                               | spin Postgres + Api + dashboard with one `dotnet run`; OTel for free |
| Context server        | **ModelContextProtocol C# SDK** (stdio)                                               | official MCP SDK for .NET                                            |
| Code analysis         | **Roslyn** (`Microsoft.CodeAnalysis.CSharp.Workspaces`, MSBuildWorkspace)             | frame → symbol, callers, new-test detection                          |
| Git                   | git CLI via `Process`                                                                 | simpler than LibGit2Sharp, worktree + bisect support                 |
| Sandbox               | Docker (`docker run` via `Process`)                                                   | isolation boundary                                                   |
| Telemetry             | **OpenTelemetry .NET** → OTLP → Aspire dashboard                                      | traces per tool call                                                 |
| Tests                 | **xUnit**, **Testcontainers for .NET** (Postgres)                                     | real DB in tests                                                     |
| GitHub Action         | **TypeScript** — `@actions/core`, `@actions/github` (Octokit), bundled w/ `esbuild`   | JS action = fast start, no Docker pull                               |
| Web board             | **TypeScript + React + Vite**                                                         | incident list, live run timeline (SSE), gate + eval views            |
| Sample victim         | ASP.NET Core Web API `OrderService` + EF Core + xUnit                                 | realistic .NET bugs                                                  |
| Agent                 | **IBM Bob 2.0** — Bob Shell (`bob run`), Bob IDE for building                         | challenge requirement                                                |

Skipped on purpose: message broker (Postgres enough), Kubernetes (Docker enough), OPA (C# rules enough), auth on board (local demo). Add when multi-tenant.

---

## 6. Repo layout

```
sentinel/
├─ src/
│  ├─ Sentinel.AppHost/        Aspire: Postgres + Api + dashboard
│  ├─ Sentinel.Core/           fingerprint, BobRunner, StreamJsonParser, Gate, Policy, GitOps
│  ├─ Sentinel.Api/            /alerts, /hooks, /runs, SSE, queue worker
│  ├─ Sentinel.Cli/            `sentinel fix | hook | eval`
│  └─ Sentinel.ContextMcp/     MCP server (Roslyn + git + dotnet test)
├─ tests/
│  └─ Sentinel.Tests/          Core unit tests + Testcontainers integration
├─ action/                     TS GitHub Action (action.yml, src/main.ts)
├─ web/                        TS React board
├─ samples/OrderService/       victim app, tests, bug tags, alerts/*.json, hidden-tests/
├─ .bob/
│  ├─ custom_modes.yaml        sentinel-triage, sentinel-fixer
│  ├─ rules-sentinel-fixer/    01-protocol.md, 02-dotnet-conventions.md
│  ├─ skills/repro-first/      SKILL.md
│  ├─ mcp.json                 context server registration
│  └─ settings.json            hooks → `sentinel hook …`
├─ bob_sessions/               task session summary screenshots (submission requirement)
├─ docs/                       architecture.png, eval-report.md
└─ README.md
```

### Custom mode sketch (verify schema vs docs at kickoff)

```yaml
customModes:
  - slug: sentinel-fixer
    name: Sentinel Fixer
    roleDefinition: >
      Senior .NET engineer on call. Reproduce bug with failing xUnit test first, then minimal fix.
    whenToUse: Automated incident remediation.
    groups:
      - read
      - mcp
      - command
      - - edit
        - fileRegex: ^(src|tests)/.*\.cs$
    customInstructions: >
      Never modify existing tests. Never touch config, secrets, network.
      Output SENTINEL_REPORT.md at end.
```

---

## 7. Bug corpus (OrderService)

| #   | Bug                                                         | Symptom / exception                              |
| --- | ----------------------------------------------------------- | ------------------------------------------------ |
| 1   | pagination off-by-one (`Skip(page * size)` w/ 1-based page) | missing first page items                         |
| 2   | null ref on customer w/o address                            | `NullReferenceException` in `ShippingCalculator` |
| 3   | decimal rounding `Math.Round` banker's vs away-from-zero    | invoice total off by 0.01                        |
| 4   | timezone: `DateTime.Now` vs UTC in order cutoff             | wrong delivery date                              |
| 5   | culture parsing `decimal.Parse("1,5")`                      | `FormatException` on de-DE                       |
| 6   | race: inventory decrement read-modify-write                 | oversell, negative stock                         |
| 7   | missing `await` → fire-and-forget save                      | `ObjectDisposedException` DbContext              |
| 8   | EF tracking: `AsNoTracking` then update                     | changes silently not saved                       |
| 9   | discount stacking validation missing                        | negative order total                             |
| 10  | enum string mapping mismatch                                | `ArgumentException` on status webhook            |

Mix: easy (1,2,5) → hard (6,7,8). Each: tag `bug/NN`, `alerts/NN.json`, `hidden-tests/BugNNTests.cs`.

---

## 8. Build ladder

Stop at any rung → still demoable.

| Rung      | Scope                                                                                                      | Done when                               |
| --------- | ---------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| **v0**    | TS Action: CI fail → `gh run view --log-failed` → `bob run --mode sentinel-triage` → PR comment root cause | comment appears on red PR               |
| **v1**    | `sentinel fix`: fixer mode + repro-first rules + gate + PR                                                 | bug 1–3 fixed w/ PR, gate table in body |
| **v2**    | ContextMcp + policy hooks + audit + Docker sandbox + red-team demo                                         | poisoned README attack blocked + logged |
| **v3**    | Api ingest/dedupe/queue, OTel → Aspire, eval harness, web board                                            | `eval-report.md` w/ real numbers        |
| v4 (post) | real Sentry/Alertmanager webhooks, multi-repo, OPA, Roslyn-heavy analysis, GitHub App                      | portfolio / blog post                   |

---

## 9. 48h plan (IST)

| When                         | Work                                                                                                |
| ---------------------------- | --------------------------------------------------------------------------------------------------- |
| Pre-kickoff (Sep 25 < 20:30) | confirm rules re pre-built code. Prep: OrderService + corpus design, repo skeleton, Aspire AppHost. |
| H0–2 (Sep 25 20:30)          | kickoff, get Bob access, `bob run` smoke test, API key in GH secrets                                |
| H2–8                         | **v0** Action live                                                                                  |
| H8–20 (Sep 26)               | **v1** fixer mode + gate + PR. Sleep.                                                               |
| H20–32                       | **v2** MCP server, policy hooks, sandbox, red-team                                                  |
| H32–40 (Sep 27)              | **v3** eval run (budget-sized), metrics, web board                                                  |
| H40–45                       | video (3 min), slides (6–8), README, architecture diagram, Bob session screenshots                  |
| H45–48                       | submit by ~17:30 IST. Buffer. Deadline 20:30 IST.                                                   |

Rule: feature freeze H40. No exceptions.

---

## 10. How Bob used to build Sentinel (judges want this)

- **Plan mode**: architecture + this doc → implementation plan.
- **Agent mode**: scaffold .NET solution, Aspire, EF migrations, Action.
- **Subagents**: parallel — write 10 bugs + hidden tests while main agent builds gate.
- **Doc understanding**: feed Bob docs PDF/markdown for custom mode + hook schema.
- **Review workflow** (`/review`): review Sentinel PRs.
- Screenshot **every task session summary** → `bob_sessions/`. Mandatory for submission.

### Bob usage & transparency

Sentinel itself runs on Bob modes/hooks, inside Bob IDE — not a side script.

Real Bob session list (screenshotted to `bob_sessions/`):

1. `/init` — repo bootstrap.
2. Ask mode — architecture discussion + Mermaid diagram.
3. Plan mode — this doc → implementation plan.
4. `sentinel-fixer` demo run — repro test → fix → gate.
5. `/review` — review Sentinel's own PRs.
6. Commit / PR creation sessions.

README discloses Claude Code was also used alongside Bob — full transparency, no hiding tooling.

**Coin budget (~40 Bobcoins total):**

| Use                                          | Coins |
| -------------------------------------------- | ----- |
| Build sessions (plan, agent mode, subagents) | ~12   |
| Fixer demo runs                              | ~20   |
| Reserve (retries, buffer)                    | ~8    |

Measure actual spend on the first real run via `stats.session_costs` in the `result` event, then adjust the plan — table above is a budget, not a guarantee.

---

## 11. Submission checklist

- [ ] Title, short + long description, tags
- [ ] Cover image
- [ ] Video: problem (20s) → live: red CI → Sentinel PR (90s) → red-team block (30s) → eval numbers (30s) → architecture (10s)
- [ ] Slides: problem, solution, architecture, safety, eval, Bob usage, roadmap
- [ ] Public repo (MIT), README w/ quickstart + eval table
- [ ] Demo URL (web board deployed or recorded) + sample repo w/ Sentinel PRs
- [ ] Bob task session summary screenshots
- [ ] Post-event feedback form ($100 pool)

---

## 12. Career payoff

**CV bullet (fill real numbers):**

> Built Sentinel, an autonomous incident-remediation system on IBM Bob 2.0 (.NET 10, ASP.NET Core, Roslyn, MCP, TypeScript): agent writes failing repro test, deterministic fail-to-pass gate verifies fix before PR; policy hooks + sandbox block test tampering and secret exfiltration. Eval over 10-bug corpus: X/10 pass@3, Y% false-fix rate, median alert→PR Z min vs ~N min manual. [Placement], IBM Bob 2.0 Hackathon.

**Interview stories it unlocks:**

- System design: dedupe via fingerprint + unique constraint, Postgres queue w/ `SKIP LOCKED` + visibility timeout, idempotency.
- Reliability: non-deterministic component wrapped in deterministic verification.
- Security: prompt injection threat model, hooks ≠ boundary, sandbox + egress allowlist, least-privilege tokens.
- Measurement: pass@k, false-fix rate, hidden tests (SWE-bench method).
- Trade-offs: why Postgres not RabbitMQ, why C# rules not OPA, why one process.

**After event:** LinkedIn post + demo video, tag IBM + lablab.ai. Connect judges (Uber, Amazon, Walmart, PayPal, AmEx staff/principal eng) w/ 2-line note + repo link. Blog: "Making headless AI agents safe in CI".

---

## 13. Risks

| Risk                                      | Mitigation                                                                                          |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------- |
| hooks not firing in `bob run`             | gate independently checks test tamper; policy via custom mode `fileRegex` + `--disable-tool-groups` |
| Bobcoin budget small                      | k=1 on full corpus, k=3 on 3 bugs; `--max-cost` per run                                             |
| Bob can't run in GH runner (auth/install) | run CLI locally/worker, Action only posts results                                                   |
| Roslyn MSBuildWorkspace slow/fragile      | fall back to regex on stack frames + `git grep`                                                     |
| Scope creep                               | ladder. Freeze H40.                                                                                 |
| Docker sandbox eats time                  | v1 runs in ephemeral GH runner, sandbox only in v2                                                  |
