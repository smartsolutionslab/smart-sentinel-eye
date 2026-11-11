# Research: The operator console on one origin

Facts below were read from the tree on 2026-11-11 unless marked **to be observed**, which means the spike decides it.

## What the tree says

| Fact                                                                                                                                                                                                                                                                                                  | Source                                                                    |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| Nine REST routes, all `/<ctx>/{**catch-all}`, each with `CorsPolicy: gateway`, `RateLimiterPolicy: per-source`, `PathRemovePrefix` and `RequestHeaderRemove: X-Fab`.                                                                                                                                  | `src/ApiGateway/appsettings.json`                                         |
| The only non-proxy route is `POST /stream-distribution/streams/authorize` → 404, a literal that beats the catch-all.                                                                                                                                                                                  | `src/ApiGateway/Program.cs`                                               |
| The gateway project references only YARP, service-discovery-YARP and ServiceDefaults.                                                                                                                                                                                                                 | `SmartSentinelEye.ApiGateway.csproj`                                      |
| `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` checks the **gateway assembly's own type dependencies** for `Microsoft.AspNetCore.SignalR`, `.WebSockets`, `.Http.Connections`. YARP carries upgrade requests inside its own assembly, so the gateway's code need not reference them. | `tests/Architecture.Tests/BoundaryTests.cs:147`                           |
| `Gateway_returns_404_for_a_path_with_no_configured_route` ("no accidental catch-all") exists in the integration stack.                                                                                                                                                                                | `tests/Integration.Tests/ApiGateway/GatewayRoutingIntegrationTests.cs:38` |
| The gateway endpoint has no fixed port: `.WithHttpEndpoint()`. The three console Vite apps have fixed unproxied ports 5173, 5176, 5177.                                                                                                                                                               | `src/AppHost/AppHost.cs:520`, `WebApplications.cs`                        |
| Web apps are added only outside test mode, so the integration stack has no Vite servers.                                                                                                                                                                                                              | `AppHost.cs` comment ("Skipped in test mode")                             |
| The shell's page routes include `event-ingestion/*` and `system-variables`; the gateway's bare routes include `/event-ingestion/**` and `/system-variables/**`.                                                                                                                                       | `router.tsx`; `appsettings.json`                                          |
| The shell reads `VITE_CAMERAS_REMOTE_URL` / `VITE_EVENT_INGESTION_REMOTE_URL` at module scope, strips a trailing `/`, throws in production when empty, and loads `${baseUrl}/remoteEntry.js`.                                                                                                         | `remotes.ts`, `RemoteSurface.tsx:163`                                     |
| The shell's `vite.config.ts` reads no environment (deliberate, see the AppHost comment on hyphenated env names). Neither remote sets a Vite `base`.                                                                                                                                                   | the three `vite.config.ts`                                                |
| `@module-federation/vite` 1.23.10 uses Vite's `config.base` for dev entry paths and has a `publicPath` option.                                                                                                                                                                                        | installed plugin source                                                   |
| The `management-web` realm client already allows `http://localhost:*`, so dev sign-in on a new local origin needs no realm edit.                                                                                                                                                                      | `smart-sentinel-eye-realm.json:134`                                       |
| `e2e/operator-shell-federation.spec.ts` asserts `:5176/remoteEntry.js` and `:5177/remoteEntry.js` (3 places). Playwright's baseURLs are 5173, 5174, 5175.                                                                                                                                             | the spec; `playwright.config.ts`                                          |
| The `VITE_API_GATEWAY_URL` default is `''` and a production build throws without it.                                                                                                                                                                                                                  | `docs/deployment-frontend-env.md`, `gateway.ts`                           |

## Decisions and alternatives

**D1: config-gated UI hosting.** Alternative: always register the shell fallback route. Rejected: it would answer every unrouted path with
the shell's HTML and break `Gateway_returns_404_for_a_path_with_no_configured_route` in the integration stack, whose purpose is to catch an
accidental catch-all.

**D2: duplicate the nine routes in config.** Alternatives: (a) derive the `/api` routes in code from the bare ones at startup; (b) YARP route
templates with an optional prefix. Rejected: (a) hides the route table from anyone reading `appsettings.json`, the repo's one place for it;
(b) YARP's `PathRemovePrefix` takes a literal, so one route cannot strip two different prefixes. Duplication is explicit, and a parity test
removes the drift risk.

**D3: Q1 option A as a header rule.** The owner chose A. `Sec-Fetch-Dest` is a "forbidden header name": scripts cannot set it, so a
`fetch` cannot accidentally carry `document`. Browsers that do not send it (very old) fall to the REST route, which is today's behaviour.
Alternatives were B and C (spec, Clarifications). **To be observed (spike c):** YARP 2.3.0 documents `NotContains` as "none of the headers
with this name may contain any of the values"; whether an absent header satisfies it is pinned by a test, not assumed.

**D4: the gateway serves built files itself.** Alternatives: a static container behind the gateway (ADR-0192 allows it as the byte server)
and Ingress path routing. Rejected for now: a container adds an AppHost resource, a second readiness story and a deploy artifact to ship one
console; Ingress has no production deployment to live in (ADR-0192 fact 4). Not closed: moving to a container later changes only where the
destination points.

**D5: static files before the limiter.** The `per-source` limiter is applied per route by name; middleware that serves a file and
short-circuits never reaches `UseRateLimiter` if ordered before it, and proxied UI routes simply omit the policy name. No new limiter, no
exemption list.

**D6: `base` as a constant.** Alternative: read it from an env var. Rejected: the path map is fixed by ADR-0192, and an env value is one more
way for a build to come out wrong silently. The shell keeps `/`.

**D7: relative env values plus Vite proxies.** Alternatives: (a) keep absolute values and detect the gateway origin at runtime (hacky, two
code paths in the shell); (b) run two dev servers per app (doubles ports and Aspire resources); (c) give up the fixed-port shell. Chosen:
one set of values and Vite's proxy, the pattern the kiosk's `/hubs` proxy already uses. **To be observed (spike b):** that a Vite dev server
with a `base` still answers what the proxy forwards to it.

**D8: edit three e2e substrings.** Alternative: leave the remote entry at the root of its port by keeping `base: '/'` and having the gateway
strip `/remotes/cameras`. Rejected: Vite dev emits root-absolute chunk URLs (`/src/...`, `/node_modules/.vite/...`), which at the gateway
origin would hit the shell, so the remote's `base` must carry the prefix. This is why the remote entry's URL has to move.

**D9: CSP deferred.** A CSP that works needs the identity provider, hub and media origins and the apps' inline styles enumerated, and a test
that fails when one is missing. That is a design of its own; shipping a permissive one would be decoration.

**D10: 5180.** No file in the tree names 5180 (searched `.cs`, `.ts`, `.tsx`, `.json`, `.yml`, `.md`, `.mjs`, `.props`, excluding `node_modules`
and build output), and nothing was listening on it on this machine on 2026-11-11. It is a convention, not a reservation; the plan's tasks
re-check it. Run
mode only; test mode keeps a dynamic port so concurrent test stacks are unaffected.

## Open items for the spike (T-first)

| Id  | Question                                                                                                                                        | Pass                                                                                   | Fail means                                    |
| --- | ----------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- | --------------------------------------------- |
| a   | Does Vite's HMR socket pass through YARP while `BoundaryTests` stays green, unmodified?                                                         | edit a shell file at the gateway origin and the page updates in place; the test passes | stop; amend ADR-0192; do not weaken the test  |
| b   | Does a remote built with `base` serve `remoteEntry.js` and its chunks under the prefix, through the gateway and through the shell's Vite proxy? | the cameras page renders at the gateway origin, all requests under `/remotes/cameras/` | adjust `publicPath`, or revisit D6/D7         |
| c   | Does `NotContains` match a request with no `Sec-Fetch-Dest`, and not one with `document`?                                                       | a test with both requests                                                              | split into `NotExists` + `NotContains` routes |
