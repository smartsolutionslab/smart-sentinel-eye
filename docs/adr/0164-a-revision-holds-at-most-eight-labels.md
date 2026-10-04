# ADR-0164: A revision holds at most eight labels

**Status:** Accepted
**Date:** 2026-10-04
**Supersedes:** —
**Superseded by:** —

**Relates to:** ADR-0112 (multi-tile layouts — the precedent this mirrors,
§1 aggregate shape and §4 ceiling), ADR-0156 (raised the wall cap to 3×3 —
the tile count this reasons from), ADR-0123 (the composite-and-render
measurement), ADR-0104 (the revision payload is the part that legitimately
varies), constitution §IV (latency budget). Delivered by spec 150 (#2345).

**Drafting note.** Drafted on #2345 on 2026-09-14 as "ADR-0149" and adopted
by the product owner on 2026-10-04 with `MaxLabels = 8`. Between the draft and
its adoption, ADR-0149 was taken by another decision and ADR-0156 raised the
wall cap from 4 tiles to 9. The decision and its reasoning are the draft's.
Facts that had become false were corrected at commit time: the tile count, the
line reference, the worst-case arithmetic, what CI does with the leg, and the
newest figure for the leg. Nothing else was changed.

## Context

Spec 150 (#2345) extends an overlay revision to hold many labels rather than
one, published atomically. ADR-0112 §1's precedent settled the aggregate shape:
extend the aggregate's payload instead of using per-label chains, which that
ADR already rejected because they fracture the lifecycle.

What was not settled is **how many**. That number is not a spec detail, for
three reasons.

**It is a render-path cost on a leg that is already breaching.** ADR-0123
records a real-wall measurement of the composite-and-render leg at **p50
54.2 ms against constitution §IV's 50 ms budget**: p95 79.2 ms, max 164.6 ms
(#1891). The newest figures agree. Across 21 `develop` CI runs on the nine-tile
fixture, the mean p50 is **56.63 ms** (σ 8.55 ms; spec 225, `figures.md`,
2026-09-30). That was measured on CI's software rasteriser, not on kiosk
hardware. Every label on a revision is drawn per tile per frame, so a cap
chosen casually multiplies a leg that has no headroom left.

**A wall caps at nine tiles, not 250.** `GridDimensions.MaxTiles = 9`,
`MaxCells = 9` (3×3, ADR-0156, which amended ADR-0112's 4), enforced by the
check at `Layout.cs:80`. The relevant figure is therefore *9 tiles × N labels*,
not the 250 that appears in several issues and in ADR-0146 (the correction is
filed as #2363). That makes the cost about 28× smaller than the figure first
reasoned from, which argues for a *larger* N than fear would suggest. It is
also exactly why the number deserves stating rather than assuming. ADR-0156's
own 9-tile cap is not yet verified on real kiosk hardware (#2614). Until it
is, nine is a ceiling to design toward, not a measured fact.

**CI reports the leg but asserts no threshold**, and §VII's dashboard
obligation is still outstanding (#1940). Spec 225 put the render-leg record
into CI's summary step, but its baseline failed its own noise test, so no
threshold was committed. #2337 stays open until an ADR says what CI may
enforce on this leg. The instrument is `reportKioskLatency('overlay_draw', …)`,
and an e2e already reads it against a 50 ms budget, so a per-PR measurement
can discharge §IV. But today nothing would fail a build on a regression
introduced after this decision.

ADR-0112 set the precedent for exactly this kind of question, and set it in an
ADR rather than a spec:

> **Decision: v1 caps a wall at `MaxTiles = 4`** … This is a real NFR ceiling,
> not config. … **A larger wall (e.g. 3×3) is a future ADR** gated on a measured
> decode budget on real kiosk hardware, not a config knob.

ADR-0156 is that future ADR. A labels-per-revision cap is the same kind of
thing: a hard domain invariant, on the render path, that directly sets the
per-frame cost.

## Decision

**A revision holds at most `MaxLabels = 8` labels.** This is a domain
invariant enforced on the aggregate, in the same place and the same way as
`MaxTiles`. It is not a configuration value.

Eight, because:

- **It is the largest number that is obviously safe at the measured cost.** At
  9 tiles the worst case is 72 label draws per frame. The breaching p50 comes
  from frame cadence and compositing, not from label text: ADR-0123 defines the
  leg as the operator's wait, including the frame wait. Eight labels add little
  to a leg whose breach has other causes.
- **It is more than any authored overlay observed to date needs.** The editor
  is single-label today. The operator use cases that motivated #2345 (a title,
  a value, a unit, a timestamp, a status) sit comfortably under eight.
- **It leaves the ceiling visibly binding.** A cap of 64 would never be reached
  and so never tested. An operator can actually hit 8, so the refusal path gets
  exercised rather than staying theoretical.

**Raising it is a future ADR, gated on a measured overlay-draw figure on real
kiosk hardware, not a config knob.** This is ADR-0112's closing move, adopted
verbatim and for the same reason: a ceiling that can be raised by editing
configuration is not a ceiling.

## Consequences

- `#2348` (z-order) and `#2349` (non-text primitives) unblock. Both were
  waiting on this number, not on the aggregate shape.
- The domain refuses a ninth label with a named error, the way it refuses an
  out-of-bounds tile. The editor must show that refusal rather than silently
  dropping the label.
- **The cap is untested against the real leg until #2337 lands a render-budget
  gate.** Eight was chosen from the measured p50 and the corrected tile count,
  not from a measurement of eight labels specifically. That is a known gap, and
  it is the honest reason a raise is gated on measurement rather than argument.
- The reverse index is unaffected. Labels resolve and push as a set under one
  version bump, so SystemVariables keeps its `overlayIdentifier` key and only
  its cached value widens.

## Alternatives Considered

- **No cap.** Rejected: an unbounded payload on a breaching render leg that no
  CI gate watches. ADR-0112 rejected the same for tiles.
- **Config knob.** Rejected on ADR-0112's stated grounds: a ceiling that
  configuration can raise is not a ceiling, and the measurement that would
  justify a raise does not exist yet.
- **A larger cap (16, 32).** Defensible, and cheap to adopt later via the gated
  path. Rejected for now because nothing has measured it and no use case asks
  for it.
- **Deferring until #2337's render-budget gate exists.** Rejected because #2348
  and #2349 are blocked behind the number, and #2337 is not in flight. The gate
  would strengthen a future raise; a conservative first value does not need it.
