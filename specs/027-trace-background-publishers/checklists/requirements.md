# Specification Quality Checklist: Every journey has a beginning, not just the ones from the plant floor

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

**All items pass.** But this checklist passed spec 026's first version too, and
that spec turned out to rest on three false premises. So the useful entry here is
not the ticks.

### What makes this one different, and what still could be wrong

**The premise is a survey, not an argument.** Spec 026's failures all came from
reasoning about a mechanism without running it — arguing from a column list, from
how percentiles "must" work, from where the break "must" be. This spec's central
claim is an enumeration of every publish call site in the codebase, which is
checkable by anyone in a minute and either complete or not.

**What could still be wrong, and Phase 0 should check rather than assume:**

- The survey classifies nine publishers as "fine" because a request or a message
  establishes their cause. That is inferred from spec 026's measurement of the
  *ingestion* path, not observed for each of the nine. **The claim that an HTTP
  handler inherits the request's cause should be seen once**, not reasoned from.
- Whether the health watcher genuinely publishes only on change, which FR-006 and
  SC-005 depend on.

Both are minutes of work and neither is assumed by the requirements below in a
way that would silently mislead.

### Four judgements a reviewer should push back on if they disagree

**FR-003 and SC-003 restate spec 026's FR-006 for new loops, deliberately.**
Reusing a requirement verbatim reads like padding. It is here because the trap is
the same and the temptation is *stronger*: both call sites are loops with the
item right there, and one journey per sweep is fewer lines than one per camera.
Spec 026 shipped that trap's fix only because the requirement was written down
before the code.

**FR-004 exists because the defect it prevents was shipped once already**, in
spec 026, and caught in code review rather than by any test. New code that omits
it would be a regression against a fix that is three commits old.

**US2 is P2 but not droppable.** Audit retention is background housekeeping and
genuinely less urgent than a camera dropping out. It is in scope because it is
the call site most likely to be skipped — it publishes inline rather than through
a domain event handler, so pattern-matching on spec 026's change misses it — and
because a feature that closes one of two known gaps while reading as complete is
worse than one that says which half it did.

**FR-009 and SC-008 ask for a complete written survey, not a fix.** They are
satisfiable by a table. That is the point: the expensive part of this feature was
finding out where the orphans are, and leaving that undocumented would mean the
next person redoes it. What they deliberately do *not* do is make the property
enforceable — see "Out of scope".

**No [NEEDS CLARIFICATION] markers were needed.** The one open question — whether
a future background publisher should be prevented from shipping as an orphan
rather than merely counted — is written into Out of scope as a real and separate
question, because answering it here would turn a two-call-site change into an
architecture-test change.
