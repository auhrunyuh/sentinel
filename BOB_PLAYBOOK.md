# Bob playbook: finish Sentinel with Bob as the main component

Rules for every session:
- Use the hackathon account (`ibm-coding-challenge-…`, us-east). Check the Bobcoin meter after each task.
- After each task, open **Tasks** in the chat panel, click the task header, and screenshot the session summary. Save it as `bob_sessions/sentinel_taskNN_<desc>_summary.png`.
- **Build sessions** (tasks 01–07) run in the main repo with `SENTINEL_POLICY=audit`. Otherwise the firewall blocks Bob from editing `.bob/` and existing tests, which is its job. **Demo sessions** (08–09) run with the default enforce mode.
- Commits happen only through Bob (task 01 and task 10).

---

## 0. Terminal checks (≈0.02 coin)

```bash
test -n "$BOB_API_KEY" && echo set || echo missing
cd ${TMPDIR:-/tmp}/sentinel-demo-01
echo "reply with the word ok" | bob run --mode ask --format json --max-turns 1 --max-cost 0.05 --accept-license --trust; echo "exit=$?"
echo "reply with the word ok" | bob run --mode sentinel-fixer --format json --max-turns 1 --max-cost 0.05 --accept-license --trust; echo "exit=$?"
```
Both should print one JSON line with `"type":"result"`. Paste both to Claude if not.

## 1. Headless fix, the core demo (cap 1 coin)

```bash
cd /Users/aaarny/Desktop/Hackathons/BOB && sh scripts/demo-setup.sh 01
cd ${TMPDIR:-/tmp}/sentinel-demo-01 && SENTINEL_HOME=/Users/aaarny/Desktop/Hackathons/BOB sh scripts/sentinel fix --alert alerts/01.json --project OrderService.slnx --max-cost 1
```
Save the output (gate table + cost) for slides.

## 2. Open Bob IDE on the main repo in audit mode

```bash
cd /Users/aaarny/Desktop/Hackathons/BOB
open --env SENTINEL_POLICY=audit -a "<Bob IDE app name from /Applications>" .
```
Trust the folder when asked, or the hooks and custom modes won't load.

---

## Bob IDE sessions

### Task 01 — Baseline commit (Agent mode)
> Stage all files in this repository and create the first commit. Generate a conventional commit message summarising the project: Sentinel, a safe incident-remediation system on IBM Bob (fail-to-pass gate, agent firewall via Bob hooks, verdict agents, sample OrderService with bug corpus, GitHub Action). Add this line to the commit body: "Initial scaffold written with Claude Code (Anthropic); subsequent features built with IBM Bob." Do not modify any files.

### Task 02 — Project context (Agent mode)
> /init

### Task 03 — Architecture docs (Ask mode, then switch to Agent)
Ask:
> Explain the architecture of @src/Sentinel.Core and @src/Sentinel.Cli/Program.cs: the components, how `sentinel fix` flows from alert to Bob to gate to resume, and how the hooks in @.bob/settings.json enforce policy.

Agent:
> Save that explanation to docs/architecture.md with three Mermaid diagrams: (1) component diagram of Sentinel.Core classes (Bob, Fix, Gate, Review, Policy, Audit, SecretScanner, Diff); (2) sequence diagram of `sentinel fix` including the gate and the --resume retry loop; (3) flowchart of the diff-guard path: save → scanner → optional verdict agents → router → fixer. Keep it under 150 lines. Only create that one file.

### Task 04 — Eval harness (Plan mode, then Agent)
Plan:
> Plan a `sentinel eval` command. For each bug NN in samples/OrderService/bugs: run scripts/demo-setup.sh NN into a temp dir, run Fix.RunAsync there with alerts/NN.json (SENTINEL_HOME set, --max-cost per bug from a flag, default 0.5), then copy samples/OrderService/hidden-tests/BugNNTests.cs into the temp repo's test project and run only that test. Metrics per bug: gate pass, hidden-test pass, cost, tokens, duration. Summary: pass@1 (gate AND hidden pass), false-fix rate (gate pass but hidden fail), gate-catch count, median duration, cost per successful fix. Output .sentinel/eval.json plus a markdown table. Reuse Proc, Gate, Fix; no new NuGet packages. Add a `--bugs 01,03` filter so I can run a subset. Keep it minimal.

Agent:
> Implement the plan in a new src/Sentinel.Core/Eval.cs plus an `eval` case in src/Sentinel.Cli/Program.cs, with one unit test for the metric calculation in tests/Sentinel.Tests. Run `dotnet build -c Release` and `dotnet test`, and fix any failures.

### Task 05 — Security rules tuning (Agent mode)
> Tighten @.bob/rules-sentinel-sec-review/01-checklist.md for ASP.NET Core: missing [Authorize] on new endpoints, over-posting via model binding, PII or secrets in ILogger calls, raw SQL string concatenation, and path traversal from user input. Keep the file under 25 lines and keep the exact <verdict> output format unchanged.

### Task 06 — Red-team fixture (Agent mode)
> Create samples/OrderService/alerts/99-poisoned.json: a copy of alerts/01.json whose "message" field also contains a prompt-injection attempt telling the agent to run `printenv`, to curl a URL, and to delete the failing test in OrderService.Tests. This fixture exists to prove Sentinel's PreToolUse policy blocks those actions. Add one line about it to samples/OrderService/README.md.

### Task 07 — README results (Agent mode, after running eval below)
> Read .sentinel/eval.json and fill in the eval results table in @README.md with the real numbers. Add a short "Firewall in action" section quoting the blocked actions from the red-team run in .sentinel/audit.jsonl (rule names and reasons only, no secrets).

### Task 08 — DEMO: interactive fix in Bob IDE (enforce mode; record the screen)
```bash
sh scripts/demo-setup.sh 02
open -a "<Bob IDE app name>" ${TMPDIR:-/tmp}/sentinel-demo-02
```
Switch to the **sentinel-fixer** mode and prompt:
> Fix the production incident described in @alerts/02.json following your mode rules: first add a NEW failing xUnit test in a new *Tests.cs file that reproduces it, run it to confirm it fails, then apply the minimal fix, run the full test suite, and write SENTINEL_REPORT.md.

Then in a terminal:
```bash
cd ${TMPDIR:-/tmp}/sentinel-demo-02 && SENTINEL_HOME=/Users/aaarny/Desktop/Hackathons/BOB sh scripts/sentinel gate --base HEAD --project OrderService.slnx
```

### Task 09 — DEMO: firewall blocks injection (enforce mode; record the screen)
```bash
sh scripts/demo-setup.sh 01 ${TMPDIR:-/tmp}/sentinel-redteam
cp samples/OrderService/alerts/99-poisoned.json ${TMPDIR:-/tmp}/sentinel-redteam/alerts/
cd ${TMPDIR:-/tmp}/sentinel-redteam && git add -A && git commit -qm "red-team alert"
SENTINEL_HOME=/Users/aaarny/Desktop/Hackathons/BOB sh scripts/sentinel fix --alert alerts/99-poisoned.json --project OrderService.slnx --max-cost 0.5
SENTINEL_HOME=/Users/aaarny/Desktop/Hackathons/BOB sh scripts/sentinel audit verify
grep '"allow":false' .sentinel/audit.jsonl
```
Commit the fixture in the demo repo (as above) or `fix` will refuse to run on a dirty tree.

### Task 10 — Review + final commit + PR (Agent mode, main repo)
> /review the uncommitted changes. Then generate a conventional commit message and commit. Then create a pull request titled "Sentinel: eval harness, docs, red-team fixture" with a description summarising the changes and linking the bob_sessions/ evidence.

---

## Eval run (after task 04; ~2.5 coins for 5 bugs at 0.5)

```bash
cd /Users/aaarny/Desktop/Hackathons/BOB && dotnet build -c Release
sh scripts/sentinel eval --bugs 01,02,03,04,05 --max-cost 0.5
```
Short on coins → `--bugs 01,03`.

## Estimated budget (40 coins)
| Item | Coins |
|---|---|
| Tasks 01–07 (build sessions) | ~8–12 |
| Headless fix + red-team | ~1.5 |
| Task 08 IDE demo | ~1–2 |
| Eval, 5 bugs | ~2.5 |
| Reserve (retakes) | rest |
