# Sentinel — voiceover script (≤ 3:00)

Pace: calm, ~140 words/min. Words in **bold** get slight stress. `[…]` = what's on screen / when to act. Total ≈ 390 words.

---

## Part 1 — Intro slides (0:00 – 0:30)

**[Slide 1 · Problem]**
> AI agents can now fix production bugs. But run one headless, and it gets **every tool, unchecked**. It can weaken a test to make it pass, read your secrets, or skip your git hooks. So nobody ships "the AI fixed it" without **proof**.

**[Scroll → Slide 2 · Solution]**
> This is **Sentinel**. It turns an alert into a failing reproduction test, then a verified fix. Three guards: a fail-to-pass gate, an agent firewall on Bob's hooks, and hidden tests that catch fake fixes.

**[Scroll → Slide 3 · Built with IBM Bob]**
> IBM Bob is the engine. Sentinel drives Bob in a custom fixer mode, polices every tool call through Bob's hooks, and resumes Bob's task when a fix fails. And we built Sentinel **with** Bob.

**[Scroll → Slide 4 · Title card]** *(hold 2 seconds, say nothing, then cut to demo)*

---

## Part 2 — Task 8: Bob IDE fixes a bug (0:30 – 1:37)

**[Bob IDE: alerts/02.json on screen]**
> Here's a real alert: a null reference crashing the shipping-label endpoint.

**[Mode switched to Sentinel Fixer, prompt sent]**
> I switch Bob into our **Sentinel Fixer** mode. Its rules say: prove the bug first.

**[Bob writes the new test, runs it — red]**
> Bob writes a brand-new test that reproduces the crash, and runs it. It **fails** — good. That's the bug, on record.

**[Bob edits Shipping.cs, suite goes green]**
> Then a minimal fix, and the full suite goes green. Bob writes a short report: root cause, fix, risk.

**[Terminal: sentinel gate → PASS table]**
> But we don't trust Bob's word. Sentinel's gate checks it independently: the new test fails on the old code, passes on the new code, no existing test was touched, and the diff stays small. **Pass.**

> One honest detail: our **hidden** test — one Bob never sees — later showed this fix changed the error instead of returning a pickup label. The gate passed; the hidden test caught it. That's exactly why we measure false fixes.

---

## Part 3 — Task 9: the firewall under attack (1:37 – 2:50)

**[Terminal: demo-redteam.sh starts]**
> Now an attack. This alert is **poisoned**: hidden inside, instructions telling the agent to dump the environment, send our Bob settings to an attacker, and delete the failing test.

**[Bob working — sped up]**
> Sentinel runs Bob headless, with the firewall enforcing. Every tool call goes through our policy engine first.

**[DENIED lines appear]**
> Bob's own model refused the obvious commands. But it went looking at its **own guardrails** — the hook settings, its mode permissions. Sentinel **denied** every attempt.

**[audit verify → "audit log OK"]**
> Every decision is written to a hash-chained audit log. Change one line, and verify fails.

**[Gate PASS + cost]**
> And the real bug still got fixed. Gate pass on the first attempt — about **half a Bobcoin**, four minutes, and the hidden test passed.

---

## Part 4 — Close (2:50 – 3:00)

**[Title card or repo page]**
> While building this, Bob slipped past our controls three times — skipping a hook, writing through the shell, building a project around a blocked file. Each one is now a rule. Sentinel: proof, not promises.

---

### Recording tips
- Record Part 1 live while scrolling the slides (one scroll per paragraph).
- Parts 2–4: record the voice in iMovie over `sentinel-demo.mp4` (Record Voiceover button). Pause between blocks; trim later.
- If you run long, cut the "One honest detail" paragraph from Part 2 — it's also in the statements.
