# ADR-0063: Test Project Layout — Architecture.Tests + Integration.Tests Initially

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney has per-context per-layer test projects (Domain.Tests,
Application.Tests, Infrastructure.Tests, Api.Tests) — 4 test projects
per bounded context. For 9 contexts that's 36 test projects.

ADR-0033 mandates integration tests via Aspire AppHost + Testcontainers
on every PR. We need at least one project to hold those tests.

## Decision

**The initial scaffold creates two test projects only:**

- `SmartSentinelEye.Architecture.Tests` — cross-cutting NetArchTest
  boundary rules (enforces ADR-0027 inter-context isolation).
- `SmartSentinelEye.Integration.Tests` — full-stack integration tests
  via Aspire fixture (ADR-0068, deferred until first integration test).

**Per-context per-layer test projects are created per-feature** during
Spec-Kit work. The first feature spec that touches CameraCatalog
creates `CameraCatalog.Domain.Tests`, `CameraCatalog.Application.Tests`,
etc., following the Yumney pattern.

## Consequences

- **Positive:** Karpathy-aligned — smallest scope at scaffold time.
- **Positive:** test project structure mirrors actual feature work,
  not speculative future contexts.
- **Negative:** less rigid up-front structure than Yumney's pre-built
  layout. Per-feature creation requires reviewer vigilance.

## Alternatives Considered

- **Yumney's full 36-project layout up front** — rigid, predictable,
  largely empty for the first months.
- **One test project per context (not per layer)** — middle ground;
  loses per-layer coverage gates (ADR-0065).
