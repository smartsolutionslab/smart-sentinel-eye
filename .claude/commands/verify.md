---
description: Phase 5 (ADR-0037) — observe the change working end to end, not merely tests green, and write the verification note.
argument-hint: "[what to verify]  (omit to verify the current branch's change)"
---

# Verify — phase 5

ADR-0037's phase 5. The question this phase answers is **not** "do the
tests pass" — phase 4 answered that. It is: **has anyone watched this
behave?**

`$ARGUMENTS` names what to verify; with no argument, verify the change on
the current branch (`git diff origin/develop...HEAD --stat`).

## The distinction that makes this phase worth having

A green test proves the code does what the test says. This phase asks
whether the *system* does what the issue said. The gap between those is
where this repo's recorded failures live: a guard that read the design
artefact and proved only that the design was written down; a leg recorded
as measured before anyone read the figure; an e2e suite green in CI on
retries and red on a cold stack.

So: **ask the running system, once.** A source-scanning test, a
re-reading of the spec, or "the unit tests cover this" does not
discharge phase 5.

## What to run

Pick the smallest thing that actually observes the behaviour.

**Backend, unit + architecture:**
```sh
pwsh scripts/coverage-check.ps1 -Configuration Release
```
Release, not Debug — CI treats warnings as errors and Debug hides
CS8601/CS0618/IDE.

**Backend, integration (the real Aspire stack, ADR-0103):**
```sh
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj
```
Stop any running AppHost first, or MSB3027 will look like a broken
build. A restart wait needs `WaitOnResourceUnavailable` — the default
gives up on the very transition it should be watching.

**Frontend:**
```sh
pnpm -r --filter "./apps/**" test
pnpm exec playwright test --list     # parses, without a stack
```
Playwright needs the stack up, and locally it usually is not — so
`--list` is the offline check and the blocking CI `e2e` job is the real
one. If you do boot the stack and run e2e, a failure on a **cold** stack
is a real finding, not flake: CI's retries hide exactly that.

**Live, through the app:** boot the stack and drive it. This is the
strongest evidence and the one to prefer for anything a user touches.
Boot with the anonymous dashboard flag — a background `dotnet run`
buffers away the login link. Mint tokens from Aspire's **proxied**
endpoint, not the container's mapped port, or everything 401s.

## If the change is on the event-to-overlay path

Constitution §IV. Name the leg, cite the measurement against its budget,
and say how it was measured.

**Run the measurement twice.** The first run after machine churn looks
exactly like a regression, and this repo has been fooled by that. Report
the second figure, and say you ran it twice.

Legs and budgets are in CLAUDE.md; the authority is the table in
constitution §IV, which distinguishes four states across the six legs.
If the change is not on that path, write `N/A — <one-line reason>`.

## The note

Post it as a PR comment (or return it to the orchestrator). It must say:

- **what was observed** — the actual behaviour, in a sentence;
- **how** — the exact command or the click-path;
- **what the output was** — quoted, not characterised;
- **latency** — the leg and the figure, or `N/A — <reason>`;
- **what was not covered** — the honest gap. Every verification has one.

## Failing honestly

If the behaviour does not hold, say so and stop. A verification note that
reports a pass nobody watched is worse than no phase 5 at all — it
converts an unknown into a false record, and this repo has had to correct
several of those.
