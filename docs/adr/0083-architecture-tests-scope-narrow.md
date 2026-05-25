# ADR-0083: Architecture.Tests Scope — Boundary Rules Only

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney has a `Yumney.Architecture.Tests` project containing 8 test
classes that lint CLAUDE.md convention rules: no `sut` variables, no
`Request` suffix on request types, no inline DTOs, no primitives in
commands, no void domain methods, no single-letter lambda parameters,
etc. The same approach would catch convention drift at CI time in
Smart Sentinel Eye.

## Decision

**Scope `SmartSentinelEye.Architecture.Tests` to NetArchTest boundary
rules only** — not Yumney-style convention/CLAUDE.md rule linting.

- Tests enforce ADR-0027 (no cross-context references) and ADR-0044
  (Domain layer doesn't reference EF Core / Marten / Wolverine /
  Npgsql / RabbitMQ / ASP.NET Core).
- Convention rules (no primitives in commands, no `sut` vars, no
  `Request` suffix on request types, etc.) are enforced via **PR
  review and Karpathy guideline #3**, not automated tests.

Reconsider expansion if convention drift becomes visible after the
first 2–3 features ship.

## Consequences

- **Positive:** Karpathy-aligned smaller scope. No upfront
  investment in reflection-based linting infrastructure.
- **Positive:** test maintenance bounded.
- **Negative:** convention drift possible between PRs. Reviewer
  vigilance bears the cost.

## Alternatives Considered

- **Adopt Yumney's full convention-linting scope** — 8 test classes
  to write and maintain.
- **Selective adoption** (3–5 high-value convention rules) — middle
  ground; deferred until evidence of drift.
