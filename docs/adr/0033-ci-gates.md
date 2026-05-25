# ADR-0033: CI Gates — Maximum Strictness on Every Code PR

**Status:** Accepted
**Date:** 2026-05-25

## Context

Smart Sentinel Eye runs 24/7 in industrial fabs. A defect that reaches
production is operationally expensive and reputationally damaging.
GitHub Actions is the only CI infrastructure in scope. We need to
decide which gates are mandatory before a PR can merge.

## Decision

Apply **maximum-strictness gates on every code PR**, into both
`develop` and `main`.

Required checks (all blocking):

| Job | Tool |
|---|---|
| .NET build (Release) | `dotnet build` |
| .NET unit tests | xUnit |
| Boundary rules | `NetArchTest` (in the test project) |
| .NET format | `dotnet format --verify-no-changes` |
| Web build | `vite build` |
| Web type-check | `tsc --noEmit` |
| Web lint | ESLint |
| Web unit tests | `vitest run` |
| Secret scan | `gitleaks` |
| Integration tests | Aspire AppHost + Testcontainers (real Postgres, RabbitMQ, Keycloak) |
| Container smoke | `aspire publish --target k8s` |

**Exemption:** PRs touching only `docs/`, `specs/`, or top-level
`*.md` skip the integration and container smoke jobs via
`paths-ignore`. Format/lint and secret scans still run on every PR
including docs.

## Consequences

- **Positive:** main and develop are protected against silent
  regressions in any covered surface.
- **Positive:** the integration suite catches Aspire AppHost wiring
  bugs that pure unit tests miss.
- **Negative:** ~10–20 minutes of CI per code PR. At ~20 PRs/week, this
  approaches the GitHub Actions free-tier monthly minute cap.
  Mitigation paths: aggressive caching of NuGet, npm, Docker layers,
  test parallelization, and (once concurrency demands) self-hosted
  runners.
- **Negative:** Testcontainers requires Docker in the runner. GitHub
  Actions Ubuntu runners ship Docker by default; Windows runners do
  not — keep integration tests on Linux.

## Alternatives Considered

- **Minimal gates** (build + unit + lint): faster, weaker safety net.
  Rejected for a 24/7 industrial product.
- **Tiered gates** (light on `develop`, heavy on `main`): lower cost
  but bugs land on `develop` and are discovered only at release time.
  Rejected — it shifts the cost from CI minutes to debugging time.
- **No exemption for docs**: integration tests on docs-only PRs add
  no signal. Accept the exemption.

## Implementation Notes

- Workflow files live in `.github/workflows/`. The primary workflow
  is `ci.yml`. Created during the .NET scaffold task.
- Required-checks list in branch protection (ADR-0029) must be kept
  in sync with this ADR.
- Self-hosted runners decision deferred — track usage in the first
  three months.
