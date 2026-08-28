# Specification Quality Checklist: A wall shows one instant

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-28
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

### Iteration 1 — two failures, one root cause

**"Success criteria are measurable" — FAIL.** SC-002 measured skew "within the
stated bound" while **no number was stated anywhere**. US1's central claim was
unfalsifiable: any skew passed. No defensible default existed — ADR-014's only
figure (`< 5 ms`) is explicitly an *inter-display* target and this spec's scope
is deliberately *intra-wall*, so borrowing it would have silently imported a
target set for different hardware. → raised as **Q1**.

**"Feature meets measurable outcomes" — FAIL**, consequentially: SC-001 and
SC-002 both rested on that same unstated bound.

**A second marker was raised in the same pass.** The edge case *"The slowest
tile sets the pace"* named a behaviour the requirements did not decide, and the
plausible answers produce visibly different walls and different budget
outcomes. FR-012 said an out-of-bound tile is *visible*; nothing said what the
**other tiles** do meanwhile. → raised as **Q2**.

### Iteration 2 — both resolved, all items pass

**Q1 → 33 ms, one frame interval at 30 Hz.** Recorded in FR-002 with its
reasoning, and made concrete in SC-002. The number is not new: 30 Hz is the
cadence floor ADR-0123 already requires of a kiosk, so the bound is the existing
figure read for this leg. FR-002 also records *why it is not `< 5 ms`*, so a
later reader does not "correct" it back to ADR-014's number.

**Q2 → hold, capped at the 200 ms budget.** Split into FR-012a (the wall waits
only as far as the budget allows, then releases and marks) and FR-012b (a
released tile keeps playing). SC-002a makes it measurable by inducing an outlier
and confirming the other tiles' latency does not rise with it. The edge case now
points at its resolution and names the boundary worth testing — a tile sitting
just either side of the cap, where the wall must not oscillate.

### On "No implementation details" — passed deliberately, with a note

The spec names MediaMTX, `CellPage`, `CameraViewer` and `performance.now()` in
its *Why this exists*, *Assumptions* and edge cases. This deviates from the
generic checklist and it is intentional: the repo's specs are written this way
(spec 043 names `CameraViewerPanel`; spec 044 names `CameraSimProvisioner` and
its FFmpeg command line), because the findings that justify a feature here are
findings *about the code*. The gap between ADR-014 and the built system — this
feature's central claim — cannot be stated without naming what is actually in
the media path.

The test applied instead: **the Requirements and Success Criteria sections
contain no mechanism.** FR-001 says tiles present against a common reference
without saying how one is obtained; Out of Scope defers the mechanism to Phase 2
and gates it on FR-014. That holds.

### One thing Phase 2 must not lose

**FR-014 is a gate, not a task.** ADR-014 cannot be implemented as written —
there is no StreamKeeper in the media path, no browser exposes a PTP time API,
and the PTP hardware it names as a deployment prerequisite is absent. The
amendment comes **before** a mechanism is chosen, or Phase 2 will have quietly
redesigned a Locked decision.

### Status

**All items pass.** Ready for `/speckit-plan`.
