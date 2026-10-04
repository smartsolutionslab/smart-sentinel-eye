# Phase 5 verification — spec 150 / issue #2345

Date: 2026-10-04. Branch `feat/2345-a-revision-that-holds-more-than-one-label-v2`,
worktree `D:\Github\sse-2345`, commits through `849110bc` (phase-6 fix round: two
blockers and three should-fix findings, see "Phase 6" below for detail).

## Phase 6 — review findings and their fixes, independently re-verified

Phase-6 review (`backend-reviewer` + `frontend-reviewer`) found **two blockers, four
should-fix items, and nine nits** across the backend and frontend chains. All blockers
and should-fix items were fixed; the result was independently re-verified by the
orchestrator, not merely relayed:

- **Blocker (backend): no guaranteed label order after an EF reload.** Fixed by sorting
  at the exposure point (`Revision.Labels`). The implementing engineer tried hard to
  reproduce the scramble as a failing test — including a raw-SQL counterfactual that
  bypassed the application layer entirely — and could not: this Postgres/EF combination's
  actual query-generation already orders owned-collection rows correctly during
  materialization. Reported honestly rather than claiming a false red; the fix was kept
  anyway because it makes ordering an explicit contract on the type rather than an
  implicit property of EF's current query shape, which is good practice independent of
  whether today's shape happens to get it right.
- **Blocker (backend): three untouched integration test helpers still read the deleted
  `resolvedText` (singular) wire shape.** Fixed in all three; the orchestrator
  independently ran the affected classes (`TwoPlaceholdersInOneLabelTests`,
  `MovedSnapshotAndResolveRoutesIntegrationTests`, `ResolvedTextReachesItsFabTests`) live
  against the real stack: 9/9 passed.
- **Should-fix: a `null` label element 500'd instead of 400'ing.** Fixed; the orchestrator
  independently ran the new HTTP tests (`OverlayGeometryValidationIntegrationTests`, now
  10 tests, was 8) live: 10/10 passed.
- **Should-fix: zero coverage on the edit path's label-set-size rejection.** Fixed; 48/48
  `OverlayDesigner.Application.Tests` (was 46), independently re-run.
- **Should-fix: the reverse-index seeder could misalign `ResolvedTexts` against labels
  when skipping a malformed element.** Fixed, with a genuine red→green (quoted in the PR
  body): 27/27 `SystemVariables.Infrastructure.Tests` (was 26), independently re-run.
- **Should-fix (frontend): a dropped null-safety guard on label text would crash the
  whole wall on render** (not a wire-shape defect — the multi-label rewrite lost an
  existing `?.` that protected against the "absent/renamed text" case). Fixed; 249/249
  `apps/kiosk-web` vitest (was 248), independently re-run.
- Nine nits across both chains: stale `<see cref>`/prose referencing deleted V1 types,
  an FR-007 test comment overclaiming its own assertions, a stale doc comment pointing
  at the wrong stabilisation helper, and others — all either fixed or explicitly accepted
  with a stated reason (none silently dropped).

Full non-integration suite, re-run after all phase-6 fixes, independently, matching the
implementing engineer's own report exactly:
```
OverlayDesigner.Domain.Tests:          100/100
OverlayDesigner.Application.Tests:      48/48
SystemVariables.Infrastructure.Tests:   27/27
SystemVariables.Application.Tests:     104/104
SystemVariables.Domain.Tests:          101/101
Architecture.Tests:                    617/617
LayoutComposition.Application.Tests:   149/149
Shared.Contracts.Tests:                102/102
AuditObservability.Application.Tests:   75/75
ScenarioSimulator.Tests:               162/162
```
`dotnet build -c Release`: 0 errors, re-confirmed after the fix round.

## Phase 5 — what was observed (pre-fix-round baseline, still accurate for everything
not touched by phase 6)

## What was observed

**`dotnet build SmartSentinelEye.slnx -c Release`**: 0 errors, 0 new warnings. Independently
reproduced by the orchestrator, not merely relayed.

**Full non-integration .NET suite**, independently re-run, every tally matching the
implementing engineer's own report exactly:
```
OverlayDesigner.Domain.Tests:         100/100
OverlayDesigner.Application.Tests:     46/46
SystemVariables.Application.Tests:    104/104
SystemVariables.Infrastructure.Tests:  26/26  (includes the seeder red test + counterfactual)
Shared.Contracts.Tests:               102/102
AuditObservability.Application.Tests:  75/75
LayoutComposition.Application.Tests:  149/149
Architecture.Tests:                   617/617
ScenarioSimulator.Tests:              162/162
```

**Live integration tests against the real Aspire stack** (T013 — the FR-007 branch
re-keying proof specifically needs a live EF round-trip, which a unit test cannot
produce):
```
OverlayDesigner-tagged integration tests: 30/30
Four other touched integration files:     22/22
```
`docker ps` confirmed empty after both runs (the fixture tears the stack down itself on
disposal). Independently re-run by the orchestrator (the first of the two groups); matched
exactly.

**Frontend**: `tsc --noEmit`, `eslint --max-warnings 0`, `prettier --check` clean across
`apps/shared`, `apps/kiosk-web`, `apps/management-web`. Full vitest suites, independently
re-run:
```
apps/shared:          574/574
apps/kiosk-web:       248/248
apps/management-web:  543/543
```

**The characterisation/guard set — confirmed unmodified, not just claimed:**
- `OverlayRevisionStateMachineTests` (7), `OverlayChainArchivalTests` (7),
  `TimestampOrderingTests` (1) — 15 backend tests, byte-identical assertions (only
  `Label.From(...)` → `[Label.From(...)]` call-site wrapping).
- `OverlayLabelCharacterisation.test.tsx`, `OverlayLabelParity.test.tsx`,
  `OverlayLabelNodeCountCharacterisation.test.tsx` — `git diff` read directly by the
  orchestrator: every changed line is a prop-construction line (`overlay={x}` →
  `overlays={[x]}`); zero assertion lines moved.
- `OverlayEditor.tsx` and its four guard files (`OverlayEditorCharacterisation`,
  `OverlayEditorBackdrop`, `OverlayEditorKeyboard`, `OverlayEditorUndo`) — `git diff --stat`
  against the pre-change commit is empty. Byte-identical, confirmed directly, not relayed.
- `e2e/kiosk-shows-a-label-over-video.spec.ts` — `git diff` against the pre-change commit
  is empty. Byte-identical.
- `e2e/overlays.spec.ts`'s four geometry assertions (`0.2487`, `0.5`, `0.3`, `0.9`) —
  `git diff` read directly: only the read path (`.labels[0]`) was added, no expected value
  changed.

**`pnpm exec playwright test --list`**: 125 tests in 41 files (was 124/40 before T023's
new spec), independently re-run and matching exactly — one new file, one new test, no
collisions, no drops.

## What was NOT observed — the honest gap

**T024 (re-read `overlay_draw` p50/p95 on the nine-tile fixture at `MaxLabels` worst case,
72 nodes, run twice) and T025 (`NFR_VariableResolutionLatencyTests`' median, run twice) were
not performed.** `C:` had only ~3.1 GB free (of 237 GB) when this phase was reached — this
repo's own recorded history is that an Aspire stack boot under that kind of disk pressure
has crashed Docker Desktop outright and left it unresponsive until a GUI restart. Given that
risk, and with the user's explicit agreement, the live render-leg and variable-resolution
re-measurements were deferred rather than attempted.

**This is a real gap, not a formality.** ADR-0164's own stated honesty is that `MaxLabels =
8` was chosen from the measured p50 and the corrected tile count, not from a measurement of
eight labels specifically — "that is a known gap, and it is the honest reason a raise is
gated on measurement rather than argument." T024 was meant to be the first direct
observation at the new worst case (72 label draws/frame). It still has not happened. The
render-leg instrument (`reportKioskLatency('overlay_draw', …)`) and the e2e scaffolding
(`e2e/support/render-leg.ts`) exist and are unchanged by this PR, so this measurement is
cheap to run **as a follow-up, before merge, once disk headroom allows** — it does not
require further code changes, only a clean stack boot.

**Every other part of phase 5 — the full build, every unit/integration test suite, the two
live integration runs, and the characterisation/guard set — was observed first-hand, not
inferred from reading the code.**

## Latency (constitution §IV)

**Not yet dischargeable — see the gap above.** This change is on the event-to-overlay path
(every label on a revision is drawn per tile per frame); §IV's table is not touched, same
as spec 150's own declared non-goal before this PR, but the obligation to actually read the
new worst case remains open until T024 runs.

## Stack teardown

No stack was booted for T024/T025; nothing to tear down for that part. The two live
integration-test runs for T013 tore themselves down via `AspireFixture` disposal; `docker
ps` confirmed empty after each.
