# Specification Quality Checklist: 482 — Save gates adopt the shared useSubmitGuard

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-03
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what an adoption's scope needs (the parameter rename is left to the plan, with the trade-off stated)
- [x] Focused on the unchanged behaviour of the two Save buttons and one guard to maintain
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001 is an empty `git diff` on tests; SC-002 is a search)
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic where possible (a refactor's subject is code shape)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded (merging the gates, `saveBlocked` terms and the six existing callers explicitly out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] §IV stated as not on the path, with the reason (FR-009)

## Notes

- Premise verified against `develop` @ `e9b973a3`: `useSubmitGuard` exists in `apps/shared/src/hooks/useFormDialog.ts` (ADR-0151) with six callers;
  the two gates' hand-written handlers are identical to its body. The issue's "extract only if they match" resolves to "adopt".
