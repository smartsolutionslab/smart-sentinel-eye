# Specification Quality Checklist: The operator console on one origin

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-11
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs) beyond what the governing ADR already fixes (paths, `/api` prefix, ports)
- [x] Focused on user value and business needs (one address for the console and its API)
- [x] Written so a non-developer can follow the four user stories
- [x] All mandatory sections completed

## Requirement Completeness

- [ ] No [NEEDS CLARIFICATION] markers remain — **one open: Q1 (bare REST routes vs shell pages at `/system-variables` and `/event-ingestion`)**
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (one address, zero 429s, counts unchanged, latency bar unchanged)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded (kiosk, wall, e2e cut-over, production edge, Keycloak, SignalR, WebRTC out)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification beyond the path map the ADR fixes

## Notes

- FR-013 deliberately differs from the wording of issue #3245 (CORS narrowing): ADR-0192 decisions 6 and 7 make it a later step. The issue is to be corrected when this spec is accepted.
- The production byte server (gateway itself or a static container) is a plan decision, recorded there with its reason (ADR-0192 decision 4).
- Facts checked against the tree on 2026-11-11: nine gateway routes in `src/ApiGateway/appsettings.json`; shell page routes `event-ingestion/*` and `system-variables` in `apps/management-web/src/app/router.tsx`; fixed ports in `src/AppHost/WebApplications.cs`; `management-web` realm client already allows `http://localhost:*`; the gateway endpoint is declared with no fixed port (`AppHost.cs`), hence FR-015.
- One item open until the owner answers Q1; every other item passes.
