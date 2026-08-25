# Specification Quality Checklist: A fab identifier can be sorted, in every context that has one

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-25
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

Three validation calls worth recording, because this spec is unusual.

**The "user" here is a developer, and the spec says so plainly rather than
inventing an operator.** The feature changes nothing an operator can reach — the
deployed listing has always sorted correctly. Dressing a test-seam defect up in
operator language would have made the spec less true, not more business-focused.
The business value is real and stated: half an hour lost per encounter, paid
repeatedly, for a trap whose failure message points at nothing useful.

**"All eight, not the one that needs it" is the spec's weakest point and it is
argued rather than asserted.** Seven contexts gain an ability no current caller
uses, which is ordinarily the definition of building for a need that does not
exist. The Assumptions section makes the counter-argument explicitly — this is a
gap between copies that are meant to be identical, not a new abstraction — and
names the alternative it rejects. A reader who disagrees has something concrete
to disagree with. The plan's constitution check must confront the same tension
rather than waving at this section.

**SC-002 is phrased as "demonstrated by the order itself".** An ordering that
treats every fab as equal raises no error and satisfies any criterion about the
call succeeding — while leaving exactly the paging defect the tie-break exists to
prevent. Kept technology-agnostic without losing that sharpness.

Two things were verified against the code rather than assumed, and both corrected
the issue this spec is written from: there is **one** workaround comment in the
tests, not two; and a **third** fab-ordering call site exists that the issue does
not mention, which is what establishes that the codebase already has two idioms
and that only the one inside a translated query is forced to change.

The central failure was **reproduced**, not inferred, before the spec was written.
