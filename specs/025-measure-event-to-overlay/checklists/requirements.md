# Specification Quality Checklist: The event-to-overlay leg can be measured

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-22
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

**All items pass.** Four things a reviewer should push back on if they disagree:

**US2 is P1, not a refinement of US1.** An instrument reporting a plausible
number for the wrong span cannot detect its own error, and someone will cite it.
Spec 022's journey test already measures the same thing from outside, so
agreement is checkable — SC-003 sets the tolerance at 20%, loose enough to
absorb clock and sampling differences and tight enough that measuring a fragment
would fail it.

**FR-005 and FR-006 exist because the failure modes are silent.** A missing
acceptance moment recorded as zero is a perfect score for a journey nobody timed;
a negative duration from a PTP-stepped clock is a sub-zero latency. Both would
make a dashboard look better than reality, which is the direction of error this
programme has been caught by four times.

**One event with two effects is recorded twice.** Averaging them would hide a
slow arrival behind a fast one, and each application is a separate arrival at a
screen. The alternative is defensible, so it is an assumption rather than a
silent choice.

**The cold path stays in the distribution.** #1655's twelve-second first event
will be visible, which will make the p99 ugly. Excluding it would make the
dashboard flatter and less true — the feature's whole purpose is that the budget
stops being asserted.

**No dashboard here.** §VII asks for measurement *and* a dashboard, and this
delivers the first. That is not the spec dodging its obligation: where dashboards
live is ADR-0026's unmade decision (#1707), and building one now would settle a
Locked ADR by implementation. SC-008 requires the §IV table to say so plainly, so
the remaining half stays visible rather than looking finished.
