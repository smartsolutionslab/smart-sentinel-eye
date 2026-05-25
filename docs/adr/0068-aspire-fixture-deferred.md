# ADR-0068: AspireFixture Integration Test Pattern — Deferred to First Integration Test

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney has a polished AspireFixture (`tests/.../Fixtures/AspireFixture.cs`)
that boots the AppHost in E2ETests mode with Postgres/Keycloak/Redis/
RabbitMQ via Testcontainers, exposing per-service `HttpClient`. We
will follow the same pattern.

## Decision

**Defer the AspireFixture implementation** until the first integration
test PR — not the initial scaffold. The `SmartSentinelEye.Integration.Tests`
project is scaffolded empty.

When created, the fixture mirrors Yumney's pattern:

- xUnit `IAsyncLifetime` collection fixture (`AspireFixture`).
- Boots `DistributedApplication.CreateAsync<Projects.SmartSentinelEye_AppHost>()`.
- Exposes per-context `HttpClient` (`CameraCatalog`, `StreamDistribution`, …).
- Exposes per-context `DbContext` factories for test data seeding.
- Test classes share the fixture via `[Collection(nameof(AspireCollection))]`.

## Consequences

- **Positive:** scaffold PR stays small (Karpathy-aligned).
- **Positive:** first integration test PR has full freedom to shape
  the fixture against the actual first feature's needs.
- **Negative:** integration test coverage is zero until the first
  feature lands. Acceptable.

## Alternatives Considered

- **Build the fixture now with a smoke test** — speculative; pattern
  details depend on how the first feature uses it.
