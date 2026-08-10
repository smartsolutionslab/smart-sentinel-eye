# Specification Quality Checklist: Fab-scope the camera catalogue

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-10
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

- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`

### Validation record

Two issues were found on the first pass and fixed rather than waved through:

1. **Implementation detail leaked into the requirements.** The original
   FR-007/FR-008 named the query parameter and the HTTP status codes directly,
   and FR-012 named `EventMetadata`. All were rewritten in behavioural terms —
   *"must require them to name one"*, *"must be refused"*, *"must carry the
   camera's fab"*. The mechanism is settled (ADR-0114, reused unchanged) and
   belongs in `plan.md`, not here.

2. **A success criterion was not verifiable.** *"Another plant's camera is not
   reachable"* cannot be tested without deciding what "not reachable" looks
   like on the wire — and the whole point of FR-006 is that it must be
   *indistinguishable* from a name that never existed. SC-003 now says
   compared field by field rather than by status alone, which is what spec
   014's equivalent test actually does.

**No [NEEDS CLARIFICATION] markers.** The two decisions that would have
warranted them — the scope boundary, and whether a camera's fab is assigned or
derived — were settled with the user before the spec was written and are
recorded in Assumptions.
