# Specification Quality Checklist: A name is mutable exactly when it is not an address

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

### The premise in #1850 was checked, and it does not hold

#1850 argues that because no aggregate supports renaming, names are immutable by
convention, and that adding rename to `Camera` would therefore break a pattern.

The addressing evidence undercuts that. Two aggregates are addressed **by name**
(`Rule`, `Variable`) and three **by identifier** (`Camera`, `Layout`,
`Overlay`). For the first two a rename is an identity change; for the last three
it is an ordinary attribute edit. The absence of any rename anywhere is
explained by the hard cases, not by a decision about the easy ones.

Recorded here rather than silently acted on: the spec adopts a different
rationale from the issue that prompted it, and a reader comparing the two should
find the reason rather than a contradiction.

### Why `Variable` is called out separately (FR-003)

It is the only name in the product that is **both** an address and a stored
cross-context reference: `RuleAction.SetVariableValue` carries a `VariableName`
string, persisted with the rule and read at evaluation. ADR-0016 forbids a
project reference across that boundary, so nothing could detect a break — a
renamed variable would leave rules that silently stop firing, with no error
raised anywhere.

That is a stronger exclusion than `Rule`'s, where the only casualty of a rename
would be a saved URL, and the spec distinguishes them rather than lumping both
under "name-addressed".

### Three requirements that exist because a naive implementation satisfies the rest

- **FR-007** — both layers. Spec 028 found the storage constraint and the
  application-level check disagreeing about this exact rule, and a rename is its
  third caller. A test that exercises only one layer passes while the other is
  wrong, which is precisely what happened before.
- **FR-008** — a name collision must not be reportable as a lost update. Both
  are conflicts; ADR-0119 makes the code, not the status, the thing a caller
  keys on. If they become interchangeable a caller retries a rename that will
  never succeed.
- **FR-011** — the freed name. It currently falls out of a storage constraint's
  shape rather than from a decision. Spec 028's research made exactly this
  mistake in the opposite direction, so this spec requires the behaviour be
  chosen and tested rather than observed.

### On the outcome not being assumed

The spec was written prepared to conclude that `Camera` is **not** renameable
and to deliver an ADR alone, with #1850 closed as *answered, not built*. It
concludes otherwise because the evidence points that way, and the Assumptions
section records that the alternative was live rather than rhetorical.

### Status

All items pass. No `[NEEDS CLARIFICATION]` markers were needed: the one genuinely
open question — whether names are mutable — is what the feature exists to settle,
and the addressing evidence answers it without a judgement call being handed back
to the user.

Ready for `/speckit-plan`.
