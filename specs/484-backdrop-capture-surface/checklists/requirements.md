# Specification Quality Checklist: 484 — useBackdropCapture returns three groups

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-07
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what a regrouping's scope needs (the exact group shape and any memoising are left to the plan)
- [x] Focused on the unchanged capture-and-paint behaviour of the editor and one place that says which value serves whom
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001 is an empty `git diff` on tests; SC-002 is a count)
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic where possible (a refactor's subject is code shape)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded (`BackdropControls`' props, `useFrameCapture`, the getToken gating explicitly out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] §IV stated as not on the path, with the reason (the only importers are in `management-web`) (FR-009)

## Notes

- Premise verified against `develop` @ `e9b973a3`: eleven returned values; the editor consumes all eleven, so the issue's "group or
  narrow" resolves to **group** (nothing can be dropped).
- `useBackdropCapture` has one consumer, `OverlayEditor.tsx`; its return type is inferred and not exported.
