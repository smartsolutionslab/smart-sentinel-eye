# Specification Quality Checklist: Fab-scope system variables

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-05
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

Three decisions were settled before drafting rather than left as
`[NEEDS CLARIFICATION]`, because each would have changed the shape of the
spec rather than a detail within it:

1. **Strict ownership** — a variable belongs to one fab, and a cross-fab
   reference is a misconfiguration rather than a capability. The alternative
   considered was keeping variables global and scoping only the write, which
   would have left two fabs sharing one row and one value; it does not fix the
   defect.
2. **Refusal at the point the value is applied**, not at authoring. Checking at
   authoring time would need one bounded context to call another synchronously,
   which the constitution forbids.
3. **The overlay resolution path is in scope.** Excluding it would leave stored
   values correct and screens still wrong.

Two items to watch when this reaches planning, both recorded here because they
are the places a passing spec can still produce a broken feature:

- **SC-005 is the one that needs a real measurement**, not an assertion. The
  resolution path sits inside the event-to-overlay budget, and "no measurable
  regression" means comparing against the existing figure the same way it was
  originally taken — not re-deriving a new baseline after the change.
- **FR-005, FR-006 and SC-006 exist because of how #1252 hid.** A dropped
  value-change that logs nothing is indistinguishable from a rule that
  correctly did not match. Whatever form the record takes, planning must not
  quietly reduce it to a debug-level line nobody reads.

One deliberate omission: the spec does not say what a *rule author* sees when
they name a variable in another fab, because they see nothing — the refusal
happens later, in a different context. That is a consequence of decision 2 and
is called out in Assumptions rather than hidden.
