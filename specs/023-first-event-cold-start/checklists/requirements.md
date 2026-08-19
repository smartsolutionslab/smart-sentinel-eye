# Specification Quality Checklist: The first event after a restart reaches its effect in time

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

**All items pass.** Three judgements are worth surfacing to the reviewer rather
than leaving buried in Assumptions, because each of them bounds the feature:

**The deliverable is an explanation, not a smaller number.** US1 (P1) is the
attribution; US2 (the fix) is P2 and explicitly conditional on it. This inverts
the obvious ordering on purpose — a warm-up applied first would move the figure
without anyone being able to say what the seconds had been, and the number would
then be unfalsifiable. SC-001 puts a floor under it: the attribution must account
for at least 80% of the measured time, so "we looked and it's diffuse" does not
pass as an answer.

**SC-004 targets 1 second, not the constitution's 200 ms.** The 200 ms leg is a
steady-state budget. Holding the first-event-after-restart case to it would
likely force pre-warming every path in every service — a far larger change than
the current evidence justifies. One second is where a restart stops reading as a
stall. Called out because it is a deliberate relaxation of a constitutional
figure for one specific state, and a reviewer should get to disagree.

**"No production change" is an allowed outcome.** FR-010 and SC-004's second
clause make "understood, recorded, accepted" a pass. The feature's obligation is
that the number stops being unexplained, not that it gets smaller. This is stated
so that a later reader does not treat an unchanged figure as an unfinished
feature.

**One prior is recorded rather than assumed.** The rule cache is populated at
publish time and the measurement publishes its rule seconds beforehand, so it is
unlikely to be the culprit *in the measured scenario*. It is still subject to
FR-003 and must be confirmed or refuted — and a restart with pre-existing rules
is a genuinely different case.

**No [NEEDS CLARIFICATION] markers were needed.** The one place the feature could
have stalled — "how fast is fast enough for a first event?" — is answered as a
documented assumption with its reasoning exposed, which is more useful to a
reviewer than a question, and cheaper to overrule than to re-derive.
