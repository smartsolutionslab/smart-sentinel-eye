# Specification Quality Checklist: Every leg of the latency budget can be watched

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-21
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

**All items pass.** Four judgements are surfaced here rather than left in the
Assumptions section, because each of them bounds what the feature is:

**The feature succeeds by *addressing* six legs, not by instrumenting six legs.**
Three are in a browser with no telemetry route, and headroom may be an
arithmetic remainder rather than a measurable segment. FR-007 and SC-003 are
written so a leg with a recorded reason counts as handled. Without that, the
feature is either dishonest or unfinishable — and the alternative failure mode,
quietly instrumenting the easy legs and not mentioning the others, is exactly
what left §VII unmet for six features.

**"Watched" means aggregated, not traced, and the distinction is the feature.**
Spec 023 gave one leg spans, which answer "where did this event go". A budget is
a claim about the tail across many events, which spans alone cannot settle. A
reviewer who reads this as "add more tracing" has read it wrong.

**SC-004 puts a number on instrumentation overhead — under 5% of the measured
leg's budget.** Composite-and-render has 50 ms, so its instrumentation gets
2.5 ms. Stated because the temptation is to call observation free, and the legs
least able to afford it are the ones with the smallest budgets.

**FR-011 asks for an ADR decision this spec deliberately does not make.**
ADR-0026 describes a two-sink comparison phase that never started. Enacting it
and amending it are both defensible and they lead to different features. The
plan should present the options; the spec should not pick one by implication.

**No [NEEDS CLARIFICATION] markers were needed**, but one candidate was
considered and rejected: whether this feature covers production observability or
development and CI. The spec answers it as an assumption — dev and CI first,
because that is where measurement can be exercised today, with the standing
caveat from specs 020, 022 and 023 that a figure taken there is not a figure
about a fab. Cheaper for a reviewer to overrule than to re-derive.
