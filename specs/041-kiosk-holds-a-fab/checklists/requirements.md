# Specification Quality Checklist: The kiosk holds a fab, and holds only what a kiosk needs

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

Four validation calls worth recording. This was an unusually hard spec to keep
implementation-free, because the whole subject is a configuration value.

**No client id, scope name, file or protocol appears anywhere in the spec.** The
finding is a single identifier in a single file, and naming it would have been
the shortest possible spec — and a change request rather than a specification.
The vocabulary used instead ("identity", "plant claim", "permission set") is the
same distinction a non-technical reader needs to judge whether the change is
right: *which identity, carrying what, doing what*. The identifiers belong in the
plan.

**US2 is not a consequence of US1 and the spec insists on that.** The tempting
shape was one story — "the kiosk works again" — with least privilege as a
pleasant side effect. Separated because they can be satisfied independently and
the wrong one alone is a bad outcome: a kiosk that works while holding
administrative authority closes a defect and keeps a weakness, on the least
physically secure surface in the product. **SC-002** exists so that cannot be
rounded away.

**FR-007 and US3 make the test a first-class requirement.** This defect has
existed since the kiosk did, and survived only because a check accepted the error
as a pass. A spec that fixed the kiosk and left the check would be fixing the
instance and preserving the mechanism — and the mechanism is what will produce
the next one. SC-004 demands it be demonstrated by *causing* the failure, not by
reasoning that it would occur.

**FR-005 pre-commits the answer to the most likely bad outcome.** If some kiosk
call turns out not to be covered by the narrowed permissions, the path of least
resistance is to widen the set and move on — which would quietly undo US2 while
all the tests stayed green. The requirement says that is a finding: either the
action does not belong on a kiosk, or the set is wrong, and both deserve saying
out loud.

Everything the spec asserts was verified against a running stack before it was
written: the 403 was reproduced, the two identities were read side by side, and
the permission sets were compared against the canonical one.
