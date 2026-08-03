# Specification Quality Checklist: Automation rules belong to a fab

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-03
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

Two clarifications that would otherwise have been `[NEEDS CLARIFICATION]`
markers were decided before drafting and are recorded in Assumptions:

- **What happens to rules that already exist** — assigned to `munich`,
  chosen over archiving them (which would stop live automation) and over
  leaving them unassigned (which would preserve the defect for the rules
  most likely to be running).
- **Where a rule's plant comes from when authoring** — inferred from the
  operator when they are assigned to exactly one plant, stated explicitly
  when they are assigned to several.

The second conflicts with an existing documented decision that there is no
implicit "current plant" and the caller states it per request. FR-013 requires
that deviation be recorded rather than absorbed silently; the plan phase
should determine whether that means amending the existing decision record or
writing a new one.

**Deliberately out of scope**, recorded so it is not mistaken for an
oversight: this feature scopes the rule itself, not what a rule's action
points at. A rule in one plant referring to a value in another remains
possible and is a broader consistency question spanning variables and
overlays.

**One first draft was rewritten.** The initial User Story 1 was written from
the operator's point of view ("an operator wants their rules isolated"),
which buried the fact that the worst failure involves no operator at all. It
was reframed around the unattended cross-plant firing so the priority
ordering reflects actual harm rather than visibility.
