# Specification Quality Checklist: Archiving asks before it happens

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

### The issue's central premise was wrong, and checking it changed the feature

Issue 1866 asked whether archiving might be *recoverable* — in which case the
inconsistency with cameras would be justified rather than accidental, and the
right answer might have been to do nothing.

Checking each aggregate's behaviours found that none of the four is recoverable,
and that two are **worse** than the camera retirement that prompted the issue:
archiving a layout's or overlay's published revision leaves it permanently
uneditable. That is filed as issue 1877, since fixing it is a domain decision —
but it is why FR-007 exists and why it forbids softening.

Had the premise held, this spec would have been much smaller or unnecessary.

### Every consequence in the spec was read, not assumed

This session has twice produced a claim of absence that did not survive
checking, so each consequence here was verified in code before being written
into a requirement:

| Requirement | Verified from |
|---|---|
| FR-005 (clone) | `Rule`'s own doc comment: *"clone the rule to author a new one"* |
| FR-006 (value cleared) | `Variable.Archive` sets `Value = VariableValue.Unset.Instance`; `SetValue` refuses when archived |
| FR-007 (never editable) | All six behaviours on `Layout`/`Overlay` enumerated; every guard rejects `Archived` |
| FR-008 (kiosks) | The kiosk's `onArchived` navigates away from the layout |

Nothing is claimed about rule *evaluation* stopping, because that was not
checked — the spec says only what the rule's own documentation states.

### FR-007 forbids a specific softening

*"MUST NOT be softened to 'cannot be undone', which is true of all four and
understates these two."*

Unusual for a requirement to name the wrong wording. It is here because *"this
cannot be undone"* is the sentence every confirmation reaches for, it is
perfectly true, and for a layout it omits the part that matters: the layout does
not merely stay archived, it becomes **permanently unusable**. An implementer
writing four confirmations in one sitting will converge on the shared phrasing
unless told not to.

### Why all four confirm

The tempting middle position was confirming only the two stranding cases. It was
rejected in Assumptions: a cloned rule and a redefined variable both cost an
identity and a history, and *"only somewhat irreversible"* is not a distinction
to encode in **whether** a question is asked. It is a good distinction to encode
in **what the question says**, which FR-005 through FR-008 do.

### Status

All items pass. No `[NEEDS CLARIFICATION]` markers were needed — the one genuinely
open question was whether archiving is recoverable, and it was answered by
reading the domain rather than by asking.

Ready for `/speckit-plan`.
