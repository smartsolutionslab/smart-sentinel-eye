# ADR-0103: Integration tests are Aspire-only (Testcontainers not adopted)

**Status:** Accepted
**Date:** 2026-05-30
**Supersedes:** the Testcontainers-specific guidance in ADR-0033, ADR-0052, ADR-0068
**Superseded by:** —

## Context

ADR-0052 (test stack) and ADR-0033 (CI gates) specified integration
tests running against **Testcontainers** (Postgres, RabbitMQ, Keycloak),
with the Aspire fixture from ADR-0068 layered on top. In practice the
project never took a dependency on the **Testcontainers** library. Every
integration test instead boots the real composition root through
.NET Aspire's testing host:

```csharp
await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.SmartSentinelEye_AppHost>(parameters);
```

Aspire's Developer Control Plane (DCP) starts the real containers
(Postgres/TimescaleDB, Keycloak, RabbitMQ, MinIO, MediaMTX, mosquitto)
that the production AppHost declares, one boot per test assembly via the
xUnit `AspireFixture` collection fixture. The written stack (ADR-0052/
0033/0068, the constitution, CLAUDE.md, CONTRIBUTING) therefore
diverged from what is actually built and run.

## Decision

Integration tests are **Aspire-only**. The `Testcontainers` library is
**not** a dependency and is not used. Real infrastructure for
integration tests comes exclusively from booting the production AppHost
via `DistributedApplicationTestingBuilder` (the `AspireFixture` pattern,
ADR-0068).

This is a reconciliation, not a new direction: it records the choice the
codebase already embodies and aligns with the long-standing preference
to exercise the real Aspire composition rather than a parallel
container-orchestration library.

- **Unit tests** keep using hand-written in-memory fakes (no containers).
- **Integration tests** use the Aspire fixture (real containers via DCP).
- CI runs the same fixture; the integration job only needs Docker
  available on the runner (Aspire/DCP drives it). No Testcontainers
  reuse/lifetime configuration is needed.

The Testcontainers references in ADR-0033, ADR-0052, and ADR-0068 are
superseded by this ADR. Historical per-feature spec documents
(`specs/001..008`) are left as point-in-time records.

## Consequences

**Positive:**

- One integration path, matching production composition exactly (same
  AppHost wiring, references, health gates) — higher fidelity than a
  separately-assembled Testcontainers stack.
- One container lifecycle to reason about (Aspire/DCP), one fixture.
- No second container-orchestration dependency to version or learn.

**Negative:**

- Integration tests require a bootable AppHost; a composition or boot
  defect fails the whole suite rather than an isolated container (this
  is also a feature — it catches wiring regressions, e.g. the spec-009
  MigrationRunner DI bug).
- Cold-start cost is the full stack per assembly. Mitigated by the
  per-assembly collection fixture (boot once, share across the class).

## Alternatives Considered

**Keep Testcontainers per ADR-0052/0033 — REJECTED.** It was specified
but never adopted; adding it now would duplicate what the Aspire fixture
already provides and reintroduce the doc/code divergence this ADR closes.

**Testcontainers as a CI-only fallback (per the old constitution note)
— REJECTED.** A CI-only second path that never runs locally is a
maintenance liability; the Aspire fixture runs identically in CI given
Docker on the runner.
