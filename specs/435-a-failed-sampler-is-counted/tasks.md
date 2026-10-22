# Tasks: A failed kiosk sampler is counted at the sink

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2313 (feature-level,
on Project #13) · **Decision**: ADR-0180

**Phase 4a colour: RED** (behaviour-changing). One PR (ADR-0180 point 4); kiosk and server
must not land separately.

Format: `[ID] [P?] [Story] description`. `[P]` = disjoint files (ADR-0109). No foundational
task: the slice adds no Shared.Kernel/Contracts/AppHost resource.

## Phase 4a — tests first (test-writer), observed red, output quoted verbatim

- [ ] T001 [P] [US1] `tests/Architecture.Tests/KioskLatencyRecordingCharacterisationTests.cs`:
  add the three `sampler_failed` facts/theory from plan §Tests; extend `MeterObserver` with
  `"SmartSentinelEye.KioskSamplerFailures"` (string literal until T005 lands the constant)
  and a `long` callback; update `MeasurementRefusalBody` to list `'sampler_failed'` (spec F3 —
  the only permitted edit to an existing assertion).
- [ ] T002 [P] [US1] `tests/Architecture.Tests/KioskMeasurementContractTests.cs`: add
  `[InlineData("sampler_failed", "KioskSamplerFailures.Record")]` to the routing theory.
- [ ] T003 [P] [US1] `apps/shared/src/observability/kioskLatency.test.ts`: `sampler_failed`
  with 100000 is POSTed; with `NaN` is not.
- [ ] T004 [P] [US1] `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx`: decode
  sampler throwing POSTs `sampler_failed` at counts 1 and 10 (body value = count), not at 2;
  lag sampler throwing POSTs at 1, not at 10, at 100; console line still present.
- [ ] T004b [P] [US1] `e2e/kiosk-latency-contract.spec.ts`: add `sampler_failed` to the
  accepted loop and to the refusal-message assertions; retitle "five" → "six".

Gate: T001-T004 run and **fail for the stated reason** (400 / missing name / no POST /
ceiling drop), verbatim output captured. T004b's red is observed in the e2e job or a local
full-stack run.

## Phase 4b — backend (backend-engineer) — depends on T001, T002

- [ ] T005 [P] [US1] `src/ServiceDefaults/KioskSamplerFailures.cs` (new, mirrors `WallSkew`)
  + `.AddMeter(KioskSamplerFailures.MeterName)` in `src/ServiceDefaults/Extensions.cs`.
- [ ] T006 [US1] `src/StreamDistribution/Api/StreamEndpoints.cs`: `isSamplerFailure` via
  `report.Measurement == "sampler_failed"`; unknown-check, `Record` branch (count ignored),
  `UnknownMeasurement()` message, `KioskLatencyReport` docs. Depends on T005.
- [ ] T007 [US1] Replace T001's meter-name literal with `KioskSamplerFailures.MeterName`.
  Depends on T005.

## Phase 4b — frontend (frontend-engineer) — depends on T003, T004; parallel to T005-T007

- [ ] T008 [P] [US1] `apps/shared/src/observability/kioskLatency.ts`: union entry, doc block,
  duration guards exempt `sampler_failed` (finiteness kept).
- [ ] T009 [US1] `apps/shared/src/ui/composites/CameraViewer.tsx`: `reportSamplerFailure`
  calls `reportKioskLatency('sampler_failed', cameraIdentifier, count, getToken,
  reportThrottleRef.current)` after `logResilienceEvent`; `getToken` in deps. Depends on T008.

Disjoint: backend owns `src/**` + `tests/Architecture.Tests/**`; frontend owns
`apps/shared/**` + `e2e/**`. No shared file.

## Phase 5 — verify

- [ ] T010 [US1] All tests green: `dotnet test tests/Architecture.Tests`, `npm run test` +
  `tsc --noEmit` + `prettier --check` + lint in `apps/shared`, the e2e contract spec.
- [ ] T011 [US1] Counterfactuals 1-6 from plan.md, each observed red then reverted; results
  written into the PR body as observed.
- [ ] T012 [US1] Live: boot the stack, force a tile's sampler to throw, read
  `sse.kiosk.sampler_failures{camera=…}` ≥ 1 in the Aspire dashboard. Latency: N/A every leg.
