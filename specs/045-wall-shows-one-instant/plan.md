# Implementation Plan: A wall shows one instant

**Branch**: `045-wall-shows-one-instant` | **Date**: 2026-08-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/045-wall-shows-one-instant/spec.md`

## Summary

Build the last unbuilt leg of the 800 ms budget: the presentation buffer.

Every tile on a kiosk wall runs its own jitter buffer and settles wherever it
likes, so tiles show different instants and nothing says so. The fix is a
**per-wall controller**: measure each tile's lag from receiver statistics,
compute one target the whole wall can hold, and push each tile's
`jitterBufferTarget` to it — capped at the leg's 200 ms, because alignment is
paid for in latency out of the very budget this leg belongs to.

**The mechanism was measured before it was planned** ([research.md](./research.md)).
The obvious primitive — `estimatedPlayoutTimestamp` — does not exist in
Chromium. What does exist is a computable lag, a working actuator, and a shared
sender clock that needs no PTP. Alignment converged 9.410 ms → 4.683 ms on a
real MediaMTX.

**Phase 1 is not code.** ADR-014 cannot be implemented as written, and FR-014
gates every mechanism decision behind an ADR that says what replaces it.

## Technical Context

**Language/Version**: TypeScript 5.x (React 19, Vite) for the kiosk; C# / .NET 10 for the report intake

**Primary Dependencies**: WebRTC (`RTCRtpReceiver.jitterBufferTarget`, `getStats`), existing `WhepClient`; MediaMTX as SFU (unmodified)

**Storage**: none — no new persistence. Measurements go to the OpenTelemetry meter via `LatencyBudget`

**Testing**: Vitest (pure arithmetic + hooks), xUnit + Shouldly (endpoint), Playwright (induced-skew convergence), plus a manual walk

**Target Platform**: evergreen Chromium kiosk; `HeadlessChrome/151` used for the probe

**Project Type**: web — React frontends (`apps/`) + .NET bounded contexts (`src/`)

**Performance Goals**: inter-tile skew ≤ 33 ms (FR-002); this leg ≤ 200 ms (FR-005); end-to-end still ≤ 800 ms (FR-006)

**Constraints**: no new observability sink (ADR-0118); no telemetry SDK in the kiosk bundle (ADR-0122); coordination must never stop video (FR-013); a single-tile wall pays nothing (FR-004); management-web unaffected

**Scale/Scope**: a wall is up to 16 tiles today; 250-camera target across the fab

## Constitution Check

*GATE: checked before Phase 0 and re-checked after design.*

| Principle | Status | Note |
|---|---|---|
| §IV latency budget is sacred | ⚠️ **This feature spends from it** | The leg adds delay by design. Capped at 200 ms (FR-005) and end-to-end re-measured (FR-006, SC-003). R4 measured lag doubling — this is the risk, and the cap is the control. |
| §IV leg table kept current | ⚠️ **Must change** | FR-010/FR-011 — needs the ADR first. Governance forbids editing §IV without one. |
| §VII dashboard for implemented legs | ⚠️ **Obligation attaches on ship** | US2 exists precisely so the leg is not built-and-unmeasured. |
| §VIII trust boundaries | ✅ | Report intake already validates untrusted browser input; new names extend the same closed set. |
| ADR-0118 one sink per environment | ✅ | No new sink. |
| ADR-0122 browser measurement via a service | ✅ | Reuses `POST stream-distribution/streams/kiosk-latency`. |
| ADR-0084 code metrics | ✅ | 300 LOC/file is **S104, C# only**; frontend has only max-lines-per-function (50). See research D6. |
| Karpathy: smallest change | ✅ | One new hook, one pure module, one receiver setter, two contract names. |
| Karpathy: no speculative generality | ✅ | No strategy interface, no pluggable clock source. One controller, one actuator. |
| **ADR-014 (Locked)** | ❌ **Cannot be implemented as written** | **Resolved by Phase 1's ADR, which gates everything else.** |

**Gate result: PASS, conditional.** The one hard violation is ADR-014, and the
plan's first phase is the amendment that resolves it. No mechanism is committed
to before it lands.

## Project Structure

### Documentation (this feature)

```text
specs/045-wall-shows-one-instant/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0 — the probe and what it settled
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1 — the induced-skew walk
├── contracts/
│   └── kiosk-latency-report.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks — not created here
```

### Source Code

```text
docs/adr/
  0128-playout-alignment-without-ptp.md      NEW — amends ADR-014 (gate)

.specify/memory/constitution.md              §IV leg table: two rows

apps/shared/src/
  streaming/WhepClient.ts                    + video receiver accessor
  ui/composites/useWhepSession.ts            + setPlayoutTarget passthrough
  ui/composites/CameraViewer.tsx             + optional playout target prop
  observability/
    wallAlignment.ts                         NEW — pure arithmetic, no React
    wallAlignment.test.ts                    NEW
    kioskLatency.ts                          + two measurement names

apps/kiosk-web/src/features/cell/
  useWallAlignment.ts                        NEW — the per-wall control loop
  useWallAlignment.test.ts                   NEW
  CellPage.tsx                               mounts the hook; renders the badge
  TileAlignmentBadge.tsx                     NEW — US3's visible failure

src/StreamDistribution/Api/StreamEndpoints.cs   + two names in the closed set
src/ServiceDefaults/LatencyBudget.cs            + PresentationBuffer segment
src/ServiceDefaults/WallSkew.cs                 NEW — skew is not a duration (D2)

tests/StreamDistribution.Tests/                 endpoint accepts/refuses names
tests/e2e/                                      induced-skew convergence
```

**Structure decision**: the arithmetic is pure and lives in `apps/shared` with no
React, so it is testable without a browser. The control loop lives in
`apps/kiosk-web` because **only a wall has more than one tile** —
`management-web` mounts the same `CameraViewer` and must be untouched
(research D5).

## Phases

Ordered so US1, US2 and US3 stay independently shippable. **Phase 1 gates all
of them.**

### Phase 1 — The ADR (FR-014). No code.

Nothing else starts until this lands.

Write `docs/adr/0128-playout-alignment-without-ptp.md`, amending row 014 of
`docs/adr/0000-initial-decisions.md`. It must settle:

1. **What replaces "StreamKeeper emits presentation timestamps"** — nothing we
   own is in the media path; MediaMTX is unmodified third-party. The replacement
   is receiver-side alignment against the SFU's RTCP sender clock (research R5).
2. **The "< 5 ms inter-display" target** — out of scope, and why: it needs the
   grandmaster and PTP-aware switches ADR-014 itself names as a deployment
   prerequisite, none of which exist here. It stays ADR-014's target for a later
   feature on a fab network.
3. **Constitution §Frontend's "PTP-aware time APIs required"** — no browser
   exposes any. The requirement is unsatisfiable as written and must be corrected
   or removed.
4. **The leg's name** (research D3) — §IV calls it *"Presentation buffer (PTP)"*
   and the mechanism uses no PTP. Rename it or justify the name. A leg named
   after a technology it does not use is the same defect spec 040 found.

**Exit**: ADR accepted; §IV's row updated to match. Only then does Phase 2 start.

### Phase 2 — Measure a tile's lag (US1 groundwork, and US2's instrument)

The observable before the actuator, so the controller is never flying blind.

- `wallAlignment.ts` — pure: a lag sample from an `RTCStatsReport`, and the
  per-frame delta between two samples. Follows `decodeElapsedBetween`'s idiom
  exactly, including returning `null` rather than `0` (research R3).
- Unit tests against fabricated stat reports — no browser needed.

**Exit**: each tile's lag is computable and tested.

### Phase 3 — Align the wall (US1)

- `WhepClient` exposes the video receiver; `useWhepSession` passes a
  `setPlayoutTarget` through. Mirrors how spec 040 exposed `stats()` — the
  smallest possible seam, session lifecycle untouched.
- `useWallAlignment` — the control loop: sample every tile, compute
  `target = min(max(lag), 200 ms)`, apply. A tile needing more than the cap is
  **released and marked**, never held (FR-012a); a released tile keeps playing
  (FR-012b).
- **Single-tile short circuit**: with fewer than two tiles the hook does nothing
  and sets nothing (FR-004).
- Every failure path is a no-op: if statistics are unreadable or the receiver
  rejects a target, video continues (FR-013).

**Exit**: US1 shippable — a wall coordinates, and an induced spread converges.

### Phase 4 — The leg gets a number (US2)

- Contract: add `presentation_buffer` and `wall_skew` to `KioskLatencyReport`'s
  closed set. **Client and server in one commit** — the server 400s an unknown
  name, so a split lands a kiosk posting into validation errors (research D1).
- `LatencySegment.PresentationBuffer` — 200 ms budget, `isWholeLeg: true`: the
  kiosk controls both ends of the delay it adds.
- **Skew gets its own instrument, not a latency segment** (research D2) — a
  spread is not a journey, and naming it one is the mislabelling this repo keeps
  catching.
- §IV: this leg's row becomes true; the decode leg's *"in part"* is revisited.
  Expected outcome is **restated, not raised** (research D4) — RTT/2 plus buffer
  plus decode is an estimate, and rounding an estimate up to *measured whole* is
  what §IV's wording forbids.

**Exit**: US2 shippable — both figures readable in the sink, per tile.

### Phase 5 — Say when it cannot hold (US3)

- `TileAlignmentBadge` — a tile released past the cap is visibly marked.
- The released condition is recorded for an engineer as well as shown.
- **Hysteresis**: a tile sitting either side of the 200 ms cap must not
  oscillate between held and released (spec edge case).

**Exit**: US3 shippable.

### Phase 6 — Verify, and be honest about it (FR-015)

Per [quickstart.md](./quickstart.md).

**The trap this phase exists to avoid**: on an idle box with identical sources
the spread was **already 9.4 ms before any of this was built** (research R6). A
green run proves nothing unless the skew was *induced*. Every check here starts
by creating a spread and asserts on convergence from it.

The verification note records which claims were demonstrated on this hardware and
which were not verifiable here — inter-display skew being the obvious one, since
that hardware does not exist.

## Complexity Tracking

| Deviation | Why | Simpler alternative rejected because |
|---|---|---|
| Amending a **Locked** ADR | ADR-014 names a component not in the media path and a browser API that does not exist | Implementing "as written" is impossible; implementing something else without amending would silently redesign a locked decision — the failure the spec was written to prevent |
| A control loop in the kiosk | Only the wall sees all tiles | Per-tile self-alignment has no reference to align *to*; a fixed buffer depth ignores the spread it is meant to close |
| A second instrument for skew | Skew is a spread, not a duration | Recording it as a `LatencySegment` puts a number under a name that means something else |

## Risks

1. **The suite is green because the box is quiet.** The likeliest way this ships
   broken (research R6). Mitigated only by induced skew — and that mitigation is
   itself a judgement, so it is called out in quickstart rather than assumed.
2. **Alignment eats the budget.** R4 measured lag doubling. The 200 ms cap and
   SC-003's end-to-end re-measurement are the controls; without them this leg
   fixes a wall and breaks the SLO.
3. **The setpoint gets reported instead of the achievement.** `jitterBufferTarget`
   is write-only in `getStats`. Reporting it would produce a perfect,
   meaningless number — FR-007 exists for this.
4. **The 33 ms bound is unfalsifiable on this hardware** unless skew is induced,
   for the same reason as risk 1.
