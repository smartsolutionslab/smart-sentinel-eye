# Implementation Plan: The overlay and the picture it annotates

**Branch**: `046-overlay-matches-its-frame` | **Date**: 2026-08-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/046-overlay-matches-its-frame/spec.md`

## Summary

Two parts, deliberately separable.

**Part 1 stops the system claiming a synchronisation it does not perform.** §IV
promises *frame-synced*; nothing is. That is corrected, with an ADR amending
ADR-0021, and the record gains the reason frame accuracy is not done so the next
reader does not repeat the probe.

**Part 2 holds each label back by its own tile's measured frame age**, so a label
and the picture beneath it describe roughly the same moment — and keep doing so
as spec 045's alignment adds buffer.

**Phase 0 already ran and changed the feature.** The original scope was full
frame accuracy; [research.md](./research.md) established it cannot be built here
and the scope was returned for a decision, per FR-017. That is why this plan
starts at an ADR rather than at a probe.

## Technical Context

**Language/Version**: TypeScript 5.x (React 19, Vite) for the kiosk; C# / .NET 10 for the report intake

**Primary Dependencies**: spec 045's `wallAlignment` per-tile frame age; the existing kiosk-latency report path

**Storage**: none — no persistence, no migration

**Testing**: Vitest (pure timing arithmetic, hook behaviour), xUnit (report intake), Playwright (induced-buffer walk)

**Target Platform**: evergreen Chromium kiosk

**Performance Goals**: the applied delay is bounded and counted inside the 800 ms budget (FR-009, FR-010)

**Constraints**: no media-path component (FR-017); no new observability sink; failure must not stop video *or* overlays (FR-014)

**Scale/Scope**: a wall of up to 16 tiles

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| §IV latency budget is sacred | ⚠️ **Part 2 spends from it** | A held label is a later label. Bounded (FR-009) and counted (FR-010), and Part 1 is unaffected. |
| §IV record must be true | ✅ **This is the point** | Part 1 exists to make §IV match behaviour. |
| §VII dashboards | ✅ | FR-015/016 measure the applied delay through the existing service path. |
| ADR-0118 one sink | ✅ | No new sink. |
| ADR-0122 browser measurement via a service | ✅ | Reused unchanged. |
| ADR-0128 no media-path ownership | ✅ **Preserved** | FR-017. The probe's finding is what makes this a constraint rather than a preference. |
| **ADR-0021 (Locked)** | ❌ **Unimplementable as written** | Resolved by Phase 1's ADR, which gates all code. |
| Karpathy: smallest change | ✅ | One delay in the overlay path, derived from a figure that already exists. |
| Karpathy: no speculative generality | ✅ | No configurable strategy, no per-overlay policy. One rule. |

**Gate result: PASS, conditional** on Phase 1's ADR landing before any mechanism.

## Project Structure

```text
docs/adr/
  0129-labels-are-aged-not-frame-matched.md    NEW — amends ADR-0021 (gate)

.specify/memory/constitution.md                §IV wording; ADR-0015 leg text

apps/shared/src/observability/
  labelDelay.ts                                NEW — pure: delay from a frame age
  labelDelay.test.ts                           NEW
  kioskLatency.ts                              + one measurement name

apps/kiosk-web/src/features/cell/
  useLabelDelay.ts                             NEW — holds a label back
  useLabelDelay.test.ts                        NEW
  CellPage.tsx                                 applies the delay to tile labels

src/StreamDistribution/Api/StreamEndpoints.cs  + the name in the closed set
src/ServiceDefaults/LatencyBudget.cs           + a segment, or its own instrument

tests/Architecture.Tests/                      the §IV wording guard
```

**Structure decision**: the arithmetic is pure and lives in `apps/shared` so it
is testable without a browser. The mechanism lives in `kiosk-web` beside
`useWallAlignment`, because it consumes that hook's per-tile figure and because
`management-web` has no wall and must be untouched — the same split spec 045
used, for the same reason.

## Phases

### Phase 1 — The ADR and the record (US1, Part 1). Ships alone.

- Write `docs/adr/0129-labels-are-aged-not-frame-matched.md`, amending ADR-0021.
  It must state: that ADR-0021 cannot be implemented as written; the **three**
  blockers from research.md, including the one that survives fixing the others;
  that age-matching is adopted and is **not** frame accuracy; and that
  ADR-0128's media-path rejection is preserved rather than revisited.
- Correct §IV's *frame-synced* wording and ADR-0015's leg description.
- Add the guard: an architecture test asserting the corrected wording, mirroring
  `LatencyLegRecordTests`. **That file caught spec 045 changing §IV** and is the
  reason this correction cannot quietly drift back.

**Exit**: the record is true, and Part 2 is optional. **If the feature stops
here, it has delivered something worth having.**

### Phase 2 — The delay, as arithmetic

- `labelDelay.ts` — pure: given a tile's frame age, the delay to apply; bounded
  per FR-009; **null rather than zero** when the age is unreadable, following the
  house rule that a zero reads as a perfect score for something nobody measured.
- Unit tests, including the bound and the unreadable case.

### Phase 3 — Hold the label (US2, Part 2)

- `useLabelDelay.ts` — schedules the label change on **monotonic time**
  (`performance.now()`), never epoch time: fab clocks are PTP-stepped and
  `CellPage` already carries that reasoning for its highlight timers.
- Ordering is preserved and nothing is dropped (FR-012); the existing monotonic
  version guard on overlay text is the model.
- `CellPage` passes each tile its own delay, sourced from `useWallAlignment`.
- Every failure path shows the label immediately (FR-011, FR-014).

### Phase 4 — Measure what was applied

- One measurement name through the existing report path. **Client and server in
  one commit** — the endpoint validates against a closed set and 400s anything
  else, while the reporter swallows failures, so a split reports nothing while
  looking healthy. Spec 045 documented that trap.
- **The achieved delay, not the intended one** (FR-015).
- Decide, and record, whether this is a `LatencySegment` or its own instrument.
  It is a duration, so a segment is defensible — but it is not one of ADR-0015's
  six legs, and spec 045's `WallSkew` exists precisely because filing a quantity
  under a name that means something else is how this codebase gets caught.

### Phase 5 — Verify by inducing, not observing

- Induce buffer on a tile and confirm its label delay follows.
- Confirm a tile with no overlay is untouched, asserted as **no timer scheduled**
  rather than as unchanged latency — spec 045's review found the weaker assertion
  passes against a component doing nothing.
- Confirm the end-to-end budget still holds with the delay counted in.
- Record what could not be verified: **nobody can see this**, so there is no
  human confirmation step, and the verification note must say so rather than
  implying a walk was equivalent to one.

## Complexity Tracking

| Deviation | Why | Simpler alternative rejected because |
|---|---|---|
| Amending a **Locked** ADR | ADR-0021 needs two things that do not exist | Implementing as written is impossible; implementing something else without amending is the silent redesign spec 045 was written to avoid |
| Delaying a label at all | Removes an offset that spec 045 widens | Doing nothing leaves §IV's claim false — but note Part 1 alone fixes *that*, which is why Part 2 must justify itself separately |

## Risks

1. **Part 2 gets built and Part 1 gets forgotten.** The record correction is the
   certain benefit and the mechanism is the interesting work. Phase 1 is first
   and ships alone specifically to stop that ordering inverting.
2. **The delay is named "frame sync" somewhere.** In a metric, a comment, a
   variable. FR-008 forbids it; a reviewer should grep for it.
3. **The mechanism is unfalsifiable.** Nobody can see 30 ms, so a test that
   passively observes a small difference proves nothing. Every check induces
   buffer first (SC-004) — the same discipline spec 045 needed and nearly missed.
4. **A held label makes an alarm late.** The spec flags this and does not settle
   it. If a safety-relevant overlay exists, this needs deciding before Part 2
   ships rather than after.
