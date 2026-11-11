# Implementation Plan: The operator console on one origin

**Branch**: `491-gateway-single-origin-shell` | **Date**: 2026-11-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/491-gateway-single-origin-shell/spec.md`. **Governing decision**: ADR-0192 (accepted).
**Issue**: #3245 (phase 1 of epic #3248).

## Summary

The gateway becomes the one origin for the operator console: the shell at `/`, the two federated remotes under `/remotes/<name>/`, and REST
at `/api/<context>/...` (prefix stripped). The work has four parts, in this order, and **the first is a spike that can stop the rest**
(ADR-0192, Implementation Notes):

1. **Spike (the riskiest assumptions, decided by observation before anything depends on them).** (a) Vite's hot-reload socket passes
   through YARP and `BoundaryTests` stays green unmodified; (b) a remote built with a path `base` loads through the gateway, chunks included;
   (c) a YARP route that matches "request header absent, or not `document`" behaves as Q1 option A needs.
2. **Gateway routing.** `/api/<ctx>` twins of the nine routes, the navigation rule on the bare routes, the `/api/` carve-out twin, and a
   config-gated UI section (`Ui`) that hosts the pages in one of two modes: `Proxy` (dev) or `Static` (built bundles).
3. **Frontend paths.** A Vite `base` per remote, relative remote locations, `VITE_API_GATEWAY_URL=/api`, and Vite dev-server proxies so the
   fixed port 5173 keeps working with the same values.
4. **Composition.** AppHost sets the dev `Ui` mode and a stable dev address for the gateway, in run mode only.

§IV legs touched: **none**.

## Technical Context

**Language/Version**: C# on .NET 10 (gateway); TypeScript, React 19, Vite 8 (apps).

**Primary Dependencies**: `Yarp.ReverseProxy` 2.3.0, `Microsoft.Extensions.ServiceDiscovery.Yarp` 10.11.0, ASP.NET Core static files (in the
shared framework, no new package). `@module-federation/vite` 1.23.10, `@module-federation/runtime` 2.9.3 (already pinned).

**Storage**: N/A.

**Testing**: xUnit + Shouldly, `Architecture.Tests` (`BoundaryTests`), `Integration.Tests` (gateway routing, over the Aspire fixture);
vitest (`management-web` remotes, `management-cameras`, `management-event-ingestion`); Playwright (`e2e/operator-shell-federation.spec.ts`,
three URL expectations, see D8).

**Target Platform**: the gateway process on Linux/Windows; evergreen Chromium browsers.

**Project Type**: web application (gateway service plus three Vite apps).

**Performance Goals**: no new budget. Click-to-first-frame stays ≤ 3 s p95 (spec 002 FR-013), measured twice (SC-005).

**Constraints**: `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` unmodified; one gateway instance
(ADR-0153 clause 1); `Gateway_returns_404_for_a_path_with_no_configured_route` stays true wherever no UI is hosted (D1).

**Scale/Scope**: nine REST routes duplicated, two remotes, one shell, one fixed dev address.

## Constitution Check

_GATE: passes before research; re-checked after design (still passes)._

| Rule                                            | Status                                                                                                                                                                                                                                                                                                                                       |
| ----------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| §IV latency budget (every change cites its leg) | **Legs touched: none.** The hub, WebRTC and Keycloak stay direct (ADR-0192 decision 3). The one WebSocket the gateway carries is Vite's dev hot-reload socket.                                                                                                                                                                               |
| §IV, the structural guard                       | `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` is not edited. If the spike (a) fails, **work stops and ADR-0192 is amended**; the test is never weakened (FR-014).                                                                                                                                            |
| §VI Aspire is the composition root              | The dev `Ui` mode, the stable dev address and the reference from the gateway to the three Vite apps are declared in `AppHost`, not wired by hand.                                                                                                                                                                                            |
| §VIII safe by default at trust boundaries       | The gateway becomes an HTML origin. `Authorization` is forwarded unmodified and not interpreted (the gateway still validates no JWTs, ADR-0106). Static serving is confined to configured roots by `PhysicalFileProvider` (no path traversal). No new scope. A Content-Security-Policy is **deferred and filed** (D9), not silently skipped. |
| §III bounded-context isolation                  | The gateway references no context (`ApiGateway_references_no_bounded_context`, unchanged).                                                                                                                                                                                                                                                   |
| ADR-0036 smallest change                        | No speculative abstraction: two hosting modes are exactly the two ADR-0192 names; the paths are constants of the ADR, not config.                                                                                                                                                                                                            |
| ADR-0139 test colour                            | New behaviour (routes, the navigation rule, static hosting, env guards) is **red first**. The existing frontend suites are characterisation: they pass unmodified (SC-002). One e2e spec's URL expectations change (D8), recorded rather than hidden.                                                                                        |
| ADR-0030 / ADR-0086                             | Conventional commits, no `Co-Authored-By`.                                                                                                                                                                                                                                                                                                   |

No violations; Complexity Tracking is empty.

## Decisions

Each is expanded, with the alternatives, in [research.md](./research.md).

- **D1. The UI is hosted only when configured.** A `Ui` configuration section turns UI hosting on (`Ui:Mode` = `Proxy` or `Static`). Absent,
  the gateway behaves exactly as today, so `Gateway_returns_404_for_a_path_with_no_configured_route` ("no accidental catch-all") keeps its
  meaning in the integration stack, which has no web apps.
- **D2. REST twins in config, guarded by a test.** The nine `/api/<ctx>/{**catch-all}` routes are written out in `appsettings.json` beside the
  bare ones (same `CorsPolicy`, `RateLimiterPolicy`, transforms with `PathRemovePrefix` = `/api/<ctx>`). A test asserts every bare route has
  an `/api` twin with identical policy, so the two lists cannot drift.
- **D3. Q1 option A as one rule.** Each bare route gets a header match `Sec-Fetch-Dest` **NotContains** `document` (YARP
  `HeaderMatchMode.NotContains`). A browser navigation matches no bare route and falls to the shell fallback; `fetch`, scripts and test
  clients (no such value) still match. Spike (c) pins the absent-header case by a test before D3 is relied on.
- **D4. Dev mode = proxy; production mode = the gateway serves the built files itself.** In `Proxy` mode YARP routes `/remotes/cameras/**`,
  `/remotes/event-ingestion/**` and the lowest-order `/{**}` to the Vite dev servers. In `Static` mode the gateway serves each built `dist`
  directory with ASP.NET static files and falls back to the shell's `index.html`. **Why not a separate static container** (ADR-0192 decision 4
  leaves this to this plan): ADR-0192 already rejected two services sharing the front door; a second process adds an AppHost resource, a
  second health story and a second deploy artifact for no benefit while there is one operator console; the single-point-of-failure cost is
  already accepted in the ADR. The price is that the gateway image must contain the three `dist` folders at publish time; that is a
  packaging step for phase 4, not for this phase, which proves `Static` mode by a test with a temp directory and by serving real built
  bundles locally (quickstart).
- **D5. Static assets and the proxied UI do not use `per-source`.** Static files are served by middleware **before** the limiter and the proxy;
  proxied UI routes carry no `RateLimiterPolicy`. `/api/...` keeps `per-source` as today (FR-009).
- **D6. Vite `base` is a constant per remote, and the shell stays at `/`.** `management-cameras` uses `base: '/remotes/cameras/'` and
  `management-event-ingestion` `base: '/remotes/event-ingestion/'`; the shell keeps `/`. They are constants of ADR-0192's path map, not
  environment values, so there is nothing to forget at build time. `@module-federation/vite` reads Vite's `config.base` (verified in the
  installed 1.23.10); the spike proves the chunks and `remoteEntry.js` agree.
- **D7. One env set, relative values, Vite proxies for the fixed port.** A Vite dev server has one env, so the values cannot differ between
  `:5173` and the gateway origin. The dev values become `VITE_API_GATEWAY_URL=/api` and the two remote URLs `/remotes/cameras`,
  `/remotes/event-ingestion`, and the shell's Vite dev server proxies `/api`, `/remotes/cameras` and `/remotes/event-ingestion` (to the
  gateway and to 5176/5177), the same pattern the kiosk already uses for `/hubs`. Then `:5173` and the gateway origin behave identically.
  The proxy target is passed as a shell-safe alias env name (AppHost comments record why hyphenated names are dropped by POSIX shells).
  **Consequence to confirm (see "For the owner")**: the shell's own dev calls no longer cross an origin; the cross-origin path is still
  exercised by the kiosk and the wall (5174, 5175) and by Playwright's direct API calls until #3246 and #3247.
- **D8. The remote entry moves under its base, so one e2e spec changes.** With a `base`, a remote's entry is `/remotes/cameras/remoteEntry.js`,
  not `:5176/remoteEntry.js`. `e2e/operator-shell-federation.spec.ts` asserts the old URL in three places. Those three substrings change to the
  new path; nothing else in e2e changes in this phase (the full cut-over is #3247). Recorded in the PR, not hidden.
- **D9. No Content-Security-Policy in this phase.** A CSP must name the identity provider, the hub and the media origins, plus the app's
  own inline styles; that is its own design and test. Phase 1 adds only `X-Content-Type-Options: nosniff` on gateway-served static files, and
  files an issue for the CSP and security headers, referenced from the PR and the epic.
- **D10. A stable dev address in run mode only.** The gateway endpoint gets a fixed port (proposed **5180**; verify it is free) when
  `isRunMode` and not test mode, so the one URL is bookmarkable and allowed by the realm client's existing `http://localhost:*` redirect.
  Test mode keeps its dynamic port. The gateway references the three Vite resources by service discovery (no `WaitFor`, matching the
  existing no-wait reasoning), so destinations are `http://management-web` etc.

## Project Structure

### Documentation (this feature)

```text
specs/491-gateway-single-origin-shell/
├── spec.md
├── plan.md              # this file
├── research.md          # decisions, alternatives, what was verified in the tree
├── contracts/
│   └── gateway-paths.md # the path map and the Ui configuration contract
├── quickstart.md        # how to see it working, and the spike's pass/fail table
└── tasks.md             # /speckit-tasks (not created here)
```

(No `data-model.md`: the feature has no stored entities; the path map is in `contracts/`.)

### Source Code (repository root)

```text
src/ApiGateway/
├── Program.cs                         # Ui hosting (Proxy/Static), static files before limiter, shell fallback
├── appsettings.json                   # /api twins; Sec-Fetch-Dest rule on the bare routes
└── (a small Ui options type, only if Program.cs would pass the 300-line advisory)

src/AppHost/
├── AppHost.cs                         # fixed dev port for api-gateway (run mode only)
└── WebApplications.cs                 # Ui__Mode=Proxy on the gateway; gateway -> three Vite apps; relative env values

apps/management-web/vite.config.ts            # server.proxy: /api, /remotes/*
apps/management-cameras/vite.config.ts        # base '/remotes/cameras/'
apps/management-event-ingestion/vite.config.ts# base '/remotes/event-ingestion/'
apps/management-web/src/app/navigation/remotes.ts   # relative base URLs accepted; guard kept
docs/deployment-frontend-env.md               # the new contract values
e2e/operator-shell-federation.spec.ts         # three URL substrings (D8)

tests/Integration.Tests/ApiGateway/           # /api twins, navigation rule, carve-out twin, Ui modes
tests/Architecture.Tests/                     # twin-parity guard (if it is a source scan); BoundaryTests UNMODIFIED
```

**Structure Decision**: web application; the change is a gateway-side routing/hosting slice plus path configuration in three existing apps.
No new project.

## Risks and how each is bounded

| Risk                                                                                                                  | Bound                                                                                                         |
| --------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| The hot-reload socket cannot pass without the gateway touching `Microsoft.AspNetCore.WebSockets` / `Http.Connections` | Spike (a), first. Fail = stop, amend ADR-0192, never weaken the test.                                         |
| `NotContains` does not match a request with the header absent                                                         | Spike (c), first, by a test. Fail = use `NotExists` on one route and `NotContains` on another, or revisit Q1. |
| A remote loads its chunks from `/` instead of its base                                                                | Spike (b); the e2e federation spec then observes the remote end to end.                                       |
| Page loads hit the rate limiter                                                                                       | D5 plus SC-003 (zero 429 at the production limit).                                                            |
| Two modes drift                                                                                                       | D2's parity test; the fixed-port path is exercised by the unmodified suites and by e2e.                       |
| The gateway restart now takes pages down too                                                                          | Accepted in ADR-0192; unchanged here.                                                                         |

## For the owner (confirm at the Plan gate)

1. **D7's consequence.** Because one Vite dev server has one set of env values, the shell moves to relative `/api` and `/remotes/*` everywhere
   and `:5173` keeps working through Vite's own proxy. The shell's **own dev calls stop being cross-origin**; the cross-origin path remains
   exercised by the kiosk, the wall and Playwright's direct API calls until #3246 and #3247. This is the reading of FR-012 I propose.
2. **D8.** A remote's entry URL changes from `:5176/remoteEntry.js` to `/remotes/cameras/remoteEntry.js`, so three expectations in one e2e
   spec change in this phase. "The fixed ports keep working" holds for the apps; it cannot hold for that URL.
3. **D9.** CSP and security headers are deferred to a filed follow-up, not part of this phase.
