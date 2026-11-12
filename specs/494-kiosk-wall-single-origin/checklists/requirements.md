# Specification Quality Checklist: The kiosk and the wall display on one origin

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-12
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details beyond what ADR-0192 and spec 491 already fix (paths, ports, `Ui` modes); mechanisms (hub route, static-root optionality) are left to the plan
- [x] Focused on user value (a kiosk and a wall reached from one address, the live path unchanged)
- [x] Written so a non-developer can follow the five user stories
- [x] All mandatory sections completed

## Requirement Completeness

- [ ] No [NEEDS CLARIFICATION] markers remain — **Q1 open** (the kiosk's REST base: keep the absolute gateway origin, recommended, or move to `/api` now)
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (one address, zero hub/WHEP requests at the gateway, zero 429s, suites unmodified, first-frame not worse)
- [x] All acceptance scenarios are defined (happy, return-path abuse, cross-instance, fixed-port compatibility, fail-fast)
- [x] Edge cases are identified
- [x] Scope is clearly bounded (Keycloak, the hub's server side, `@microsoft/signalr`, WebRTC, CSP, production edge, e2e cut-over out)
- [x] Dependencies and assumptions identified (spec 491 merged; overlap with spec 493 in `UiHosting.cs`)

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] Test colour declared per requirement (ADR-0139)
- [x] §IV: every leg stated, none touched

## Notes

- Premises of #3246 checked against `origin/develop` at `fe07298c`; the table is in the spec. Premise 2 (realm clients need new redirect URIs) is false for dev because of the existing `http://localhost:*` wildcard; `webOrigins: ["+"]` remains to be observed (FR-007).
- Premise 4 is incomplete: the dev hub URL is page-relative, so at the one origin it would hit the gateway. FR-008 is the constraint the plan must meet.
- The spec cannot pass the Specify gate until Q1 is answered.
