# Specification Quality Checklist: An integration event is never lost after its write commits

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

**All items pass.** No clarifications were raised, and that is a deliberate call
rather than an empty section — the two questions worth asking both have answers
already recorded in the repository:

- **Which mechanism?** ADR-0088 already chose a durable outbox in the write's
  own database. Re-opening it here would be re-deciding a decision, so the spec
  assumes it and says explicitly that if the plan finds it cannot be applied as
  written, that is an ADR amendment and a gate rather than a quiet substitution.
- **At-least-once or exactly-once?** The consumers already deduplicate by
  identifier (spec 006 FR-002), so at-least-once is the target and the spec says
  so rather than leaving it to be discovered.

**One wording note for the planner.** The spec deliberately avoids the words
"outbox", "Wolverine", "RabbitMQ" and "transaction" in its requirements, using
"announcement" and "pending announcement" instead. That is not squeamishness: the
defect exists precisely because the mechanism was assumed to imply the guarantee,
and stating the guarantee in its own terms is what makes it possible to check
whether the mechanism actually delivers it. The mechanism belongs in plan.md.

**Scope risk to watch in planning.** FR-005 covers nine repositories across every
bounded context. This is the widest-reaching feature in the programme so far, and
the plan should say how it avoids becoming nine independent changes that drift —
the shared seam (`DomainEventDispatcher`, `IEventBus`) is the obvious answer and
should be confirmed rather than assumed.
