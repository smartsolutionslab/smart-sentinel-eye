# ADR-0108 — Playwright for browser end-to-end tests

**Status:** Accepted

**Relates to:** ADR-0052 (test stack), ADR-0103 (Aspire fixture, no Testcontainers), ADR-0074 (two React apps), ADR-0080 (browser auth), ADR-0106 (API gateway), ADR-0007/0008 (Keycloak)

## Context

The frontend has two test layers — vitest unit/component tests (mocked RTK
Query) and the .NET xUnit integration suite against the Aspire stack — but
**nothing exercises the real browser app against the real backend**. The
gateway work (ADR-0106) and the auth wiring mean a request now crosses
OIDC login → token → cross-origin gateway → service → DB. That seam is
exactly where unit/contract tests are blind, and it only "works" if every
layer agrees (issuer, CORS, route prefix, scope). We need a true
end-to-end test that drives the browser through it.

## Decision

Use **Playwright** (`@playwright/test`) for browser e2e.

- Specs live at the repo root under `e2e/`, **outside** the `apps/*` pnpm
  workspace, so they are not pulled into the per-app `lint`/`typecheck`/
  `test` runs (which must stay fast and stack-free).
- E2e runs against a **live `aspire run` stack** — Aspire owns
  orchestration, so Playwright does **not** manage a `webServer`. The
  config points `baseURL` at management-web (`http://localhost:5173`).
- Auth is the **real Keycloak login** as a seeded realm user
  (`operator`/`admin`), so the test covers OIDC redirect + token + the
  gateway's per-service JWT validation, not a faked token.
- Chromium project for now; WebKit/Firefox can be added when kiosk
  WebRTC/WS coverage lands.

## Consequences

**Positive:**

- First real coverage of the login → gateway → service → DB vertical;
  a broken issuer, CORS origin, route prefix, or missing scope fails the
  build instead of surfacing as a runtime 401/404.
- Playwright is multi-origin-native (app ↔ Keycloak ↔ gateway), headless
  in CI, with tracing/video on failure.

**Negative / costs:**

- New tooling + browser binaries (`playwright install chromium`).
- E2e needs the **full Aspire stack up**, so it is heavier than unit/
  integration and runs **separately** — it is **not** wired into the
  default CI jobs yet. A dedicated CI job (boot Aspire, seed, run
  Playwright) is a follow-up.

## Alternatives considered

- **Cypress — rejected.** Weaker multi-origin support (the app↔Keycloak↔
  gateway hop matters here) and a heavier runner; Playwright's tracing and
  parallelism fit CI better.
- **HTTP-only e2e via the xUnit Aspire fixture — rejected as the primary.**
  Fast and stable, but it never loads the React app, so "make the frontend
  work" stays unverified at the UI. Kept as a complementary layer.
