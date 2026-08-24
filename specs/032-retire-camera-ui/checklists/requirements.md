# Specification Quality Checklist: Retire a camera from the management app

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-24
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

### Three decisions the description asked to be settled explicitly

1. **Absent, not disabled** (FR-004). Follows spec 030 FR-007's precedent for
   the address-correction control, and is asserted as absence rather than as a
   failed submission — a disabled control still tells the operator the action
   is conceptually available, which for a terminal state is untrue.

2. **Stay on the page** (FR-009, FR-011). Retiring does not navigate away. The
   operator asked to change *this* camera and should see the result on it;
   navigating to a listing where it has just vanished shows them an absence and
   asks them to infer success. FR-010 still requires it to be gone from the
   listing when they next go there.

3. **No expected-version precondition** (FR-016). Confirmed against the
   endpoint's declared contract, not assumed — it advertises 204/400/403/404
   and declares no 409, 412 or 428.

### Two requirements that exist because a naive implementation passes without them

- **FR-012** — the success wording. Retirement answers success whether or not
  this operator caused it, so any message claiming "you retired this camera" is
  a claim the app cannot support. A test asserting "a success message appeared"
  stays green while the message says something untrue.
- **FR-013** — non-enumeration. Inherited from spec 029 FR-006 and spec 030
  FR-008, and restated rather than referenced because it regresses by someone
  adding a *helpful* message. Verified by comparing renderings field for field
  (SC-004), not by observing that both cases showed an error.

### On the terminology mismatch, deliberately not resolved here

The API and domain call the retired state `Decommissioned`; the operator-facing
word throughout spec 028, spec 030 and this spec is **retired**. That split is
pre-existing and load-bearing in shipped code. Renaming either side is not this
feature's business, and doing it quietly inside a UI spec is how a rename
escapes review.

### Status

All items pass. No `[NEEDS CLARIFICATION]` markers were needed: the description
named the three open decisions and supplied the precedent for each, and the one
fact worth verifying rather than guessing — whether retire takes `If-Match` —
was checkable against the endpoint's declared contract.

Ready for `/speckit-plan`.
