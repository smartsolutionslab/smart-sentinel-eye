# Phase 5 verification — spec 263 / issue #2608, US1 only

Date: 2026-09-26. Branch `feat/2608-event-driven-scene-rotation`, worktree `D:\Github\sse-2608`.

## What was observed

**Whole-solution Release build**: `dotnet build SmartSentinelEye.slnx -c Release` —
`Build succeeded. 0 Error(s)` (230 warnings, all pre-existing SonarAnalyzer advisories
under ADR-0084, none new, none promoted to errors).

**Backend test suites, run directly by the orchestrator** (`dotnet test <project> -c
Release`, after the phase-6 remediation commits):

```
LayoutComposition.Domain.Tests:        Passed! Failed: 0, Passed: 216, Total: 216
LayoutComposition.Application.Tests:   Passed! Failed: 0, Passed: 118, Total: 118
Shared.Contracts.Tests:                Passed! Failed: 0, Passed: 88,  Total: 88
AuditObservability.Application.Tests:  Passed! Failed: 0, Passed: 73,  Total: 73
AuditObservability.Domain.Tests:       Passed! Failed: 0, Passed: 90,  Total: 90
Architecture.Tests:                    Passed! Failed: 0, Passed: 508, Total: 508
```

**Frontend test suites, run directly by the orchestrator** (`npx vitest run`, per app,
after the phase-6 remediation commits):

```
apps/shared:          33 files / 447 tests passed on the first full-suite run;
                       2 files / 3 tests failed under concurrent-build machine load
                       (OverlayEditorKeyboard/Undo timing tests, unrelated to walls).
                       Re-ran those 2 files in isolation: 2 files / 86 tests passed.
                       Consistent with this repo's own "measurement runs need
                       repeating" lesson — machine-load flake, not a regression.
apps/kiosk-web:        14 files / 198 tests passed, including CellPage.test.tsx
                       (55/55, unmodified from before the T063 refactor) and
                       WallPage.test.tsx (4/4).
apps/management-web:   42 files / 385 tests passed, including WallForm.test.tsx (3/3)
                       and WallDetailPage.test.tsx (3/3).
```

**e2e, offline parse only** (`npx playwright test --list`):
```
[kiosk] › kiosk-shows-a-wall.spec.ts:28:5 › a kiosk opens a published wall and its tiles render
[wall]  › wall-changes-its-scene.spec.ts:71:5  › US1-3 (switch + kiosk follows)
[wall]  › wall-changes-its-scene.spec.ts:111:5 › US1-17 (reconcile after reconnect)
Total: 9 tests in 8 files
```
Parses cleanly against the actual component markup (selectors verified by
frontend-reviewer against the real DOM). Not executed live — see gaps below.

**Phase-6 review**: backend-reviewer, frontend-reviewer, security-reviewer, and
`/code-review` all ran against the diff. All should-fix findings were remediated (see
PR body for the full list and the two findings refused in writing).

## What was NOT observed — the honest gap

This delivery could not get a live Aspire stack for `D:\Github\sse-2608` at any point
in this session. The machine's one available stack (ADR/lesson: "one machine, one
Aspire stack") belonged to `D:\Github\sse-2526`, confirmed in active use by the product
owner for unrelated work throughout. Booting a second, concurrent stack was avoided
deliberately — a second boot produces `FailedToStart` failures that read exactly like a
code defect, not a resource conflict, and would not have been trustworthy evidence
either way.

As a direct result:

1. **`tests/Integration.Tests` (the Aspire-fixture suite, including
   `WallEndpointsTests.cs` covering US1-1..15 and the new
   `WallNameCaseInsensitivityIntegrationTests.cs`) was verified only by `dotnet build -c
   Release` (0 errors), never executed against a live Postgres/Wolverine stack in this
   session.** This is the single biggest gap in this verification. CI's own Docker
   integration job is the real gate for this — this repo's own precedent (issue #2333)
   used the same resolution when a local stack genuinely wasn't available.
2. **FR-V1's settle-time measurement (spec.md §7: ≥20 scene switches on a live kiosk,
   p50/p95/max settle and blank time, run twice) was not performed.** No pass/fail
   threshold is defined for this figure (spec.md is explicit that this is data
   gathering, not a gate), but SC-004 lists it as a success criterion and tasks.md's
   T070 calls for it. **This is an open item, not a silent pass** — recommended as a
   fast-follow once a stack is available, ideally before merge if the timing works out,
   otherwise as a immediate post-merge action. No figure is fabricated here.
3. **A live click-path walkthrough of US1's "Independent test" (spec.md §4: create a
   wall in management-web, open it in the kiosk, click Next, observe the switch) was
   not performed**, for the same reason. The nearest available evidence is: the
   integration test file's assertions (build-verified, not run), the reviewers'
   independent code reads confirming the wiring is correct end to end (gateway route →
   endpoint → domain → outbox → post-commit broadcast → SignalR → kiosk hub → RTK Query
   cache), and the fix for the pre-commit broadcast race (phase-6 finding) that a live
   run would otherwise have been the only way to notice.
4. **`scripts/coverage-check.ps1` requires PowerShell 7, not installed in this
   environment** (`pwsh` not found; Windows PowerShell 5.1 refuses the script's
   `#requires` line). Substituted with the direct `dotnet test` runs above, which give
   pass/fail but not a coverage percentage. ADR-0065's coverage gates are enforced by
   CI regardless.

## Latency budget (constitution §IV)

**N/A — a scene switch is explicitly not on the event-to-overlay path** (spec.md
header, ADR-0157 §Consequences). It carries no overlay state and no plant-floor label.
No leg in the §IV table is spent by this change. The one product-relevant timing
concern (a wall re-negotiating N WebRTC sessions at once) is FR-V1 above — a Phase-5
measurement requirement with no defined threshold, not a §IV leg — and it is the one
piece of this verification that could not be completed live.

## Refused findings (see PR body for full rationale)

- Fab-existence disclosure via `WALL_SCENE_OTHER_FAB` vs `WALL_SCENE_NOT_FOUND`
  (security-reviewer finding 1) — refused as a code fix in this pass; flagged to the
  human as it reverses an explicit, spec-pinned acceptance scenario (US1-10).
- ADR-0114 fab-inference scope for `POST /walls` (security-reviewer finding 3) —
  flagged to the human; matches pre-existing repo drift (CameraCatalog, EventIngestion
  already infer fab without an ADR amendment), not a new problem introduced here.

## Summary

Every automated gate that could run locally, ran and is green (build, unit, domain,
application, architecture, contract, and frontend test suites — 1093 backend tests
(216+118+88+73+90+508 across the six listed projects) + 1033 frontend tests (450
shared + 198 kiosk-web + 385 management-web, per the phase-6 remediation's own final
run) total). The gap is entirely at the
live/integration layer, caused by a shared single-machine Aspire stack being in active
use by someone else for the whole session, not by anything discovered wrong with the
feature. This is reported as an open gap, not papered over.
