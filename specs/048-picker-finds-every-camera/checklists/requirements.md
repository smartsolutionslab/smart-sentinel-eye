# Specification Quality Checklist: The camera picker finds every camera

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-30
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

**Two rewrites were needed, both for the same fault — leaking the mechanism.**

The first draft named the query parameter, the hook, and the file and line of
the defect. Those belong in the plan; a spec that names them has decided the
shape before anyone weighed the options. Rewritten to describe what an operator
experiences: *a camera that exists is not offered, and nothing says so*.

The second draft still carried `limit: 50` and `MaximumLimit = 200` into the
requirements. The **facts** those numbers represent are load-bearing — a single
retrieval cannot return a whole fab, so "ask for all of them" is unavailable —
but the constants are not. FR-005 now states the constraint without the
identifier. The *why this is a correctness problem* preamble keeps the numbers,
because a reader needs 200 < 250 to understand why the obvious fix is wrong, and
that section is context rather than requirement.

**No [NEEDS CLARIFICATION] markers were raised**, and that is a deliberate call
worth recording. The one real open question — which of the three shapes to build
— is not a clarification, because all three satisfy US1 and US2 differs only in
reach. It is a planning decision with a cost table, and the spec presents it as
such rather than blocking on an answer it does not need to write the
requirements.

**Verified rather than assumed**, since a spec built on a misremembered number
would mis-scope the work:

- 250-camera production target — constitution line 365.
- The camera source refuses above 200 rather than trimming — the list handler
  returns a limit-exceeded failure.
- No name filter exists anywhere — the list query carries fabs, sort, order,
  offset, limit and a retired flag, and nothing else.
- The camera list page already pages correctly and reports its total — which is
  what makes this a defect in one consumer rather than a missing product
  capability.
