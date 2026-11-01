# Specification Quality Checklist: 472 — split `OverlaysPage.test.tsx`

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-01
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation choices beyond what a test-layout change needs (the sharing mechanism and the exact state rewrite are left to the plan)
- [x] Focused on the maintainer's need: nothing lost, concerns findable
- [x] Written so a reviewer can check it without reading the plan
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-002 is a diff of recorded lists; FR-003 names the only allowed differing lines)
- [x] Success criteria are measurable (56 = 56, empty diff, 684 suite, size limit in the plan)
- [x] Success criteria are technology-agnostic where possible (test layout is the subject)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified (43 assignment sites, hoisted `vi.mock`, generated tests, describe-local hooks, full names)
- [x] Scope is clearly bounded (LayoutsPage test, production files, assertions, FocusRefetch/RetryRecovery helpers out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into the specification

## Notes

- Premise check recorded: small preamble (~133 lines) but **43 test-body lines assign to shared mutable mock state**, so unlike spec 470 this is not a byte-identical move; FR-003 and the third scenario bound exactly what may differ.
- Counts per group were read from a run (56 tests), not from source.
- Open for the plan, by design: the sharing mechanism and the exact rewrite of the assignment sites; the grouping of the 13 groups into files; the size limit.
