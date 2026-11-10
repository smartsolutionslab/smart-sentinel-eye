# Specification Quality Checklist: Bump `jsdom` 30.1.3 → 31.0.0

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-10
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details beyond what a dependency bump inherently names (package, version, files)
- [x] Focused on the outcome that matters (the suites behave the same on the new test environment)
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001..FR-009)
- [x] Success criteria are measurable (identical counts, diff contents, every new message accounted for)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded (SignalR, canvas packages and #3239 named out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] Every functional requirement has a checkable outcome
- [x] User scenarios cover the primary flows (suites unchanged; nothing slips away quietly)
- [x] Phase-4a colour declared: characterisation

## Notes

- Premise checked against the 31.0.0 release notes and the tree on 2026-11-10. Two statements in the issue body were wrong and are
  corrected in a comment on #3240: production code makes no `getComputedStyle` call, and `apps/shared` does run under jsdom (62 of 92
  test files opt in).
