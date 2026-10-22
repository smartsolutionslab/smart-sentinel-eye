# Implementation Plan: A failed kiosk sampler is counted at the sink

**Spec**: [spec.md](spec.md) · **Issue**: #2313 · **Decision**: ADR-0180 (amends ADR-0122)

## Summary

One closed-set name, `sampler_failed`, added on both sides in one PR (ADR-0180 point
4). The server routes it to a new counter before `SegmentFor`, never to
`LatencyBudget`; the kiosk sends it from `reportSamplerFailure` alongside the existing
console line. **One slice, two engineers on disjoint files**, frontend and backend.

## Constitution / ADR check

| Gate | Status |
|---|---|
| §IV latency budget | N/A every leg (spec §5). |
| §VII dashboard | Not a leg; the counter is visible in the Aspire dashboard under its meter (ADR-0117 binds legs). |
| ADR-0118 one sink | Held — reported through a service meter, no browser OTLP. |
| ADR-0122 as amended by ADR-0180 | Implemented as written; spec §2 F1-F3 resolve details within it. |
| Bounded contexts | StreamDistribution.Api → ServiceDefaults only (existing dependency, as `WallSkew`). No Shared.Contracts change. |
| §II primitives | Not applicable — Api/ServiceDefaults, no domain model. |
| ADR-0144 | No ADR or constitution edit. |
| Phase 4a colour | **Red** (behaviour-changing). |

## Design

### Server — `src/ServiceDefaults/KioskSamplerFailures.cs` (new)

Static class mirroring `WallSkew`: `public const string MeterName =
"SmartSentinelEye.KioskSamplerFailures"`; `Meter` + `Counter<long>` named
`sse.kiosk.sampler_failures` (unit `{failure}`); `public static void Record(Guid
camera)` adds 1 with tag `camera`. No value parameter: the count is ignored by
construction (ADR-0180 point 3/7), so it cannot be recorded by mistake.

`Extensions.ConfigureOpenTelemetry`: `.AddMeter(KioskSamplerFailures.MeterName)` in
the existing chain beside `WallSkew` / `LabelDelay`.

### Server — `StreamEndpoints.RecordKioskLatency`

- `var isSamplerFailure = report.Measurement == "sampler_failed";` — the
  `report.Measurement == "…"` spelling is required: `KioskMeasurementContractTests.
  ServerMeasurements()` parses exactly that, so a `switch` arm or a constant would
  make the guard miss the name and go red for the wrong reason.
- Unknown check becomes `segment is null && !isWallSkew && !isLabelDelay &&
  !isSamplerFailure`.
- Camera and finite guards unchanged and still run first (ADR-0180 point 3).
- `Record` gains the branch `KioskSamplerFailures.Record(report.Camera)`, before the
  `LatencyBudget` fall-through. The method already takes two flags; add the third as
  a parameter (it stays under ADR-0084's 4-parameter advisory limit only if folded —
  acceptable either way, ADR-0084 is advisory) or branch in the caller; engineer's
  choice, smallest diff.
- `UnknownMeasurement()` message: `"must be 'overlay_draw', 'receive_to_decoded',
  'presentation_buffer', 'wall_skew', 'label_delay' or 'sampler_failed'"` — the
  `"must be '…'" + "'…'"` two-literal shape is what the contract test's regex reads;
  keep both literals' shape.
- Doc on `KioskLatencyReport.Measurement` / `ElapsedMilliseconds` names
  `sampler_failed` and that its value is a count the server ignores.

### Kiosk — `apps/shared/src/observability/kioskLatency.ts`

- `KioskMeasurement` gains `'sampler_failed'` (keep the single `export type … = …;`
  declaration; the guard's regex reads up to the first `;`).
- Doc block above it: a third non-segment name, a count not a duration (ADR-0180).
- `reportKioskLatency`: the `< 0` and `> ABSURDLY_LONG_MS` guards apply to every
  name except `sampler_failed`; finiteness applies to all (spec F1).

### Kiosk — `apps/shared/src/ui/composites/CameraViewer.tsx`

`reportSamplerFailure`, after `logResilienceEvent` and inside the same `count !==
null` path: `reportKioskLatency('sampler_failed', cameraIdentifier, count, getToken,
reportThrottleRef.current)`. Add `getToken` to the `useCallback` deps. Both samplers
already route through this callback, so one edit covers both.

## Tests (phase 4a, red)

| File | Test | Red because |
|---|---|---|
| `tests/Architecture.Tests/KioskLatencyRecordingCharacterisationTests.cs` | `The_sampler_failed_measurement_is_counted_on_its_own_instrument_and_not_as_latency` (202, one point on `sse.kiosk.sampler_failures` = 1, tag camera, nothing else); `The_sampler_failed_count_is_ignored_and_never_recorded_as_a_value` (theory 0 / 100000 / -5 → adds 1); `A_sampler_failed_report_with_an_empty_camera_is_refused_and_nothing_is_counted`; `MeasurementRefusalBody` updated to list `sampler_failed` (spec F3). `MeterObserver` gains the meter name and a `SetMeasurementEventCallback<long>`. | Unknown name → 400 today. |
| `tests/Architecture.Tests/KioskMeasurementContractTests.cs` | Theory `A_name_that_is_not_a_segment_reaches_its_own_instrument` gains `[InlineData("sampler_failed", "KioskSamplerFailures.Record")]`. The set-equality fact needs no edit — it goes red on a one-sided edit by itself. | Name and recorder absent. |
| `apps/shared/src/observability/kioskLatency.test.ts` | `sends a sampler_failed count above the duration ceiling`; `still refuses a non-finite sampler_failed`. | Ceiling drops 100000. |
| `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx` (where the sampler-failure tests live) | Decode sampler throws: POST `sampler_failed` with value 1 and 10, not 2; console line still emitted. Lag sampler throws: POST at 1, 10 and 100 (failures land at 4 s, 40 s, 400 s; the earlier "not at 10" assumed an 18 s gap and was wrong). | No POST today. |
| `e2e/kiosk-latency-contract.spec.ts` | Loop list gains `sampler_failed` → 202; refusal body contains `sampler_failed`; title "five" → "six". | Real kiosk token against the real server: 400. |

## Counterfactuals (prove each guard, phase 5)

1. Revert only the server arm → contract fact (client ⊄ server), characterisation
   fact (400 ≠ 202) and e2e go red.
2. Revert only the TS union entry → contract fact (server ⊄ client) and `tsc` on
   `CameraViewer.tsx` go red.
3. Route `sampler_failed` via `SegmentFor` → contract theory's "not `=> LatencySegment.`"
   check and the characterisation "nothing in segments" assertion go red.
4. Record `report.ElapsedMilliseconds` as the counter increment → the ignored-count
   theory goes red.
5. Restore the 60 s ceiling for every name → `kioskLatency.test.ts` ceiling test red.
6. Report *instead of* logging → existing spec-140 console tests go red.

## Risks

- Shared 30 s window between the two samplers on one tile (accepted, ADR-0180).
- The 400-body constant edit is the only assertion edit; any other characterisation
  edit is a block, not an adjustment.
