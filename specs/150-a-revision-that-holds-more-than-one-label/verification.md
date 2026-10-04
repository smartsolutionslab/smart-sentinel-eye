# Phase 5 verification — spec 150 / issue #2345

Date: 2026-10-04. Branch `feat/2345-a-revision-that-holds-more-than-one-label-v2`,
worktree `D:\Github\sse-2345`, commits `11c9d9b7` (backend, T004-T016/T022),
`b65d08ad` (frontend, T017-T021), `22f19df4` (e2e, T023/T026).

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
