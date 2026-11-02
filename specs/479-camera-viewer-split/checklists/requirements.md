# Specification Quality Checklist: 479 — CameraViewer split

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-02
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what a decomposition's scope needs (which concerns become hooks, functions and components is left to the plan)
- [x] Focused on the unchanged behaviour of the wall and the developer's readability
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001 is an empty `git diff` on the test files; FR-010 and SC-006 name the timer-count test)
- [x] Success criteria are measurable (test counts equal, unit lengths in code and comment lines, no suppression, suite counts and durations, timer counts)
- [x] Success criteria are technology-agnostic where possible (a refactor's subject is code shape)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified (one shared ref, callback identity, `stats` identity, always-mounted status region, comments as evidence, stop sign)
- [x] Scope is clearly bounded (`useWhepSession`, the observability modules, every caller and the props out; no unification with `CellPage`'s copy)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into the specification

## Notes

- Premise check recorded: 13 hook calls (as the issue says; a nested-generic regex miss gave 12 at first), a 352-line component (the issue said "about 415"), no `eslint-disable`, a 10-file / 3,616-line covering family (the issue named nine), and five exports other apps import (`OverlayLabel` and `OverlayShape` included).
- **§IV: this component is on the latency path.** The spec names the three legs it hosts (decode fragment, presentation buffer, playout application) and makes "no work per frame/render/tick, same cadence, same dependency arrays, same ref scopes" a requirement (FR-003, FR-010) with a measurable criterion (SC-006).
- Stated up front: the file total will rise; the first moves of module-level code are expected to barely shrink the component (478 saw −6 %), so a measure-and-ask step follows.
- Open for the plan, by design: unit boundaries and names, the probe list, the timer-count characterisation test, the extraction order.
