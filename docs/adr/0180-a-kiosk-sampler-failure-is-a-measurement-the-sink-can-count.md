# ADR-0180: A kiosk sampler failure is a measurement the sink can count

**Status:** **Proposed** (the owner ruled on 2026-10-21 on #2313; this records the ruling)
**Date:** 2026-10-21
**Amends:** ADR-0122 (adds one value to the closed set of browser measurements it governs)
**Relates to:** ADR-0117, ADR-0118, ADR-0128, ADR-0129, ADR-0144, spec 040, spec 045, spec 046, spec 140, issues 1714, 2189, 2313

## Context

#2189 made a kiosk sampler that throws **loud**: `CameraViewer` counts failures per sampler and logs at
counts 1, 10, 100, ... through `logResilienceEvent('stream', 'decode-sampler-failed' | 'lag-sampler-failed', ...)`
(`apps/shared/src/ui/composites/CameraViewer.tsx:226-232`, `:278`, `:364`, `countReportableFailure` at
`:624`). That writes to the browser console and enters no sink.

So at the dashboard (ADR-0118's single sink), **a tile whose sampler has stopped measuring looks
identical to a healthy tile with nothing to report.** It reports nothing to
`POST /streams/kiosk-latency`, so nothing reaches the `SmartSentinelEye.Latency` histogram. Silence is
ambiguous in exactly the way #2189 fixed for the console.

**The names, verified** (the issue's premise said a grep for `KioskMeasurement` found nothing in C#;
that is correct, because the set is spelled differently on each side):

| Side | Where | What |
|---|---|---|
| Kiosk | `apps/shared/src/observability/kioskLatency.ts:48` | `type KioskMeasurement = 'overlay_draw' \| 'receive_to_decoded' \| 'presentation_buffer' \| 'wall_skew' \| 'label_delay'` |
| Server | `src/StreamDistribution/Api/StreamEndpoints.cs:158` | `RecordKioskLatency`: string comparisons for `wall_skew`/`label_delay`, `SegmentFor` switch (`:232-238`) for the three segments, `UnknownMeasurement()` 400 for anything else (`:223-230`) |
| Wire | `StreamEndpoints.KioskLatencyReport` | `(string Measurement, Guid Camera, double ElapsedMilliseconds)` |

The set is closed on both sides, and the kiosk's doc states the trap: `reportKioskLatency` swallows
failures by design, so **a name the server does not know yet produces a kiosk that reports nothing and
looks healthy** (spec 045 T014). Extending a browser-to-service telemetry contract is ADR-0122's ground,
and the autonomous lane may not amend an ADR (ADR-0144).

## Decision

**1. `sampler_failed` joins the closed set**, on both sides, in one change:

- `KioskMeasurement` gains `'sampler_failed'`.
- `RecordKioskLatency` gains its arm, routed to **its own instrument**, never to `LatencyBudget`. A
  failure is not a duration, and filing it under a segment would let a p99 move because an instrument
  broke. This follows the precedent of `wall_skew` (ADR-0128) and `label_delay` (ADR-0129), each routed
  away for its own stated reason.
- `UnknownMeasurement()`'s message lists the new name.

**2. The instrument is a counter**, `SmartSentinelEye.KioskSamplerFailures` in `ServiceDefaults` beside
`WallSkew` and `LabelDelay`, registered with the other meters in `ConfigureOpenTelemetry`, tagged with
the tile's `camera`. One accepted report adds one. A counter answers the question the issue asks ("is
this tile's instrument broken?"); a histogram of a non-duration would not.

**3. The guards of ADR-0122 still apply where they mean something.** The server keeps refusing an empty
camera and a non-finite `ElapsedMilliseconds`, so the wire record is unchanged. The `ElapsedMilliseconds`
value of a `sampler_failed` report is **not recorded** (what the kiosk sends in it is open, below). The browser-kiosk discriminator (`IsBrowserKiosk`) applies unchanged: a desktop's report is
accepted and dropped, as for every other name.

**4. Kiosk and server ship in the same PR.** That is the only ordering that cannot produce the silent
400 the set's own doc warns about. Neither half lands alone.

**5. The kiosk reports at the cadence it already logs at.** `sampler_failed` is sent exactly when
`countReportableFailure` admits a console line (counts 1, 10, 100, ...), and passes through the existing
`createReportThrottle` (30 s per name per tile). No new cadence is introduced.

Latency budget: **N/A for every leg.** Nothing on the event-to-overlay path changes; this adds one POST
at a decade-bounded rate per failing tile and none for a healthy one.

### Left open by the ruling, for the approver

The ruling names the value and the arm. Points 2, 3 and 5 above are this draft's proposals, and three
details have no answer in it:

- **Which sampler.** The kiosk has two (`decode-sampler-failed`, `lag-sampler-failed`), reporting
  different §IV legs, and #2189 kept their counters apart on purpose. One name loses which one failed.
  Options: two names (`decode_sampler_failed`, `lag_sampler_failed`), or one name with the instrument
  tagged by a sampler field the wire record does not have today. This draft assumes **one name, untagged**,
  as ruled; the approver may prefer two names.
- **What `ElapsedMilliseconds` carries.** The wire requires a finite number. This draft proposes the
  kiosk sends the failure count it already computed (`countReportableFailure`'s return value) and the
  server ignores it; sending `0` would also work but reads as a perfect score to anyone grepping logs.
- **The shared throttle.** `createReportThrottle` keys by name, so with one name a decode failure and a
  lag failure on the same tile share one 30 s window.

## Consequences

**Positive: "broken" and "nothing yet" stop looking the same at the sink.** A tile whose sampler is
failing shows a non-zero counter; a quiet healthy tile shows none. That closes part of why #1714's path
has never been read end to end.

**Positive: the contract grows by the mechanism it already has.** One name, one arm, one instrument,
the shape `wall_skew` and `label_delay` set.

**Negative: sparse.** Decade cadence means a tile failing since count 1000 next reports at 10000. The
counter says "this tile has failed", not how often. Accepted: rate is not the question.

**Negative: one more name both sides must keep in step**, with the same failure mode as every other
name if a later change edits one side only.

## Alternatives Considered

**A new StreamDistribution endpoint and counter** (the issue's second option). Separates the contract
cleanly but adds a route, a gateway rule and a second client function for one bit of information.
Not chosen by the owner.

**Record the failure under an existing segment** (a zero, or a sentinel duration). A zero reads as a
perfect score for a journey nobody timed, which ADR-0122 forbids in terms. Rejected.

**Leave it in the console.** The status quo #2313 exists to end.

## Implementation Notes

What the autonomous lane builds after approval (#2313 re-enrolled; behaviour-changing, phase 4a
**red**; one PR, frontend and backend together, so it is one slice with two engineers on disjoint files).

1. **Server**: `src/ServiceDefaults/KioskSamplerFailures.cs` (meter + counter, mirroring `WallSkew.cs`),
   registered where `WallSkew.MeterName` is; `RecordKioskLatency` routes `sampler_failed` to it before
   `SegmentFor`; `UnknownMeasurement()` lists it; the `KioskLatencyReport.Measurement` doc names it.
2. **Kiosk**: `KioskMeasurement` gains `'sampler_failed'`; `reportSamplerFailure` in `CameraViewer.tsx`
   calls `reportKioskLatency('sampler_failed', cameraIdentifier, <value>, getToken, reportThrottleRef.current)`
   alongside the existing `logResilienceEvent` (not instead of it).
3. **Red tests**: a new fact in `tests/Architecture.Tests/KioskLatencyRecordingCharacterisationTests.cs`
   (`sampler_failed` is 202, increments the counter, records nothing in `LatencyBudget`; its existing
   facts stay unmodified); kiosk Vitest, a throwing sampler POSTs `sampler_failed` at counts 1 and 10 and
   not at 2; `e2e/kiosk-latency-contract.spec.ts` (spec 045 T018, the only place a real kiosk token
   reaches the closed-set validation) gains `sampler_failed` and expects 202, which is the guard against
   a one-sided edit.
4. **Dashboard**: the counter is visible in the Aspire dashboard under its meter (ADR-0117 binds legs,
   not this; no §IV row changes).
