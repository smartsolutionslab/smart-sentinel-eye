# Specification Quality Checklist: 478 — OverlayEditor split

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-02
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what a decomposition's scope needs (which concerns become functions, hooks and components is left to the plan)
- [x] Focused on the operator's unchanged experience and the developer's readability
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001 is an empty `git diff` on the test files)
- [x] Success criteria are measurable (test counts equal, unit lengths in code and comment lines, no suppression, suite counts and durations)
- [x] Success criteria are technology-agnostic where possible (a refactor's subject is code shape)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified (one history, callback identity, `readOnly` reaching only the colour field, two different repeat-announcement fixes, comments as evidence, stop sign)
- [x] Scope is clearly bounded (the already-separate units, every caller, the props and exports out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into the specification

## Notes

- Premise check recorded: 34 hook calls (the issue said 31), a 576-line component, no `eslint-disable`, 11 concerns, an 8-file covering family; the only production caller is `management-web`'s `OverlayDraftForm` (the kiosk does not import it).
- Stated up front: the file total will rise; the value is readability. A stop-and-ask step follows the first real extractions.
- The shared `useOverlayEditHistory` instance and the sibling DOM order are the two load-bearing facts (spec § premise check 4).
- Open for the plan, by design: unit boundaries and names, the probe list, direct unit tests for the now-importable pure functions, the extraction order.
