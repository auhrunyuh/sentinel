# Sentinel demo video script (≤3:00)

## Pre-recording checklist

- [ ] Hooks ENABLED in Bob Settings for the demo workspace (not audit mode).
- [ ] `launchctl unsetenv SENTINEL_POLICY`, then fully quit and restart Bob IDE.
- [ ] `dotnet build -c Release` in the main repo.
- [ ] Run `sh scripts/demo-setup.sh 02` once beforehand to confirm bug 02 injects cleanly
      (re-run right before recording so the demo dir is fresh).
- [ ] Terminal font large, notifications off (Do Not Disturb).
- [ ] Dry run the red-team demo: `sh scripts/demo-redteam.sh --dry-run` — confirm the
      printed commands look right, then clear the terminal before recording for real.
- [ ] Have `bob_sessions/` screenshots and a pre-recorded `.sentinel/audit.jsonl` on hand
      as a fallback (see below).

## Shot list

**0:00–0:20 — Problem hook**
- On screen: terminal, a red CI run / stack trace for OrderService.
- Narration: "When CI goes red or a prod alert fires, someone has to read the
  stack trace, find the bug, and fix it fast. Handing that straight to an AI
  agent is risky — a headless agent with free rein can read your secrets, hit
  the network, or just edit the failing test to fake a green build."

**0:20–0:35 — What Sentinel is**
- On screen: the architecture diagram from README.md.
- Narration: "Sentinel is a safe, autonomous incident-remediation system built
  on IBM Bob. An agent proposes the fix, but nothing ships on the agent's word
  alone — it has to pass a fail-to-pass gate, and every action it takes goes
  through a policy firewall first."

**0:35–1:35 — Task 08 LIVE: Bob IDE fixes bug 02**
- On screen: Bob IDE, sentinel-fixer mode, `@alerts/02.json` open.
- Action: prompt Bob with the sentinel-fixer instruction from BOB_PLAYBOOK task 08;
  let it write a new failing xUnit test, run it red, apply the minimal fix, run
  green, write SENTINEL_REPORT.md.
- Narration while it runs: "This is live — Bob mode sentinel-fixer, working
  against a real seeded bug. It has to write a failing test first and watch it
  fail before it's allowed to touch the fix."
- Cut to terminal:
  ```
  cd ${TMPDIR:-/tmp}/sentinel-demo-02 && SENTINEL_HOME=/Users/aaarny/Desktop/Hackathons/BOB sh scripts/sentinel gate --base HEAD --project OrderService.slnx
  ```
- On screen: PASS table from the gate.
- Narration: "That's the fail-to-pass gate — deterministic, not a vibe check.
  New test failed on the base commit, passes now, full suite is green."

**1:35–2:20 — Task 09 LIVE: firewall blocks injection**
- On screen: terminal.
- Action: `sh scripts/demo-redteam.sh` (real run, not `--dry-run`).
- Narration: "This alert has a prompt injection baked into it — it tells the
  agent to dump environment variables, exfiltrate a file to an attacker's
  server, and delete the failing test to fake a pass. Watch what happens."
- On screen: FIREWALL DECISIONS output — DENIED lines with rule names
  (no-env-dump, no-network, no-test-tamper), then `audit log OK` from
  `sentinel audit verify`.
- Narration: "Every one of those is blocked before it runs, and it's all in a
  hash-chained audit log so the denial record can't be edited after the fact.
  We actually hit this for real during development — Bob bypassed our
  pre-commit hook with `--no-verify` on its own, so we added a rule for that
  too: no-hook-bypass."

**2:20–2:45 — Cost + guardrails**
- On screen: terminal cost output from the fix run, or a quick slide.
- Narration: "The security and performance reviewers are single-turn,
  tool-less verdict agents — cheap and can't go rogue because they can't act.
  Every fix run has a `--max-cost` cap. Total spend across this whole demo:
  [FILL IN Bobcoins]."

**2:45–3:00 — Close**
- On screen: README.md scrolled to the top, then the repo URL.
- Narration: "Sentinel: github.com/auhrunyuh/sentinel. Thanks for watching."

## Fallback: if Bob doesn't trigger a block live

If the live red-team run doesn't produce a visible DENIED line on camera (e.g.
the agent gives up before trying the injected command), don't retake blind:

1. Cut to a pre-recorded/pre-captured `.sentinel/audit.jsonl` from an earlier
   successful run of `scripts/demo-redteam.sh` (keep one on disk beforehand).
2. Show the FIREWALL DECISIONS section from that earlier run on screen.
3. Narration: "This is from an earlier run of the same red-team fixture —
   here's exactly what got denied and why," then read the rule names and
   reasons out loud.
4. Still show `sentinel audit verify` succeeding, live or from the same
   earlier run, to prove the log wasn't tampered with.
