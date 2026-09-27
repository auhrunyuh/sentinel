# Problem & Solution Statement

## Problem

When CI goes red or a production alert fires, an on-call engineer reads the stack
trace, finds the bug, writes a fix, tests it, and hopes it's right. That burns hours
per incident, often at 3am.

Headless AI agents look like the obvious fix, but a headless agent gets free rein —
every tool call is pre-approved before it runs. Nothing stops it from editing the
failing test to fake a green build, reading secrets, hitting the network, or
rewriting git history to hide what it did. And even when the patch is correct,
nobody trusts "the AI says it's fixed" without proof. Remediation without guardrails
or evidence isn't something an on-call or platform team can ship to production.

## Solution

Sentinel turns a CI failure or alert into a tested, verifiable fix PR — never an
auto-merge — built around four layers:

1. **Fail-to-pass gate.** The agent must first write a test reproducing the bug.
   Sentinel checks it out on the base commit in an isolated git worktree, confirms
   it fails there, applies the fix, then confirms the same test — plus the full
   suite — passes, within a diff-size budget. No fail-then-pass repro, no PR.
   Deterministic and code-level, not a judgment call.

2. **Agent Firewall.** Bob's PreToolUse/PostToolUse/Stop hooks run a policy engine
   in front of every tool call, denying (exit 2) writes to existing test files,
   reads of secrets or protected paths, network calls, and git-history rewrites
   (`push --force`, `rebase`, `commit --amend`, `reset --hard`). Every decision is
   written to an append-only, hash-chained audit log. Real incident from
   development: Bob itself bypassed our pre-commit hook with `git commit
   --no-verify`. We added a no-hook-bypass rule plus regression tests so that
   evasion can't recur.

3. **Diff guards.** A free, tool-less regex secret scanner runs on every save,
   independent of the gate. Optional one-turn Bob "verdict" agents can also review a
   diff for security or performance issues; only a high-confidence security verdict
   is routed to the fixer for a real repair, keeping the expensive path rare.

4. **Actor-critic retry, cost capped.** When the gate rejects a fix, Sentinel calls
   `--resume` on the same Bob task with the gate's failure output, so the agent
   retries with feedback instead of starting cold. `--max-cost` / `--max-turns` cap
   every run.

## Target users

On-call engineers and platform/SRE teams who need AI-assisted remediation they can
ship to production — the fix comes with a reproducible test and an audit trail, not
just an agent's word.

## Impact

[fill: eval results — pass@1, pass@3, false-fix rate, median time-to-PR, cost per
fix, policy denials caught] against the seeded bug corpus in `samples/OrderService`.

## Next steps

- Sandbox execution + an egress allowlist, so the firewall becomes a real security
  boundary, not just a policy layer on top of full user permissions.
- An alert-ingestion API for production alerts (Sentry/Alertmanager-style), not just
  CI-failure triggers.
- An MCP context server giving the fixer agent structured project context instead
  of raw file reads.
