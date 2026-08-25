# Specification Quality Checklist: Rename a camera from the management app

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

### The issue overstated the work, twice, and the spec says so

Issue 1873 claimed the operator-facing wording for a taken name was new, and
floated a new shared refusal predicate as an open question. Both were checked:

- The overlay editor already refuses a taken name in exactly the right words,
  keyed on the code rather than the status.
- The camera dialog's refusal banner already falls back to the server's own
  explanation for refusals it does not recognise — deliberately — and the
  server's explanation for a taken name is actionable and leaks nothing.

The spec records the corrected framing rather than quietly building less than
the issue described, so a reader comparing the two finds the reason.

**This is the second claim of absence in this session that did not survive
checking** — the first produced a filed issue that had to be retracted. The
pattern is stopping at a near-miss: finding a related thing, reasoning correctly
about what it does *not* cover, and treating that as evidence the uncovered half
is missing.

### FR-008 is written as a question, deliberately

*"Satisfying this MUST NOT require a new shared refusal predicate unless the
spec's assumption proves wrong; a test MUST establish which."*

That is unusual for a requirement, and it is the honest shape. The assumption is
well-evidenced but not verified end to end — nobody has yet rendered a
`CAMERA_NAME_TAKEN` refusal in that dialog. Writing it as *"no new predicate"*
would be a guess presented as a decision; writing it as *"find out"* keeps the
default and names what would overturn it.

### Two requirements that a working implementation could still get wrong

- **FR-006 / FR-007** — a taken name and a stale version are both conflicts, and
  the existing shared helper correctly returns false for the first. If the
  dialog's branching is written carelessly, a taken name inherits *"someone else
  changed this, reload to see their version"* — wrong in both halves, and it
  sends the operator to reload something that will not change.
- **FR-010** — the typed name must reach the server unaltered. A case-only
  correction is a real change that normalises identically, and spec 033 found
  that exact trap in three separate layers. A client that helpfully normalises
  before sending would make it a fourth, and the symptom would be a rename that
  silently does nothing.

### On the success message, and not applying a rule without its reason

Spec 032 forbade announcing a successful retirement because retiring is
idempotent — the app cannot know whether *this* operator caused it. A rename is
version-checked, so that reasoning does not transfer.

The spec still requires no announcement, for a different reason: the changed
name on the page already says everything a message would. Recorded explicitly so
the two are not collapsed into one rule that would be right by accident here and
wrong somewhere else.

### Status

All items pass. No `[NEEDS CLARIFICATION]` markers were needed — the one genuinely
open question is whether the existing refusal handling suffices, and that is
better answered by a test in Phase 4 than by a question now.

Ready for `/speckit-plan`.
