# ADR-0065: Coverage Gates — 90/80/90 Enforced by CI

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0033 makes CI gates strict. Coverage thresholds give a quantitative
signal alongside the qualitative ones. Yumney enforces 90% Domain,
80% Application, 90% Shared via a Python script that blocks CI.

## Decision

Adopt Yumney's coverage gates **(aligned)**:

| Layer | Minimum coverage |
|---|---|
| `<Context>.Domain` | ≥ 90% |
| `<Context>.Application` | ≥ 80% |
| `Shared.Kernel` | ≥ 90% |
| `Shared.Contracts` | ≥ 90% |
| `<Context>.Infrastructure` | no gate (covered by integration tests) |
| `<Context>.Api` | no gate (covered by integration tests) |

- **Tool:** coverlet (cobertura output).
- **Enforcement:** custom script (PowerShell or Python, adapted from
  Yumney's `scripts/check-coverage-thresholds.py`) runs in CI after
  unit tests. Fails the PR if any layer falls below its threshold.
- **Escape hatch:** PR label `coverage-exempt` skips the gate. Use
  requires justification in the PR body.

## Consequences

- **Positive:** quantitative quality bar; reviewers don't have to
  count tests by hand.
- **Positive:** Domain layer stays well-tested by force.
- **Negative:** maintaining the threshold script. Small.

## Alternatives Considered

- **Stricter 95/85/95** — too tight; bug fixes routinely fail.
- **Looser 80/70/80** — weaker signal.
- **No gate** — relies entirely on reviewer vigilance.
