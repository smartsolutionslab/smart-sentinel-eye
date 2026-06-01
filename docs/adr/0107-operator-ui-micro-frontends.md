# ADR-0107: Operator UI as micro-frontends

**Status:** Proposed
**Date:** 2026-06-01
**Supersedes:** —
**Superseded by:** —
**Amends:** ADR-0074 (two React apps)
**Relates to:** ADR-0075 (Redux Toolkit + RTK Query), ADR-0077/0078 (Radix + Tailwind design system), ADR-0080 (browser auth), ADR-0106 (API gateway)

## Context

ADR-0074 established two React apps: `management-web` (operator/admin)
and `kiosk-web` (display). The **operator UI spans all nine bounded
contexts** — cameras, streams, layouts, overlays, variables, events,
automation, identity, audit. As a single monolithic SPA it couples
every feature into one build, one deploy, and one release cadence, so a
change to (say) Automation forces a full operator-app rebuild/redeploy
and cannot ship independently of Identity work.

We want operator features to be **developed and deployed independently
per bounded context**, composed at runtime by a thin shell — while
keeping one coherent operator experience (shared design system, auth,
navigation). `kiosk-web` stays a single display app; it does not need
splitting.

## Decision

Re-architect the **operator UI (`management-web`) as micro-frontends**:

- A thin **shell / host** app owns: routing, auth + session
  (react-oidc-context, ADR-0080), the shared design system
  (Radix + Tailwind tokens, ADR-0077/0078), shared RTK Query / store
  contract (ADR-0075), and lazy-loads feature **remotes**.
- **Feature remotes**, built and deployed independently, grouped by
  bounded context (boundary granularity finalized during the spike).
- Remotes are served behind the **API gateway / static edge**
  (ADR-0106); the shell composes them at runtime.
- `kiosk-web` is **unchanged** (single app).

The **composition mechanism is deliberately left open** pending a spike
(see below) — Module Federation (Vite `@module-federation/vite`),
Web Components, or a route-level integration — because it is the
highest-risk, hardest-to-reverse choice and should be decided on
evidence, not up front.

## Consequences

**Positive:**

- Independent build/deploy/ownership per operator feature; per-context
  teams ship without a full-app release.
- Smaller initial bundle (remotes lazy-loaded on navigation).

**Negative — this is a significant increase in frontend complexity:**

- Shared-dependency versioning across shell + remotes (React, RTK,
  design system) must be governed or runtime breaks.
- Design system and auth become **shared packages** with their own
  release discipline.
- Cross-remote state and navigation, plus end-to-end testing **across**
  remotes, are materially harder than a single SPA.
- More frontend builds and deploy targets.

## Alternatives Considered

- **Keep the monolithic SPA (status quo, ADR-0074) — simplest.**
  Rejected as the operator surface and the number of contributors grow
  and independent cadence becomes valuable.
- **Modular monolith (enforced feature-module boundaries, one build) —
  strong middle ground.** Keeps a single deploy while disciplining
  boundaries; may be the **right first step** before committing to true
  runtime micro-frontends. Captured as the fallback if the spike shows
  runtime composition isn't worth its cost yet.

## Implementation Notes

- **Spike first (blocking):** evaluate composition approaches against a
  thin slice — shell + one real feature remote (e.g. Camera Catalog) —
  measuring bundle/load cost, shared-dep handling, auth/session sharing,
  and DX. The spike's outcome ratifies or amends this ADR.
- Shared packages to extract: design system (`apps/shared/ui`), auth/session,
  RTK store contract, the realtime client (ADR-0076).
- Remote boundary: per bounded context vs per feature — decide in the
  spike.
- Served behind ADR-0106's edge; align the shell's API base URL with the
  gateway.
- Treat as a multi-slice effort: shell + remote contract + first remote,
  then migrate contexts incrementally. Do **not** big-bang the rewrite.
