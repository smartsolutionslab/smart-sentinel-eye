# Specification Quality Checklist: 475 — form-dialog effects

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-01
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what a refactor's scope needs (one hook or several, and names, left to the plan)
- [x] Focused on the operator's unchanged experience and the developer's single source for the invariants
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001 is an empty `git diff` on the test files)
- [x] Success criteria are measurable (test counts equal, grep result, reported line accounting)
- [x] Success criteria are technology-agnostic where possible (a refactor's subject is code shape)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified (`useEffectEvent` vs lint rules, hook order, focus-return guard placement, comments as evidence)
- [x] Scope is clearly bounded (LayoutEditorDialog/OverlayDraftForm, FormErrorSummary wiring, suppressions, behaviour changes out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into the specification

## Notes

- Premise check recorded: six of the eight submit guards are byte-identical (the other two use `saveBlocked` and are #3192/#3193's files); the clear-on-close bodies differ; the two reset-on-open effects have different dependency arrays (`[open]` vs `[open, currentName]`), so a hook keyed only on `open` would change Rename.
- The saving is stated up front as small (the comparable #3190 gave -2.4 %); the value is one place for the #2624 and parent-driven-close invariants, with a test that fails when they break.
- Open for the plan, by design: one hook or several, names, the hook API for the differing dependency, and the line-accounting target.
