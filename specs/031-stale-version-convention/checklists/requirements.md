# Specification Quality Checklist: One way to say a version is stale

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

**16 of 16 pass.** Two things about this spec are unusual enough to justify
rather than assert.

### It is a correction, and says so

Nothing an operator can *do* changes. The value is that an existing mechanism
stops giving one context's users advice that destroys their work. Written as
user stories anyway, because the outcome is entirely operator-facing — what they
are told — even though no capability is added.

The temptation with a change like this is to write it as a refactor: *"unify the
stale-conflict predicate."* That would have made SC-002 unwriteable, because the
measurable outcome is a sentence an operator reads.

### The decision it settles, and the trade it refuses

**Code authoritative, not status.** The shared helper already says this in a
comment and only half-does it. Both statuses are overloaded in both directions,
so neither can carry the meaning.

**The one outlier conforms, not the sixteen.**

| | Sites | Status |
|---|---|---|
| Six aggregates, `*_STALE` | **16 declaration sites** | one status, consistently |
| One aggregate, spec 029's | 1 | the other status |

Changing sixteen would be a breaking contract change across six contexts for
nothing an operator sees. Changing one is a rename.

**This deliberately does not follow correctness alone.** The outlier's status is
the *more* correct one — RFC 9110 specifies it for a failed precondition. The
spec's answer is to make the status irrelevant to the advice rather than
standardise it, so both spellings stay legal and only the code has to conform.
A reviewer who thinks correctness should win would standardise the sixteen
instead; that is the decision to push on, and Assumptions marks it as one to
overturn.

### Deliberately kept out

No status codes, header names, function names or file paths appear in the
requirements — they are phrased as "recognisable without depending on the HTTP
status" and "one way to express". The Assumptions section carries the concrete
reasoning, including the 16-versus-1 count, because a cost argument is not
credible without the number.

### The requirement most likely to be under-tested

**FR-006** — that the six correct contexts do not change. It is invisible: every
test of this feature could pass while a layouts operator starts seeing different
words. SC-004 makes it checkable by requiring their existing tests to pass
*without modification*, which is the only form of that assurance that cannot be
edited into agreement.
