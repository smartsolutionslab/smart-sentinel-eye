# Specification Quality Checklist: An event is never accepted until it is stored

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-18
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

**All items pass.** Three things about this spec are worth recording rather
than leaving to be re-derived.

**The mechanism was decided before drafting**, by the user on 2026-08-18, from
four options: stop acknowledging early; retry then dead-letter; a durable
buffer at ingress; dead-letter only. The chosen answer was **stop acknowledging
early** — each ingress uses its own mechanism to promise only what is true.
The spec deliberately does not name that mechanism; it says "the system MUST
NOT report an event as accepted before it is stored" and lets `plan.md` bind it.
Recorded here so the plan does not reopen a settled question.

**Story 3 exists because Story 1's mechanism creates it.** Keeping an event
until it is stored is what recovers an outage, and it is also what turns one
permanently-bad event into an endless retry that blocks everything behind it —
the exact defect spec 018 fixed when an escaping exception took the service
down. It is a guard on Story 1, not a separate journey, which is why it carries
the same priority and cannot be deferred past it.

**Two requirements are here to stop the change being declared done too early.**
FR-010 and FR-013 are not features; they are the things this change is most
likely to break quietly. Throughput was sized for sustained bursts, and the
obvious implementation — wait for each event to be stored before confirming it
— would turn ingest into one round trip per message. The backpressure answer
today is a "too many requests" response tied to a buffer that direct
submissions will no longer use; leaving its replacement unstated would let it
disappear by accident.
