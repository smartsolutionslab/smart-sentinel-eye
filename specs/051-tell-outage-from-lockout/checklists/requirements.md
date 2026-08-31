# Specification Quality Checklist: A dark wall says which failure it is, and comes back on its own when it can

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

### The premise was checked before the spec was written, and it was wrong

The originating issue says two failures render as one merged screen. Induced
against a running provider — container stopped, account disabled — they render as
**three different screens**, and the real defect is the opposite of the one
filed: the *recoverable* failure is the one that never retries. Measured at 90
seconds of healthy provider with the wall still dark.

The spec is written against the observation, and the issue is corrected in the
open rather than the requirement being reinterpreted to fit.

### Two [NEEDS CLARIFICATION] markers were considered and resolved instead

- **What the terminal screen says** — resolved as requirements on *properties*
  (no credential prompt, states it is unauthorized, diagnostic detail available
  but not the headline) rather than fixed wording. Wording is a plan-phase
  choice; the properties are what a test can assert.
- **The retry bound and what happens at it** — FR-006 requires the plan to state
  and justify it. Deliberately left to the plan because it is a judgement about a
  fab's operations, and the spec can bound it without picking the number.

### One requirement exists only because of an observation

FR-007. In the terminal case the application has already redirected and the
provider's login form is what renders, so the kiosk cannot put words on a screen
it is no longer showing. Any plan that assumes it can will not survive contact
with the flow.

### Deliberate asymmetry, stated rather than assumed

FR-005 defaults unrecognised causes to **recoverable**, with the reasoning inline:
a pointless retry costs one screen; a wrongly-terminal classification costs a
whole wall through an outage it would have survived.

### What this spec does not claim

It does not close constitution §Availability. The ten-hour session ceiling
(issue 1989, blocked on issue 1992) still drops a screen roughly twice a day, and
this feature does not touch it.
