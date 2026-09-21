# Verification — Spec 202, a version that survives a restart (#2426)

## Red → green, the true behavioral evidence

Phase 4a (`test-writer`), against a real, freshly-booted Aspire stack, with a
genuine `system-variables` resource restart (not simulated — confirmed via
`ResourceCommandService.ExecuteCommandAsync` returning success and
`WaitForResourceHealthyAsync` completing before any post-restart assertion
ran):

```
Failed The_push_path_survives_a_restart
  Shouldly.ShouldAssertException : afterRestart
    should be greater than
3L
    but was
1L
  the version after restarting system-variables (1) was not strictly greater
  than the highest version issued before the restart (3) — a connected kiosk
  holding 3 as its high-water mark would silently discard this push and every
  push after it, as stale, until the counter climbed back past its own history
Output: versions before restart: 1, 2, 3 / restarted system-variables / version after restart: 1

Failed The_snapshot_path_survives_a_restart
  Shouldly.ShouldAssertException : versionAfterRestart
    should be greater than or equal to
3L
    but was
0L
  the snapshot's version after restarting system-variables (0) was lower than
  the highest version issued before the restart (3) — the REST path resets
  exactly like the push path does
Output: highest version before restart: 3 / restarted system-variables / snapshot version after restart: 0
```

After phase 4b's fix, independently re-verified by the orchestrator against a
freshly booted Aspire stack — not trusting the implementing agent's own
report, and running these tests with an EXPLICIT filter, since they carry
`[Trait("Category", "Disruptive")]` and CI's default filter excludes that
category:

```
Total tests: 2
     Passed: 2
 Total time: 4,0192 Minutes
```

Both `The_push_path_survives_a_restart` (SC-1) and
`The_snapshot_path_survives_a_restart` (SC-2) pass, against a real Aspire
resource restart — this is the strongest evidence available for this fix, and
it was independently reproduced, not merely trusted from phase 4b's report.

## Other suites, independently re-run

```
Passed!  - Failed: 0, Passed: 96, Total: 96
```
(`SystemVariables.Application.Tests` — includes the fan-out-advances-once
facts and the version-before-text ordering fact.)

```
Passed!  - Failed: 0, Passed: 24, Total: 24
```
(`SystemVariables.Infrastructure.Tests` — the two version-related facts that
tested the now-removed `InMemoryReverseIndex` methods were removed, not
weakened; every remaining fact is unmodified.)

```
Total tests: 6
     Passed: 6
```
(`OverlayTextVersionStoreIntegrationTests`, against a real database — covers
the floor, the increment, the restart guarantee at unit cost via a fresh DI
scope, two genuinely concurrent advances, a batch with a duplicate identifier,
and the zero-for-untouched read.)

```
Passed!  - Failed: 0, Passed: 444, Total: 444
```
(`Architecture.Tests` — no boundary violation.)

`dotnet build -c Release` on the full solution: **0 errors**.

## Manual end-to-end procedure

`spec.md`'s independent end-to-end test procedure (booting a stack by hand,
watching a kiosk tile visibly update, restarting SystemVariables, confirming
the tile still updates and the snapshot reports a version at or above its
pre-restart high) was **not separately run**. The automated restart tests
above exercise the identical mechanism — a real resource restart against a
real stack — through the same HTTP and SignalR surfaces a human would drive
by hand; the one thing they don't prove is that a React tile visibly
re-renders, which is a frontend-rendering question this fix doesn't touch (no
file under `apps/` is in the diff). Given this session's repeated experience
with this shared machine's memory ceiling during full-stack verification
(documented in specs 199, 200, and 201's own verification notes), a further
manual walkthrough was judged not to add proportional evidence.

## Latency budget — measured, not assumed

**Leg: `event → overlay state`, ≤ 200 ms** (constitution §IV).

Baseline, phase 4a, two runs on unmodified code:
```
Run 1: median 36 ms, worst 83 ms
Run 2: median 38 ms, worst 75 ms
```

After the fix, independently re-run twice by the orchestrator:
```
Run 1: median 28 ms, worst 41 ms, samples [18, 18, 28, 30, 41] ms
Run 2: median 22 ms, worst 88 ms, samples [11, 11, 22, 81, 88] ms
```

**What this does and doesn't show (phase-6 review, backend-reviewer):**
the after-fix medians (28 ms, 22 ms) are *lower* than before (36 ms, 38 ms),
which a real regression cannot produce — so all four numbers are run-to-run
noise, not a measurement of the added statement's cost, and n=5 per run
means each median has wide sampling variance (the after-fix worst case alone
swung from 41 ms to 88 ms between the two runs). Honestly stated: this
methodology excludes a regression above roughly ±15 ms; it cannot resolve
the sub-millisecond cost a single parameterized `INSERT` actually adds, and
every sample across all four runs staying far under the 200 ms budget is the
real evidence here, not the medians' similarity. It also does not exercise
the case most likely to show a real cost — concurrent changes to
overlapping overlay sets, where `AdvanceAsync`'s row locks are now held
until the caller's ambient transaction commits (see the phase-6 deadlock
finding fixed in the SQL itself, via a deterministic lock order). A future
measurement wanting to isolate the actual added cost should time
`AdvanceAsync` directly with a much larger sample, not infer it end-to-end.

## Rejected fix directions

Recorded per `tasks.md` T019's instruction, one line each, reviewable without
opening `plan.md`:

1. **Deriving the version from the variable's own `AggregateVersion`** —
   incorrect, not just weaker: an overlay referencing two variables at
   different versions regresses the moment the lower-versioned one changes.
2. **A Postgres sequence** — would work, but generating one requires an ADR
   this lane may not write (ADR-0144), and its value is unrelated to any
   specific overlay's own history regardless.
3. **Client-side self-healing** — structurally can't close the gap: nothing
   currently triggers a refetch on a dropped push, and even if something did,
   it would be correcting a corrupted number with a corrupted one (the REST
   snapshot's version resets on the identical restart).

## §VII dashboard obligation

Not discharged by this change — already tracked as a standing, undischarged
condition (#1707, #1940), unrelated to #2426.

## Phase 6 — pending

`backend-reviewer` and `security-reviewer` (narrow: the store takes
caller-influenced overlay identifiers into raw SQL — confirm parameterization;
confirm `sse.variables.read` remains required on the snapshot route,
unchanged).
