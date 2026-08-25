# Specification Quality Checklist: A row offers the actions its chain actually supports

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

Three validation calls worth recording.

**The spec names no file, component or state value**, though the research behind
it is entirely about specific ones. That was deliberate and it cost something:
FR-005 says discarding a draft needs "its own name" without saying what the name
is. The word belongs in the plan's contract document, where the other confirmation
wordings live, rather than in a spec a non-developer reads. If planning cannot
settle it, that is a gap to raise, not to guess at.

**"Reachable chain shape" is the load-bearing phrase** in SC-001 and FR-001, and
it is deliberately not enumerated in the spec. The enumeration is the plan's job,
and writing it here would have made the spec a description of the current code.
What the spec fixes is the *obligation*: every shape, demonstrated one at a time.
The defect being fixed is precisely a shape nobody enumerated, so an aggregate
demonstration would repeat the original mistake.

**SC-003 is phrased as "the revision it was applied to" rather than naming an
assertion style.** Acting on the wrong revision produces a successful request —
that is exactly why this shipped — so a criterion satisfied by "the operation
worked" would be satisfied by the bug. Kept technology-agnostic without losing
the sharpness.

Two facts were verified against the code rather than assumed, and are recorded so
a later reader does not take them for reasoning: the service accepts revert,
archive and branch on the affected chain today (the defect is entirely in what
the app offers), and the false confirmation predates the most recent wording
change rather than being introduced by it.
