# Specification Quality Checklist: 477 — OverlayDraftForm split

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-02
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what a decomposition's scope needs (which concerns become hooks and which components is left to the plan)
- [x] Focused on the operator's unchanged experience and the developer's readability
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001 is an empty `git diff` on the test files)
- [x] Success criteria are measurable (test counts equal, unit lengths in code and comment lines, three suppressions, suite count and duration)
- [x] Success criteria are technology-agnostic where possible (a refactor's subject is code shape)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified (render-time state, `readOnly`, `onElementsChange` ref, skip-able preview, comments as evidence, stop sign)
- [x] Scope is clearly bounded (callers, OverlayEditor/ChainRecoveryNotice, `useSubmitGuard`, LayoutEditorDialog, the suppressions' judgement, apps/shared out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into the specification

## Notes

- Premise check recorded: 21 hook calls (the issue said 13), a 476-line component, 11 concerns, three suppressions confirmed, an 11-file / 3,857-line family. The family is probed before anything moves (spec 476 found nine of nineteen breaks unnoticed in a 2,757-line family).
- Stated up front: the file total will rise; the value is readability. A stop-and-ask step follows the first real extraction.
- Concern E (selection with regenerating row ids and a render-time adjustment) is flagged as the delicate one.
- Open for the plan, by design: unit boundaries and names, the probe list, which concern needs a covering test first, the extraction order.
