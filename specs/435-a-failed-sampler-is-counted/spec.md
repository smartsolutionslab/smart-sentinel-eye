# Feature Specification: A failed kiosk sampler is counted at the sink

**Feature Branch**: `fix/2313-sampler-failed`

**Created**: 2026-10-22

**Status**: Draft — phases 1-3, autonomous lane (ADR-0144).

**Issue**: #2313 (*A kiosk sampler that has stopped measuring should be visible at
the sink, not only in the tile's console*)

**ADRs**: **ADR-0180** (the binding decision: `sampler_failed`, its counter, one PR),
ADR-0122 (browser → service telemetry; amended by 0180), ADR-0118 (one sink),
ADR-0128 / ADR-0129 (the `wall_skew` / `label_delay` routing precedent), ADR-0117,
ADR-0139 / constitution §Testing (new behaviour starts red), ADR-0144 (the lane may
not edit an ADR), ADR-0052 / ADR-0053 (Shouldly, sentence-style names).

**Predecessors**: spec 140 (#2189 — the sampler failure is loud in the console),
spec 046 T012 (`KioskMeasurementContractTests`, the cross-language guard), spec 045
T018 (`e2e/kiosk-latency-contract.spec.ts`), spec 307 (`createReportThrottle`).

> Every construct is named, not line-numbered, for the reason spec 098 gives.

---

## 1. The binding decision

ADR-0180, Accepted 2026-10-22, points 1-7 and its Implementation Notes. This spec
implements it and does not reopen it. One name (`sampler_failed`) for both samplers;
the failure count travels in `ElapsedMilliseconds` and the server ignores it; a
counter `SmartSentinelEye.KioskSamplerFailures` tagged `camera`; kiosk and server in
one PR.

## 2. Premise, re-verified at `feb3c3c7` (origin/develop)

| Construct | Today |
|---|---|
| `KioskMeasurement` (`apps/shared/src/observability/kioskLatency.ts`) | Closed union of five names. No C# type of that name exists — the server's copy is string comparisons. ADR-0180's table holds. |
| `RecordKioskLatency` (`src/StreamDistribution/Api/StreamEndpoints.cs`) | Desktop caller → 202, dropped. Then `isWallSkew` / `isLabelDelay` (`report.Measurement == "…"`), `SegmentFor` switch (three segments); unknown → `UnknownMeasurement()` **400**. Then empty camera → 400, non-finite elapsed → 400. |
| Kiosk sampler failure | `CameraViewer.reportSamplerFailure` → `countReportableFailure` (decade cadence 1, 10, 100, …; two counters, per tile) → `logResilienceEvent('stream', 'decode-sampler-failed' \| 'lag-sampler-failed', …)` → `console.info` only. **Premise holds**: nothing reaches a meter. |
| Sampler cadence | Decode `DECODE_SAMPLE_INTERVAL_MS = 5_000`; lag `LAG_SAMPLE_INTERVAL_MS = 2_000`. |
| Client guards in `reportKioskLatency` | Drops non-finite, `< 0`, and **`> ABSURDLY_LONG_MS` (60 000)**; then `throttle(measurement)` (30 s per name per tile). |
| Cross-language guard | `tests/Architecture.Tests/KioskMeasurementContractTests.cs` parses the TS union and the server's `"x" => LatencySegment.` / `report.Measurement == "x"` spellings and asserts set equality; also asserts the refusal message names every server name, and a theory that non-segment names reach their own recorder. |
| Characterisation | `KioskLatencyRecordingCharacterisationTests` pins the full 400 body in `MeasurementRefusalBody`; `MeterObserver` listens to three meter names and **only `double` measurements**. |
| Contract docs | The doc on `KioskLatencyReport.Measurement`; the doc block above `KioskMeasurement`. No OpenAPI file or Shared.Contracts type lists the set. |

**Mixed-version behaviour** (why ADR-0180 point 4 is one PR): old kiosk → new server
is unaffected (it never sends the name). New kiosk → old server gets a 400 per
admitted failure report, swallowed by `reportKioskLatency` — the failure stays
console-only, i.e. today's behaviour, no regression but no gain. One PR makes the
second combination unreachable in this repo; deployment ordering is out of scope.

### Findings the ADR does not spell out (resolved here, within its decision)

- **F1 — the kiosk's own ceiling would silence the report at count 100 000.**
  `reportKioskLatency` drops any value `> 60 000`. A lag sampler failing every 2 s
  reaches count 100 000 after ~55 h; a 24/7 wall gets there. ADR-0180 point 7 makes
  the value a count, not a duration, and point 3 says the server ignores it, so the
  duration ceiling does not apply to it. **Resolution:** the 60 s ceiling (and the
  `< 0` floor) apply to duration names only; `sampler_failed` keeps the finiteness
  guard (the wire requires it). Not a new decision — the guard's stated reason
  ("a figure describing something other than a journey") does not describe a count.
- **F2 — the throttle drops the lag sampler's count 10.** Count 1 and count 10 of the
  lag sampler are 18 s apart, inside the 30 s window, so count 10 is not sent; count
  100 (~198 s) is. The decode sampler's count 10 (45 s later) is sent. ADR-0180 point
  5 accepts the throttle explicitly, so this is the specified behaviour, and the red
  tests assert it per sampler rather than the ADR note's "counts 1 and 10" literally.
- **F3 — the characterisation constant must move.** `UnknownMeasurement()` listing
  `sampler_failed` (ADR-0180 point 1) changes the 400 body that
  `MeasurementRefusalBody` pins. The ADR note says the existing facts stay unmodified;
  the facts do, the shared constant cannot. Editing it is the one sanctioned
  assertion change, and it is red first (the message test is in §5).

## 3. User story

### US1 (P1) — A tile whose sampler has stopped measuring shows at the sink

As an operator reading the Aspire dashboard, when a kiosk tile's decode or lag
sampler throws, I see `sse.kiosk.sampler_failures` increase for that tile's camera,
so a broken instrument no longer looks like a quiet healthy tile.

**Independent test**: boot the stack, open a kiosk wall, make a tile's
`getStats()` throw (or run the Vitest + Architecture tests below); the counter for
that camera is ≥ 1 under meter `SmartSentinelEye.KioskSamplerFailures`; no
`sse.latency.segment.duration` point is recorded for it.

**Acceptance scenarios**

```gherkin
Scenario: happy — a kiosk's failure report is counted
  Given a browser-kiosk caller
  When it POSTs {measurement: "sampler_failed", camera: C, elapsedMilliseconds: 10}
  Then the response is 202
  And the counter sse.kiosk.sampler_failures records 1 tagged camera=C
  And nothing is recorded under any latency segment, wall skew or label delay

Scenario: the count is ignored, not recorded
  Given a browser-kiosk caller
  When it POSTs sampler_failed with elapsedMilliseconds 100000 (or 0, or -5)
  Then the response is 202 and the counter adds exactly 1

Scenario: bad request — the guards still apply
  When a kiosk POSTs sampler_failed with an empty camera
  Then 400 with the camera message, and nothing is counted
  When it POSTs sampler_failed with a non-finite elapsedMilliseconds
  Then 400 with the elapsed message, and nothing is counted

Scenario: conflict of names — an unknown name lists the new one
  When a kiosk POSTs "sampler_broken"
  Then 400 and the message names 'sampler_failed' among the accepted values

Scenario: auth — a desktop's report is accepted and dropped
  Given a caller that is not a browser kiosk
  When it POSTs sampler_failed
  Then 202 and nothing is counted

Scenario: kiosk — a throwing sampler reports at the cadence it logs at
  Given a mounted CameraViewer whose decode sampler throws every tick
  Then sampler_failed is POSTed at count 1 and count 10, never at count 2,
       with elapsedMilliseconds equal to the count
  And the console line decode-sampler-failed is still emitted (alongside, not instead)
  Given the lag sampler throws every tick
  Then sampler_failed is POSTed at count 1, not at 10 (inside the 30 s window), and at 100

Scenario: kiosk — a long-failing sampler is not silenced by the duration ceiling
  When reportKioskLatency('sampler_failed', C, 100000, …) is called
  Then the POST is sent
```

**Unauthenticated**: unchanged — the route's existing authorization applies; no new
route, no gateway rule.

## 4. Locked choices

Minimal API endpoint unchanged (ADR-0070); wire record `KioskLatencyReport`
unchanged; `System.Diagnostics.Metrics` counter in `ServiceDefaults` beside
`WallSkew`/`LabelDelay`, registered in `Extensions.ConfigureOpenTelemetry`; no new
route, contract type, or client function.

## 5. Latency budget (constitution §IV)

**N/A for every leg.** The change is on the browser → service telemetry path
(ADR-0122), not the event → overlay path. In `apps/shared` it touches only
`reportSamplerFailure` (the sampler's `catch` branch, already off the render path)
and `reportKioskLatency`'s guard; `onLagMeasured`, alignment, decode sampling on
success, and composite/render are untouched. A healthy tile sends nothing new. No
§IV row changes (ADR-0180 Implementation Note 4).

## 6. Out of scope

Two names (`decode_sampler_failed` / `lag_sampler_failed`) — deferred by ADR-0180
point 6. A rate of failures (Negative consequence, accepted). Deployment ordering of
kiosk vs server images. Any ADR edit.
