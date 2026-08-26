# Specification Quality Checklist: An operator can watch a camera

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-26
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
- [x] User scenarios cover the strategic flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

### Why this one ticks the boxes spec 042 deliberately did not

Spec 042 left four items unticked, because its entire subject was a JSON file and
a JWT claim and abstracting them made it harder to check. **This subject is
genuinely user-facing**: an operator opens a camera and sees what it sees. That
describes in user terms without loss, so it does — the file names, component
names and browser-storage keys stay in the plan.

The one place the temptation was strongest is the "Why this exists" section,
where the defect really is *a component nothing imports*. It is phrased as *the
viewer exists and nothing renders it*, which is the same fact in the reader's
vocabulary and loses nothing a reviewer needs.

### Four validation calls worth recording

**The defect is stated as a user's loss, not a code fact.** *"An operator who has
just corrected a camera's address has no way to check whether the picture came
back"* — a 24/7 fab console that manages cameras and cannot show one. Leading
with "the component is unmounted" would have made the fix sound like wiring, and
invited a fix that wires it up without asking where it belongs.

**US3 makes the test's shape a requirement.** The current state is three passing
unit tests on a component no operator can reach — which is indistinguishable from
working to anyone grepping for coverage. FR-006 says rendering the component in
isolation does not count as reaching it, and SC-003 demands that be demonstrated
by causing the failure.

**FR-007 exists because there are two ways to be invisible.** The viewer was
mounted nowhere *and* wired to a credential nobody issues. Fixing only the mount
would produce a viewer that fails identically to no viewer — and would pass any
check that asks whether something rendered. The second failure needs its own
assertion or the first fix looks complete.

**US2 defends a rule the page already follows.** A retired camera offers no
rename, no address correction and no retirement, and states the refusal rather
than letting an operator discover it on submit. A viewer that could only report
failure would be the one control that breaks that pattern. FR-004 makes the
absence deliberate and explained rather than merely absent.

### Verified before writing

The component's isolation was searched for twice, the placeholder credential
getter was read, the three unit tests were read, spec 002's original scenario was
read, and the camera page's existing retired-camera behaviour was read. The
viewer's own working state is not assumed — it was observed carrying real video
on a kiosk wall this week.
