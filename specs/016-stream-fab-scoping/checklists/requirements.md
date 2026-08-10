# Specification Quality Checklist: Fab-scope stream distribution

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-10
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
- [x] Success criteria are technology-agnostic
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

### The surface was verified before drafting

Spec 015 withdrew three requirements because its plan and contract were written
by analogy to a sibling context rather than against the real code. That cost
FR-003, FR-006 and FR-010 and produced three issues (#1433, #1434, #1435).

This spec was written only after enumerating what actually exists:

- **Three endpoints**: `GET /streams`, `GET /streams/{cameraIdentifier}`,
  `POST /streams/authorize`. No write endpoint an operator drives — nothing
  here is authored.
- **One aggregate**, `Stream`, whose only creation path is
  `Stream.Provision(...)`, called from a handler on `CameraRegisteredV1`.
- **Cameras and streams are in separate databases.**

That last fact killed the first plan before it was written. The intended
backfill — deriving each existing stream's fab by joining to the cameras table
— is impossible: Postgres cannot join across databases without `dblink`. The
requirement became FR-008 plus FR-009 (runtime derivation, invisible until
filled) instead of a SQL `UPDATE`, and the reasoning is in Assumptions.

### Requirements deliberately absent

No ambiguity requirement, and no fab-required/fab-ambiguous refusal. Both would
be nonsense here: nothing takes a fab from a caller, so there is no ambiguity
to resolve and nothing to refuse. Specs 013–015 each have them; copying them
across is exactly the reflex that cost spec 015.

### Open risk carried into planning

FR-009's window — a stream with no fab is visible to nobody — is correct but
observable: after deploying, streams disappear from every listing until the
runtime backfill completes. Planning must decide whether that window needs to
be closed before first read, or is acceptable given the reconciler already runs
at startup.
