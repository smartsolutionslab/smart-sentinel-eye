# ADR-0074: Frontend App Shell — Two Apps (Management + Kiosk)

**Status:** Accepted
**Date:** 2026-05-25

## Context

Three user surfaces exist: **admin** (system configuration), **operator**
(control room workstation), **kiosk** (always-logged-in display on the
wall). Kiosk is security-sensitive — it boots with a device-bound
credential (ADR-0008) and should not have admin code loaded.

## Decision

**Two Vite + React + TypeScript apps:**

- `apps/management-web/` — admin + operator personas in one SPA with
  role-based routing. Operator UI loads on demand for users with
  operator scope; admin UI for admin scope.
- `apps/kiosk-web/` — always-logged-in display kiosk. Minimal bundle.
- `apps/shared/` — workspace package with shared UI components,
  generated API client, TypeScript types, hooks, realtime client
  (ADR-0076).

Each app:

- Has its own `package.json`, Vite config, build pipeline.
- Is its own Aspire JS resource in the AppHost.
- Produces its own container image / Helm chart.

## Consequences

- **Positive:** kiosk attack surface bounded; a bug or exploit in
  admin UI cannot affect kiosk runtime.
- **Positive:** kiosk bundle stays small.
- **Negative:** two build pipelines, two Aspire resources. Marginal.
- **Negative:** code duplication risk if shared workspace isn't
  maintained; mitigated by lint rules.

## Alternatives Considered

- **Single SPA with role-based routing for all three personas** —
  simpler ops, larger blast radius for kiosk.
- **Three separate apps (admin, operator, kiosk)** — stronger
  isolation, more ops overhead than warranted.
