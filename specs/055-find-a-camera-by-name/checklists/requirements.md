# Specification Quality Checklist: Find a camera by name

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-01
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

**Two stories share P1, and the second is the one that would get cut.** US1 is the
filter; US2 is that a filtered list reports a total describing its own matches.
The second reads like a detail and is not: the list's total is what every consumer
uses to decide whether it holds everything, and a filter that reports the
unfiltered total tells an operator there are 250 when eleven matched. That is the
same defect already filed against consumers rendering one page as the whole list —
this feature would be *creating* an instance of it rather than finding one.

**US3 constrains rather than adds, and is P2 for that reason only.** The chooser
today inherits the platform's list behaviour for free. Replacing it with something
that filters is where that gets lost silently, because it still looks right to
whoever built it with a mouse in their hand.

**No clarification markers.** Three candidates were resolved against evidence
rather than asked:

- *Whether the filter belongs on the picker only or the list page too* — the
  prompt explicitly offered no view. Recorded as an **assumption** (both, with the
  picker as the one that must ship if they turn out not to share work) rather than
  a question, because it changes scope but not correctness, and the plan is where
  the sharing becomes visible.
- *Whether accents fold* — deliberately left as a requirement to **decide and
  record** (FR-004) rather than decided here. Either answer is defensible; an
  unrecorded answer is not, because an operator seeing no matches cannot tell an
  unmatched rule from a missing camera.
- *Whether substring matching is fast enough* — made a measurement (FR-014, SC-006)
  rather than an assumption in either direction. A substring match cannot use an
  ordinary index, and at 250 rows per fab that may not matter at all.

**One assumption is load-bearing and worth a reviewer's attention**: that the
existing list contract is *extended*, not replaced. Every current consumer sends
no fragment and must see exactly what it sees today. If the plan finds that
cannot hold, this spec's scope is wrong rather than the plan's.
