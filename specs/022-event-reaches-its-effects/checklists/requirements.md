# Specification Quality Checklist: A plant-floor event reaches the things it is supposed to drive

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-19
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

**All items pass.** No clarifications raised, deliberately: the shape of the
work is settled by the failure that prompted it, and the two questions a reader
might ask are answered in the spec rather than deferred.

**This spec is unusual and the deviation is intentional.** Its deliverable is a
test, not a behaviour — so it names no component, no message and no service
anywhere in its requirements, and calls the thing being proven "the journey".
That is not squeamishness about implementation detail. The gap exists *because*
each part was verified in the vocabulary of its own component and nobody
verified the sentence "an event changes what the operator sees". Stating it in
that vocabulary is what makes it possible to notice the sentence was never
checked.

**Two priorities are both P1.** US2 is not a refinement of US1. A test of this
journey that asserts an instruction was sent rather than an effect occurred
would have passed against the known failure — so a wrong test here is worse than
no test, because it reports the path as covered. That deserves equal weight, not
subordinate weight.

**The strongest requirement is SC-002.** "The test fails when the journey is
broken — verified by breaking it deliberately, not by assuming." Everything else
can be satisfied by a test that runs and goes green. Only that one distinguishes
a test from a ritual, and this feature exists precisely because a suite of
green tests was not watching.

**For the planner.** FR-010 is a constraint with teeth: if the on-screen
highlight proves unobservable without changing the product, that is a finding to
raise at the gate, not a small enabling change to slip in. The whole feature is
predicated on proving what is there.
