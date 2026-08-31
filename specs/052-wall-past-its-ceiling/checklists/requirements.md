# Specification Quality Checklist: A wall stays up past its own session ceiling

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-31
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

### The unresolved question in the brief was resolved before writing

The brief flagged the authority needed to contain the privilege as unverified and
asked for it to be checked rather than assumed. It was, against a running
provider, one permission at a time:

| Authority granted to a test principal | Composite edit |
|---|---|
| none | refused |
| everything the identity service holds today | **refused** |
| view-realm | refused |
| **manage-realm** | **allowed** |

That result is load-bearing, so it is in the spec body rather than a footnote:
**the containment needs more authority than the thing it contains.** FR-004 and
US1's fourth scenario exist because of it, and the plan cannot dodge it.

### Why US1 gates US2 rather than following it

The feature widens who may hold the longest-lived credential in the system.
Spec 049 refused it on exactly that ground. Spec 050 built the widening and
described the containment without having it — the containment was true of a file
and false of every running system. Ordering US1 first is the only structural
difference between this attempt and that one.

### One story is expected to remain unmet, deliberately

US4 — twenty screens and a real power cut — is named as a story precisely so it
cannot be absorbed by the others. Four screens were exercised once, in spec 051;
twenty never have been, and a power cut never has. FR-013 and SC-008 forbid the
record from claiming the constitution's target.

### Two [NEEDS CLARIFICATION] markers were considered and resolved instead

- **Where the containment runs.** Left to the plan: it depends on the authority
  trade, and the spec bounds it with FR-004 (narrowest sufficient) rather than
  picking a home.
- **Whether a wall-display grant should be narrowed or its exposure accepted.**
  Resolved as FR-009 and FR-012 — enumerate everything and state the exposure
  honestly. Which of the two is chosen is a plan decision; *not knowing* is what
  the previous attempt shipped.

### What this spec does not claim

It does not close §Availability, and says so in the requirements rather than
only in prose.
