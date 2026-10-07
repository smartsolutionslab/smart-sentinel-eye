# Tasks 307: Telemetry that fits its bucket

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #2563 (feature-level; must be on Project #13).
**Engineer:** frontend. **One PR**, commits in task order, each building and passing on its own.
`[P]` = disjoint files (ADR-0109). Phase 4a colour per task: **C** characterisation (green
before and after, unmodified), **R** red first.

## Phase 0: baseline (blocks everything)

- [ ] **T001 [C] [US1]** On the branch base, run and record verbatim: `apps/shared` —
  `kioskLatency.test.ts`, `CameraViewer*.test.tsx`; `apps/kiosk-web` — `useWallAlignment.test.ts`,
  `LayoutGrid*.test.tsx`, `CellPage*.test.tsx`; `apps/management-web` — `CameraViewer*.test.tsx`.
  This is the characterisation baseline quoted in the PR.

## Phase 1: red tests (test-writer; after T001)

- [ ] **T002 [R] [P] [US1]** `apps/shared/src/observability/kioskLatency.test.ts`: new cases per
  plan.md §Testing 1 (throttle admit/refuse/boundary/independence; console line printed when
  POST refused; invalid figure does not consume the window; throttled call never calls
  `getToken`). Red: `createReportThrottle` does not exist.
- [ ] **T003 [R] [P] [US1]** New `apps/shared/src/ui/composites/CameraViewerReportCadence.test.tsx`
  per plan.md §Testing 2 (120 s ⇒ 4 `presentation_buffer` + 4 `receive_to_decoded` POSTs;
  `onLagMeasured` and console lines still per sample; reconnect inside 30 s ships nothing new).
  Red: today ~59 / ~23 POSTs.
- [ ] **T004 [R] [P] [US1]** `apps/kiosk-web/src/features/cell/useWallAlignment.test.ts`: one new
  case, 120 s ⇒ 4 `wall_skew` POSTs, target still updated per cycle. Red: today 60.

Test-writer returns verbatim red output for T002–T004; the engineer may not edit them.

## Phase 2: implementation (frontend-engineer; after T002–T004)

- [ ] **T005 [US1]** `apps/shared/src/observability/kioskLatency.ts`: `PERIODIC_REPORT_INTERVAL_MS`,
  `ReportThrottle`, `createReportThrottle`, optional 5th `throttle` param with order
  guards → console → throttle → send. **Gate:** T002 green; T001 `kioskLatency.test.ts` cases
  green unmodified. Blocks T006/T007.
- [ ] **T006 [P] [US1]** `apps/shared/src/ui/composites/CameraViewer.tsx`: component-scope
  `reportThrottleRef`; pass it at the `receive_to_decoded` and `presentation_buffer` calls only.
  **Gate:** T003 green.
- [ ] **T007 [P] [US1]** `apps/kiosk-web/src/features/cell/useWallAlignment.ts`: `skewThrottleRef`;
  pass it at the `wall_skew` call. **Gate:** T004 green.
- [ ] **T008 [C] [US1]** Re-run T001's set: all green, zero assertion edits. Lint, `tsc --noEmit`,
  prettier clean. Add the new test file to any frontend shard filter if one exists.

## Phase 5 (verify)

- [ ] **T009 [US1]** Run spec.md's end-to-end procedure on the live stack (4-tile wall, 60 s
  request count, before on `develop` and after on the branch, run twice). Record per-measurement
  counts and the total against SC-001 (≤ 70/min). Confirm the three instruments still receive
  values in the Aspire dashboard. Latency: N/A (observer-only); include spec.md's
  measurement-validity note in the PR body.

## Dependency summary for the orchestrator

T001 → {T002, T003, T004 in parallel (test-writer)} → T005 → {T006, T007 [P]} → T008 → T009.
Foundational: T005 (`kioskLatency.ts` is the shared seam both call sites import). No backend,
AppHost, gateway, contract or migration task exists.
