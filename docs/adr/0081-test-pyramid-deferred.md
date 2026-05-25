# ADR-0081: Test Pyramid Shape — Deferred Until Real Coverage Data

**Status:** Accepted
**Date:** 2026-05-25

## Context

Round 5c asked which test pyramid shape to target (classic 70/20/10,
trophy, diamond, honeycomb). The right answer depends on actual
defect-discovery rates per layer, which we cannot know until features
land.

## Decision

**No fixed pyramid ratio committed.** The pyramid emerges from real
coverage data as features ship.

What IS committed and constrains the shape:

- **Bottom** — per-layer coverage gates (ADR-0065): Domain ≥ 90%,
  Application ≥ 80%, Shared ≥ 90%.
- **Top** — Playwright E2E (ADR-0052) for critical operator flows.
- **Middle** — integration tests via Aspire fixture (ADR-0068),
  required on every code PR per ADR-0033.

**Revisit** after the walking skeleton + the first two features
ship. Measure: where are defects actually discovered? Which layer's
tests catch them? Adjust gates accordingly.

## Consequences

- **Positive:** avoids premature optimization based on textbook
  ratios.
- **Positive:** evidence-driven adjustment.
- **Negative:** no quantitative ratio target right now — reviewers
  rely on the per-layer gates and judgement.

## Alternatives Considered

- **Classic 70/20/10** — textbook; ignores that our integration tests
  are cheap.
- **Trophy 60/30/10** — defensible; commits to a shape we may revise.
- **Diamond / Honeycomb** — integration-heavy; defensible; same
  caveat.
