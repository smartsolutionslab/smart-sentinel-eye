# Specification Quality Checklist: An overlay label over live video, seen and timed

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-01
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

Three findings from validation, all resolved in the spec rather than left as
clarifications.

**1. The first draft named the mechanism, and the mechanism is Phase 2's.**
The input frames a real choice — reuse the existing simulated-camera container,
or serve a static looping path from the main media server. Both are container
and configuration decisions. The spec now states only that *a stream must
actually arrive*, which is the requirement; which server serves it is a plan
decision with the trade-off already recorded in the input.

**2. "Video is present" was not testable as first written, and that is the
whole defect.** The current fixtures pass while showing no video at all, so a
requirement satisfiable by a video element existing would have reproduced the
gap. FR-002 now requires decoding observed as **ongoing** — a still frame and a
live stream are different things, and only the second is the product.

**3. FR-004 and FR-005 were nearly one requirement, and would have been
weaker as one.** Failing when a half is missing (FR-004) and failing when the
label does not follow its variable (FR-005) are different failures: the first
catches a tile that never had video, the second a label that is right by
accident. A fixture can satisfy either alone.

**On the absence of clarification markers.** The one genuinely open question —
which video source — is explicitly framed in the input as having no strong view
and belongs to planning. Everything else was decided from the probe rather than
guessed, and the assumptions are recorded rather than buried.

**Deliberately not measurable, and said so.** SC-002 admits "or state that it
is not known and why" as success. That is not a weakened criterion: FR-009
makes an honestly-reported gap the required outcome when a common clock cannot
be established, because the alternative on offer is a fabricated number, and
this repository has already established that particular arithmetic to be
invalid.
