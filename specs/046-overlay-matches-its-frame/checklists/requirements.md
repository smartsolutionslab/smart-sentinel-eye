# Specification Quality Checklist: The overlay and the picture it annotates

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-29
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

### Iteration 1 — one failure

**"Scope is clearly bounded" — FAIL**, by design. FR-012 asked which of three
options the feature covers, and the answer changed whether this produced
documents or a mechanism. Not defaultable: one option meant **no code at all**.
→ **Q1**.

### Iteration 2 — resolved as option (c), full frame accuracy

**Chosen against the recommendation in the question, and the spec was rewritten
rather than patched.** Option (a) was recommended because the gap is ~30 ms,
below perceptual threshold, and the benefit could not be demonstrated to anyone.
That reasoning is preserved in the spec's own *"What cannot be demonstrated"*
section rather than deleted — a reader should be able to see the cost that was
accepted, not just the decision.

Sections rewritten for consistency, because a half-updated spec is worse than
one that never asked: US1 now asserts the pairing rather than offering "either
matching or not claiming it"; US2 measures the pairing rather than the gap;
FR-001 records the declined alternatives; the edge cases are re-derived for
matching (a frame already past, values faster than frames, PTP-stepped clocks
spanning two clock domains); and SC-001…SC-007 replaced criteria that only
suited the wording-fix option.

**FR-002 changed direction under this scope.** Under (a) or (b), *"frame-synced"*
had to come out of the SLO. Under (c) it **survives — but only once it is true**,
and the spec pins that to spec 045's precedent: the leg record must not report
delivery ahead of evidence.

### The risk this scope carries, recorded rather than smoothed

**FR-017 is a real blocker, not a formality.** Frame accuracy needs a per-frame
instant, and the obvious source is a component in the media path — which
ADR-0128 rejected as *"out of all proportion"*. That rejection stands. The spec
names one candidate route that avoids it (per-frame metadata carrying a capture
instant, if the sender's header extension survives the path) and states plainly
that **whether it survives is unknown and must be probed before any task list is
written**.

If it does not survive, the feature is blocked and must come back for a
decision. The spec forbids the tempting escape — shipping age-matching under the
name "frame accuracy" — because that would restate the exact overclaim the
feature exists to remove.

### On "No implementation details" — passed, consistent with the house style

The spec names the overlay's layering and the realtime message's field list, as
spec 045's did. The finding *is* about the code: "ADR-0021 cannot be built" is
unsupportable without showing `used_ts` is absent from the wire shape. The
Requirements and Success Criteria carry no mechanism — FR-014 says the kiosk must
establish a frame's instant, and deliberately does not say how.

### Status

**All items pass.** Ready for `/speckit-plan` — whose first act must be the probe,
not the plan.
