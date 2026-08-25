# Specification Quality Checklist: Losing the uniqueness race is a refusal, not a fault

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
- [x] Scope is clearly bounded
- [x] Edge cases are identified
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

### The central question, and why it resolved without asking

The issue framed this as an open choice: one generic refusal for all twelve
constraints, or a per-constraint mapping that names the domain concept.

Checking the codebase decided it. **Every context with a user-facing uniqueness
rule already refuses duplicates with its own specific code** —
`CAMERA_NAME_TAKEN`, `RULE_NAME_TAKEN`, `VARIABLE_NAME_TAKEN`,
`LAYOUT_NAME_TAKEN`, `OVERLAY_NAME_TAKEN`, `WEBHOOK_CLIENT_ALREADY_EXISTS`.
Those fire on every ordinary duplicate. The storage layer speaks only when one
of them was told the name was free and lost the race in between.

So a per-constraint mapping would restate seven messages that already exist, in
a place that would have to learn nine contexts' vocabulary, maintained across
twelve constraints — to improve wording on a path that fires in a race window.

It is recorded in **Assumptions** rather than stated as fact, because it is the
spec's central judgement and the one most worth overturning if the reasoning is
wrong.

### Why FR-009 exists

*"Every existing application-level uniqueness check MUST remain."*

That looks like a requirement to change nothing, which is a strange thing to
write down. It is here because the obvious next thought after adding a
storage-level backstop is that the checks are now redundant — and acting on that
would replace seven specific messages with one generic one on **every**
duplicate, not just the raced one.

It is also the exact reasoning that produced spec 028's defect: a uniqueness
rule enforced in the index and not in the repository, on the assumption that one
layer covered it. SC-005 tests FR-009 by requiring those contexts' existing
tests to pass **unchanged**.

### Two requirements that a passing implementation could still get wrong

- **FR-004 / FR-005** — the refusal must not be, or resemble, a lost update.
  Both are conflicts and the nearest existing refusal is the stale-version one.
  If they become confusable, a caller re-reads and retries forever against a
  name that belongs to someone else. The distinction is asserted, not assumed.
- **FR-008** — non-enumeration. Several contexts deliberately answer as though a
  resource in another fab does not exist. A uniqueness refusal that reported a
  collision with such a resource would undo that quietly.

### On the testing section being in the spec at all

It is unusual for a spec to describe its own evidence. It is here because the
honest answer — *the reachability test may not exercise the path on a given
run* — is the kind of thing that gets discovered at implementation and then
quietly resolved by writing a test that forces the race and flakes.

Stating it up front makes the trade explicit: the test can fail to add
information, but it cannot produce a false green. Given that a flaky test in
this repository has already cost a merge, that is the right side to err on.

### Status

All items pass. No `[NEEDS CLARIFICATION]` markers were needed — the one genuinely
open question was answered by evidence rather than by preference, and the answer
is recorded where it can be challenged.

Ready for `/speckit-plan`.
