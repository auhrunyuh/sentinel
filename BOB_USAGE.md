# How we used IBM Bob (submission answer, ≤500 words)

> Fill every `[…]` from your real Bob task summaries (`bob_sessions/`). Delete any line you did not actually do. Count words before submitting.

IBM Bob is both the tool we built Sentinel with and the engine Sentinel runs on.

**1. Bob is the runtime.** Sentinel turns a production alert into a verified fix by driving Bob. `sentinel fix` runs Bob Shell headless (`bob run --mode sentinel-fixer --format json --max-cost`) with a custom mode whose `fileRegex` limits edits to C# source. Mode-specific rules (`.bob/rules-sentinel-*`) enforce a repro-test-first protocol. Bob's structured JSON output (`stats.task_id`, `session_costs`, tokens) feeds our fail-to-pass gate. When the gate rejects a fix, Sentinel calls `--resume <task_id>` so Bob continues the same task with the gate's findings, a closed actor-critic loop. Bob lifecycle hooks are the Agent Firewall. `PreToolUse` runs our policy engine and blocks (exit 2) test tampering, secret access, network calls and git rewrites. `PostToolUse` scans every file Bob saves for leaked secrets. `Stop` reviews the full diff. Diff review uses two Bob verdict agents (security, performance): tool-less, one turn, ~30-token JSON verdict. A router sends only a security leak/vuln verdict with confidence ≥ 0.8 to `sentinel-fixer`, verified by a secret rescan plus the full test suite. Every decision goes to a hash-chained audit log. The same `.bob/` config works in Bob IDE, so a developer can run the `sentinel-fixer` mode interactively and get identical guardrails.

**2. Bob built parts of Sentinel (Bob IDE sessions).**
- `/init` generated `AGENTS.md`, giving Bob persistent project context across sessions. [task NN, cost]
- Ask mode + context mentions (`@src/Sentinel.Core`) explained the architecture. Bob then generated the Mermaid diagrams in `docs/architecture.md`. [task NN]
- Plan mode designed the eval harness. Agent mode implemented `sentinel eval`, which runs the fixer over our 5-bug corpus and scores each result against hidden tests. [task NN, files, cost]
- Bob refined the security and performance review checklists in `.bob/rules-sentinel-*-review/`. [task NN]
- Demo: in Bob IDE, `sentinel-fixer` mode fixed injected bug [NN]. It wrote a failing repro test, applied a [N]-line fix, and passed our gate. The policy hook blocked [describe block] in the same session. [task NN, cost]
- `/review` reviewed [which diff], and Bob generated our commit messages / PR description. [task NN]

**3. Budget discipline.** We had 40 Bobcoins, so we designed for them. A free regex scanner runs on every save. The verdict agents run only when explicitly requested with `--llm`, are cached by diff hash, and have no tools and `--max-turns 1`. The expensive fixer runs only on a high-confidence security verdict; performance verdicts stay advisory. Every run is capped with `--max-cost` / `--max-turns`. Total spend: [X] Bobcoins across [N] tasks.

**4. Transparency.** We also used Claude Code (Anthropic) to write parts of the implementation, including the policy engine, the gate and the sample app. Bob's contributions are the sessions listed above, with screenshots in `bob_sessions/`.

**watsonx.ai / watsonx Orchestrate:** not used. [Or describe if you add it.]

---

## Session checklist (do in Bob IDE, hackathon account `ibm-coding-challenge-…`, us-east)

Screenshot each task's session summary → `bob_sessions/<team>_taskNN_<desc>_summary.png`. Check the coin meter after task 01 before continuing.

| # | Mode | Prompt | Output |
|---|---|---|---|
| 01 | Agent | `/init` | AGENTS.md |
| 02 | Ask → Agent | "Explain @src/Sentinel.Core architecture, then save Mermaid component + sequence diagrams for `sentinel fix` to docs/architecture.md" | docs/architecture.md |
| 03 | Plan → Agent | "Add `sentinel eval --corpus samples/OrderService`: for each bug NN, copy sample to temp git repo, apply bugs/NN.patch, commit, run Fix.RunAsync with alerts/NN.json, then copy hidden-tests/BugNNTests.cs in and run it. Report pass@1, false-fix rate, cost/fix to .sentinel/eval.json + markdown. Reuse Proc/Gate/Fix, no new packages." | eval command |
| 04 | Agent | "Tighten @.bob/rules-sentinel-sec-review/01-checklist.md for ASP.NET Core (authz attributes, model binding, logging PII)" | rules |
| 05 | sentinel-fixer | apply bug 01 in sample copy, then: "Fix incident @samples/OrderService/alerts/01.json" | demo + gate pass |
| 06 | Agent | `/review` on the eval diff, then generate commit message + PR | commit/PR |

Tip: task 05 is the demo video. Record the screen during it.
