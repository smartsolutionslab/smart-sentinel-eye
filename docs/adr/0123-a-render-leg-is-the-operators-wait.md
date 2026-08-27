# ADR-0123: A render leg is the operator's wait, and a wait has a floor

**Status:** **Accepted**
**Date:** 2026-08-27
**Supersedes:** —
**Superseded by:** —

## Context

ADR-0015 budgets **overlay composite + render at ≤ 50 ms**. Spec 040 built the
instrument that measures it, and the first figures read off a running wall came
in over budget: p50 **54.2 ms**, p95 79.2 ms, max 164.6 ms (#1891).

The obvious reading was that compositing is slow. It is not, and the instrument
explains why. `measureOverlayDraw` starts a clock at the overlay's state change
and stops it inside a second chained `requestAnimationFrame`:

```ts
const startedAt = performance.now();
requestAnimationFrame(() => {
  requestAnimationFrame(() => {
    reportKioskLatency('overlay_draw', camera, performance.now() - startedAt, getToken);
  });
});
```

Three things are inside that elapsed time: the **wait until the next frame
boundary** (0 to one frame interval), then **one whole frame interval** while the
first frame paints, then the **drawing work**. Only the third is compositing.

So the figure has a floor set by the display, not the code. The wall those
numbers came from was running at **27 Hz** — a 37.0 ms frame — which puts the
floor between 37.0 ms and 74.1 ms before any drawing happens at all. A p50 of
54.2 ms sits in the middle of it.

That left a question the measurement cannot answer, because it is a question
about meaning rather than about milliseconds: **does this leg's 50 ms bound the
compositing work, or the operator's wait?** Both readings are defensible and they
imply different instruments, different budgets, and different owners for the
problem.

## Decision

**The leg is the operator's wait: from the overlay's state changing to the
operator being able to see it, frame wait included.**

An operator cannot see a change before the frame that carries it. Time spent
waiting for that frame is time the fab floor spends looking at stale information,
which is the thing the budget exists to bound. A number that excluded it would
describe how fast the code ran while saying nothing about how late the operator
was.

Three things follow, and each is part of the decision.

**1. The instrument is correct as built and is not to be "fixed".** Starting the
clock at the state change and stopping after the paint is the leg. #1891 was
opened on the suspicion that `overlay_draw` reports frame cadence rather than
compositing; the resolution is that it reports both, and the leg is both.

**2. The budget therefore implies a minimum cadence.** With the wait in scope,
elapsed ≈ *(0…T) + T + work*, so the display sets a floor the code cannot get
under:

| Cadence | Frame *T* | Median ≈ 1.5 *T* | Tail ≈ 2 *T* | Against ≤ 50 ms |
|---|---|---|---|---|
| 24 Hz | 41.7 ms | 62.5 ms | 83.3 ms | median and tail breach |
| 27 Hz | 37.0 ms | 55.6 ms | 74.1 ms | median and tail breach |
| 30 Hz | 33.3 ms | 50.0 ms | 66.7 ms | median exactly at budget; tail breaches |
| **40 Hz** | 25.0 ms | 37.5 ms | **50.0 ms** | **both hold** |
| 60 Hz | 16.7 ms | 25.0 ms | 33.3 ms | comfortable |

**A kiosk must sustain ≥ 30 Hz for the median to hold and ≥ 40 Hz for the tail.**
This is now a stated requirement on the kiosk and its hardware rather than an
unexamined consequence of a number chosen elsewhere.

**3. A breach of this leg is read as cadence first, compositing second.** The
floor is larger than the budget below 30 Hz, so a figure over 50 ms says nothing
about the drawing code until the cadence is known to be sufficient. Whoever
investigates one reads the frame rate before reading the profiler.

### What this does not decide

**Whether 50 ms is the right number.** Nothing here argues it is wrong. It says
27 Hz cannot meet it, which is a statement about the wall. If the target hardware
cannot sustain 40 Hz with a full wall of decodes, then the budget and the
hardware disagree and that is a product decision — recorded then, not now.

**Whether the observed 27 Hz is representative.** It was measured on a developer
machine running four 1280×720 H.264 decodes and Playwright in one browser. A fab
kiosk shows one wall and does nothing else. The cadence that set that floor is
plausibly an artefact of the capture, and re-reading it on representative
hardware is the open work.

## Consequences

**Positive:**

- The leg means one thing, written down, so the next reader of a breach is not
  re-deriving it from the instrument's source as #1891 had to.
- The minimum cadence is stated. It was always implied by the budget; now it can
  be designed for, and tested against, rather than discovered.
- The instrument is settled, so spec 040's figures stay comparable across the
  change instead of being reset by a new definition.
- It generalises: the presentation-buffer leg (#1714, unbuilt) is also a wait an
  operator experiences, and will inherit this reading rather than reopening it.

**Negative:**

- **The budget is unachievable on a slow wall by definition**, and no amount of
  engineering in the compositing path can rescue it below 30 Hz. That is honest
  rather than convenient, and it converts a vague performance worry into a
  hardware requirement someone has to meet.
- **Measurements now include something the code does not control.** A kiosk on a
  weak GPU reports a breach caused by its display, not its software. Mitigated by
  the per-camera dimension (#1931) and by reading cadence first, but the figure is
  genuinely not a pure measure of this system's work.
- **§IV's table and ADR-0015's wording predate this** and describe the leg without
  saying the wait is in scope. They are accurate but incomplete, and should carry
  this reading when either is next amended.

## References

- ADR-0015 — the latency budget and its legs
- ADR-0122 — a browser measurement enters observability through a service
- Constitution §IV (leg states), §VII (dashboards bind implemented legs)
- #1891 — the over-budget figures that prompted this, and the arithmetic behind the table
- #1931 — the per-camera dimension, without which a wall reports one blended histogram
- #1714 — the unbuilt presentation buffer, the other leg this reading will bind
