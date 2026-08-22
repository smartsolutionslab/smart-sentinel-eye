# Specification Quality Checklist: A cross-service journey can be followed end to end

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-22
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

**All items pass.** Four judgements a reviewer should push back on if they
disagree:

**US3 is P1 alongside US1.** Whatever joins the journey up must not make a
delay look like work, because replacing a missing answer with a confident wrong
one is worse than today and this codebase has been caught five times by things
that looked like success. So the constraint is a first-class story rather than a
caveat.

> **Amended 2026-08-22 after Phase 0.** This note originally argued the point by
> claiming a continuation would report a twenty-millisecond journey as eight
> minutes "in every percentile it appears in". **That is not how span duration
> works** — a span measures its own start to its own end, and percentiles are
> computed over spans. The checklist passed it anyway, which is worth recording:
> **a quality gate confirmed a claim nobody had checked against the mechanism.**
> The item below — "requirements are testable" — was and is satisfied; the
> reasoning around it was not.

**The link-not-continuation reading was an assumption, and Phase 0 reopened
it.** The spec said the plan should test it, and testing it found the argument
for it did not hold. Parentage may now be free — Wolverine already carries the
fields and builds its receive span from them — so the plan tries that before
writing custom span code. What still has to be checked either way is whether the
result is followable in the sink this project has: spec 024 registered a trace
source and could not confirm spans arrived for two days, and FR-008 and SC-007
exist so that cannot repeat.

**SC-003 is phrased as "no reported duration grows".** Not "durations are
accurate", which is unfalsifiable, and not "spans are linked", which is the
mechanism rather than the outcome. If any measured span gets longer because of
this feature, it has misrepresented a delay as work and failed.

**SC-001 and SC-007 both require a person, not a test.** "Someone who did not
build the system can name the originating event" and "followable by someone
reading it" are deliberately about a human using the sink. A test asserting a
link exists in memory would satisfy neither, and would be exactly the kind of
green result this programme has learned to distrust.

**No [NEEDS CLARIFICATION] markers were needed.** The one genuinely open
question — whether linking works and is usable — is written as an assumption the
plan must test, because a spec that stalls on it would block work that has to
happen either way: the context still has to reach the far side, whatever
relationship is then recorded.
