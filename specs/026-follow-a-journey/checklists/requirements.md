# Specification Quality Checklist: A cross-service journey can be followed end to end

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-22 · **Re-run**: 2026-08-22 against the rewritten spec
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

**All items pass — and they passed for the first version too, which is the most
useful thing this checklist has to say.**

### What this checklist did not catch, and could not

The first spec asserted three things about mechanisms nobody had run: that
parentage inflates percentiles, that the outbox cannot store causal context, and
that the outbox was where the chain broke. All three were false. Every item above
was legitimately ticked at the time, because **each requirement genuinely was
testable and unambiguous — it was just aimed at the wrong thing.**

That is not a defect in the checklist; it is its boundary. "Testable" is a
property of a sentence. "True" is a property of the world, and nothing on this
list asks about the world.

**The practical consequence, worth carrying to the next spec**: a spec that
argues from a mechanism should name the experiment that would falsify it, and
Phase 0 should run that experiment before the plan is written. In this feature
each falsifying experiment took minutes; the second and third were only run
because the first had already found something.

### Four judgements a reviewer should push back on if they disagree

**FR-006 and SC-005 exist because the cheap fix would look like it worked.**
Ingestion batches up to 200 deliveries. One cause per batch is less code and
produces a joined trace — which would satisfy US1 by eye and quietly destroy
US2, since two hundred unrelated events would share a parent. Made a
first-class requirement rather than a note, because this is precisely the class
of thing this programme keeps shipping.

**FR-007 forbids duplicating what already works.** The library propagates the
relationship correctly across services and through the outbox; this feature
supplies the one input it lacks. Written as a requirement because the first plan
was about to add a header to every message in the system for no measured benefit.

**US3 is now a regression guard rather than an open risk**, and it stays P1
anyway. It has been observed to hold on the exact mechanism being used — the
joined trace reports 4305 ms while its spans report 42, 0, 58 and 1. Keeping it
costs one assertion; dropping it would mean nothing notices if that stops being
true.

**SC-001 and SC-007 still require a person, not a test.** A test asserting a
relationship exists in memory satisfies neither. Spec 024 registered a trace
source and could not confirm spans arrived for two days.

### On the rewrite itself

The spec keeps a "What the first version got wrong" section rather than being
silently corrected. A spec that reads as though it were always right teaches
nothing, and the next person to argue from a schema instead of an experiment
should be able to see what that cost here.
