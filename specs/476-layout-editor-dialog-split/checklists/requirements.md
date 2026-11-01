# Specification Quality Checklist: 476 — LayoutEditorDialog split

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-01
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what a decomposition's scope needs (which concerns become hooks and which components is left to the plan)
- [x] Focused on the operator's unchanged experience and the developer's readability
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001 is an empty `git diff` on the test files)
- [x] Success criteria are measurable (test counts equal, unit lengths, one suppression, suite count and duration)
- [x] Success criteria are technology-agnostic where possible (a refactor's subject is code shape)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified (hook order, `open`-driven state, whole-form props, comments as evidence, stop sign)
- [x] Scope is clearly bounded (callers, GridDesigner/TileView/ChainRecoveryNotice, `useSubmitGuard`, OverlayDraftForm, apps/shared out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into the specification

## Notes

- Premise check recorded: 21 hook calls (the issue said 11); a 444-line component with seven interleaved concerns; the `knownCameras` merge and `wasOpen` adjustment are done during render and must stay so; `saveBlocked`'s guard is deliberately outside #3198.
- Stated up front: the line total will not fall (signatures and imports are added); the value is readability, and ADR-0084's limit is not enforced.
- Open for the plan, by design: the unit boundaries and names, which concern needs a covering test written first, and the extraction order.
