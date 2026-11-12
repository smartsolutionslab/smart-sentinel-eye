# Research: The kiosk and the wall display on one origin

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-11-12

Each entry: the question, what was observed or read (with where), the decision, and the alternatives set aside. "Observed" means run on
this machine on 2026-11-12. "Read" means taken from source and not run.

## R1. What does a Vite dev server with a non-root `base` do with the paths the kiosk and its e2e suite use?

**Observed**, spike in the scratchpad (`spike494/server.mjs`): a minimal app shaped like the kiosk's `index.html` (favicon link, module
script), served by the **installed** Vite (`apps/kiosk-web/node_modules/vite`, **8.3.6**) with `base: '/kiosk/'`, the same `/hubs` proxy
options as `apps/kiosk-web/vite.config.ts` (`ws: true, changeOrigin: true, secure: false`) pointed at a fake upstream, and a plugin that
injects a root-absolute `/@fs/...` preload exactly as `apps/shared/src/ui/fonts/fontPreload.ts` does in `serve`. Free ports 5190 (Vite)
and 5191 (upstream). Stopped afterwards.

| Request                                   | Answer                                                                                                                    |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `GET /` (`Accept: text/html`)             | **302**, `Location: /kiosk/`                                                                                              |
| `GET /layouts/abc` (`Accept: text/html`)  | **404**, "The server is configured with a public base URL of /kiosk/ - did you mean to visit /kiosk/layouts/abc instead?" |
| `GET /kiosk/layouts/abc`                  | **200**, the app's HTML: `/kiosk/@vite/client`, `/kiosk/favicon.svg`, `/kiosk/src/main.ts` (all rewritten under the base) |
| the plugin-injected preload in that HTML  | **not rewritten**: `href="/@fs/D:/x/font.woff2"`                                                                          |
| `GET /@fs/<real file>`                    | **404**                                                                                                                   |
| `GET /kiosk/@fs/<real file>`              | **200**                                                                                                                   |
| `GET /kiosk` (no slash)                   | **404** (Vite does not redirect it)                                                                                       |
| `GET /kiosk/oidc/callback?code=x&state=y` | **200**, the app's HTML                                                                                                   |
| `POST /hubs/layouts/negotiate` (no base)  | **200 from the upstream**: the proxy runs before the base middleware, so `/hubs` keeps working with the base set          |

**Decision.** A Vite `base` per instance is viable, and the fixed-port consequences are exactly two: the port's root redirects to the base
(which is all the kiosk and wall e2e specs navigate to: `goto('/')` or `http://localhost:5175/`), and an un-prefixed deep link on the port
is a 404. No kiosk or wall e2e spec navigates to an un-prefixed deep link or asserts a kiosk URL path (searched: `goto(`, `toHaveURL`,
`waitForURL`, `pathname` across `e2e/kiosk-*`, `e2e/wall-*`, `spanning-wall`, `rule-switches-a-wall`, `support/kiosk-session.ts`,
`support/wall-session.ts`), so the suite is expected to pass unmodified (spec FR-013). Two fixes follow from the table:

- **The dev font preload must carry the base** (spec FR-011). Without it the preload is a 404 on the instance's own port and, at the gateway
  origin, reaches the shell's dev server. `fontPreload.ts` reads `config.base` in `serve`; with base `/` (the shell) the output is
  unchanged.
- **The gateway must redirect `/kiosk` and `/wall`** to the slashed path itself, because Vite will not (spec Edge Cases).

**Not observed:** a `vite build` with the base. Spec 491 observed that a build with a `base` emits every asset under it (T021); the kiosk
build is checked in the quickstart and by a build-output test (plan W9).

## R2. How does the kiosk reach the hub from the one origin without the gateway and without an extra hop? (FR-008)

**Read.** In dev the kiosk resolves the hub to the page-relative `/hubs/layouts` (`apps/shared/src/realtime/hubUrl.ts`), and
`VITE_LAYOUT_HUB_URL`, when set, is used as given in dev as well as in production. From `:5180/kiosk/` the relative path would reach the
gateway, whose lowest-order `ui-shell` route forwards it to the shell's Vite server (no `/hubs` proxy there). `LayoutComposition` has no CORS
policy (`AddCors`/`UseCors` appear only in `src/ApiGateway`). Today's route from `:5174` is: browser → the instance's Vite `/hubs` proxy →
`layout-composition`.

**Observed**, same spike, the Vite `/hubs` proxy asked from a foreign origin:

| Request to the Vite server (5190)                                                                                                                                                                             | Answer                                                                                                                           |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `OPTIONS /hubs/layouts/negotiate`, `Origin: http://localhost:5180`, `Access-Control-Request-Method: POST`, `Access-Control-Request-Headers: authorization,content-type,x-requested-with,x-signalr-user-agent` | **204**, `Access-Control-Allow-Origin: http://localhost:5180`, the four headers allowed                                          |
| `POST /hubs/layouts/negotiate`, `Origin: http://localhost:5180`, `Authorization: Bearer t`                                                                                                                    | **200 from the upstream**, `Access-Control-Allow-Origin: http://localhost:5180`; the upstream received `Authorization` unchanged |
| WebSocket upgrade `GET /hubs/layouts?id=abc`, `Origin: http://localhost:5180`                                                                                                                                 | **101** from the upstream (the proxy does not check the origin of a proxied socket)                                              |
| the same preflight with `Origin: http://fab.example`                                                                                                                                                          | **204 with no `Access-Control-Allow-Origin`**: a browser would refuse it                                                         |

That matches the source: Vite's default `server.cors` is `{ origin: defaultAllowedOrigins }`, the regex
`^https?://(?:(?:[^:]+\.)?localhost|127\.0\.0\.1|\[::1\])(?::\d+)?$` (`vite/dist/node/chunks/node.js:806`, `:25109`), and the CORS
middleware sits before the proxy.

**First proposal (superseded at the Plan gate):** an absolute hub URL at each instance's own Vite server, relying on the Vite CORS default
observed above. It kept today's two hops but left a dev-only residual (a forwarded host whose name is not `localhost` would be refused) and
did nothing for production, where the hub is on a different origin and `layout-composition` had no CORS policy at all. The spike stays
recorded because it is also evidence for what the replacement must admit (the four request headers, the WebSocket upgrade).

**Decision (owner, Plan gate, 2026-11-12): a CORS policy on `layout-composition`, scoped to the hub path, and the kiosk calls the hub
there directly.**

- **Which URL, which setting, who sets it.** The page-relative default `/hubs/layouts` cannot be used: from `:5180/kiosk/` it reaches the
  gateway (premise 4, and the gateway's `ui-shell` catch-all would hand it to the shell's dev server). The kiosk therefore gets an
  **absolute** hub URL through the **existing** `VITE_LAYOUT_HUB_URL`, which `hubUrl.ts` already honours in dev as in production. The
  **AppHost** sets it on both kiosk instances to `<layout-composition http endpoint>/hubs/layouts` (an endpoint reference, no literal
  port); in production the deploy layer already must set it (`docs/deployment-frontend-env.md`). No application code changes for the hub.
- **The Vite `/hubs` proxy stays as it is.** With `VITE_LAYOUT_HUB_URL` set it is no longer on the kiosk's path; it remains the fallback
  for a Vite server started outside Aspire (no `VITE_LAYOUT_HUB_URL`), and `AppHostWebAppHostingTests` asserts `VITE_LAYOUT_HUB_ORIGIN` is
  still injected. Removing it is a separate clean-up, not this slice.
- **Hop count.** Today from `:5174`: browser → Vite `/hubs` proxy → (Aspire's endpoint proxy) → `layout-composition`. After: browser →
  (Aspire's endpoint proxy) → `layout-composition`, from `:5174`, `:5175` and `:5180` alike. **One intermediary fewer in dev, none added
  anywhere; the gateway is not on it** (FR-008, SC-002). Production: browser → `layout-composition` at the absolute URL, as the production
  contract already says.
- **What CORS costs the event → overlay leg: nothing per message.** CORS acts on HTTP requests: the `negotiate` `POST` (pre-flighted,
  because it carries `Authorization` and SignalR's own headers) at connection set-up and on each reconnect. Pushes then travel as frames over
  the established WebSocket; there is no HTTP request per message, so no CORS evaluation and no preflight per push. The WebSocket upgrade
  itself is not subject to CORS. The degraded transports are HTTP: Server-Sent Events is one long-lived credentialed `GET` (no custom
  headers, no preflight); long polling issues a request per poll, which **would** pre-flight per poll, so the policy sets
  `Access-Control-Max-Age` and the browser reuses one preflight across polls and reconnects.
- **How that is shown** (plan W12): (1) structurally, the CORS middleware is mounted for the hub path only and before authentication, and the
  broadcaster, the hub class and their tests have no diff; (2) a red-first test pins the `Max-Age` header; (3) at Verify, the browser's
  network log for a kiosk receiving a burst of variable changes shows **one** `OPTIONS` per connection and **zero** during the burst; (4) the
  existing push integration tests (`OverlayPushIntegrationTests`, `SignalRRevocationIntegrationTests`, `ReconnectReconcileIntegrationTests`,
  .NET clients that send no `Origin`) pass unmodified. No new latency measurement is claimed; §IV's event → overlay figure stays as
  recorded (#1714 still open for the end-to-end re-measurement).

**Alternatives set aside:**

- **The instance's own Vite proxy at an absolute URL** (the first proposal): superseded by the owner for its forwarded-host residual and
  for leaving production uncovered.
- **A base-relative hub path (`/kiosk/hubs/layouts`) proxied by the gateway to the Vite server.** Two hops and the gateway on the leg:
  forbidden by ADR-0192 decision 3 and the issue.
- **A gateway route `/hubs/**` straight to `layout-composition`.** "The hub through the gateway", explicitly out of scope (#3246,
  ADR-0192 "Alternatives": deferred, needs its own ADR and a measurement).

**Found on the way, not in scope:** the console also opens the hub (`apps/management-web/src/features/walls/WallDetailPage.tsx`), with
the page-relative default, and `apps/management-web/vite.config.ts` has no `/hubs` proxy, so the wall-detail live updates in dev look
unreachable from `:5173` and `:5180` today. Not observed (no stack). Filed as a separate issue rather than fixed here; the same policy plus
`VITE_LAYOUT_HUB_URL` on `management-web` would be the obvious fix.

## R7. What must the hub's CORS policy admit for the SignalR browser client? (FR-008)

**Read**, the installed client `@microsoft/signalr` **10.0.11** (`node_modules/.pnpm/@microsoft+signalr@10.0.11/.../dist/esm`; read only,
not changed):

| Source                                                        | What it sends                                                                                    | Policy consequence                                                                                  |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| `HttpConnection.js:24-25`, `XhrHttpClient.js:27`              | `withCredentials` defaults to **true**                                                           | `Access-Control-Allow-Credentials: true`, so the allowed origin must be echoed, **never `*`**       |
| `XhrHttpClient.js:28`, `FetchHttpClient.js:96`                | `X-Requested-With: XMLHttpRequest`                                                               | allowed request header                                                                              |
| `Utils.js:160`                                                | `X-SignalR-User-Agent`                                                                           | allowed request header                                                                              |
| `layoutHub.ts` `accessTokenFactory`                           | `Authorization: Bearer …` on HTTP requests                                                       | allowed request header                                                                              |
| `WebSocketTransport.js:50`, `ServerSentEventsTransport.js:24` | the token as the `access_token` query value                                                      | nothing for CORS; `Program.cs`'s existing `OnMessageReceived` hook already reads it on the hub path |
| negotiate, long polling                                       | `POST` (negotiate, sends), `GET` (polls), `DELETE` (long-polling close); `Content-Type` on sends | methods `GET`, `POST`, `DELETE`; allowed request header `Content-Type`                              |

**Decision.** The policy names exactly those: the configured origins, credentials allowed, the four headers, the three methods, and a
preflight max-age (proposed 10 minutes: long enough to cover reconnect storms and long polling, short enough that removing an origin takes
effect within a shift). It is mounted **only** for requests whose path starts with `LayoutLifecycleHub.Path`, the same scoping the
`access_token` hook uses, so `layout-composition`'s REST endpoints, which the browser reaches through the gateway, gain no CORS answer.

## R8. How is it wired, and what in the guards could it trip?

**Read.** The only CORS wiring in the repo is `src/ApiGateway/GatewayHost.cs` `AddGatewayCors`: a named policy, origins from
configuration (`Cors:AllowedOrigins`), `WithOrigins(...)`, added through a builder extension. Per-context composition is
`Add<Context>{Infrastructure,Api}` (ADR-0051). `LayoutLifecycleHub` and its `Path` live in `LayoutComposition.Infrastructure/Broadcasting`;
no test project references `LayoutComposition.Api`, while `Integration.Tests` references `LayoutComposition.Infrastructure`.

**Decision.**

- **Where:** `src/LayoutComposition/Infrastructure/Broadcasting/LayoutHubCors.cs`, beside the hub it serves, with two extensions:
  `AddLayoutHubCors(this WebApplicationBuilder)` (reads `LayoutHub:Cors:AllowedOrigins`, refuses any entry containing `*` with an
  exception naming the key, registers the named policy) and `UseLayoutHubCors(this WebApplication)` (`UseWhen` on the hub path →
  `UseCors(policy)`). `Program.cs` gains one call each, `UseLayoutHubCors` **before** `UseAuthentication`, so a preflight (which carries no
  token) is answered by CORS rather than refused by the hub's `[Authorize]`. Mirrors the gateway's shape; no new library (CORS is in the
  shared framework `LayoutComposition.Infrastructure` already references).
- **Configuration:** `LayoutHub:Cors:AllowedOrigins` (array). Absent or empty: no origin admitted, i.e. today's behaviour, so every lane
  that does not run the kiosks (the integration fixture) is unchanged. The AppHost sets it inside `AddWebApplications`, where the kiosk
  instances are declared, from endpoint references: `kiosk-web`, `kiosk-wall`, and the gateway's endpoint (the one origin, 5180 in run
  mode). That covers the developer stack and the CI e2e job, whose kiosk specs at `:5174`/`:5175` then exercise the cross-origin hub for
  real. Production: the deploy layer sets the public origin(s); no wildcard.
- **Guards checked:**
  - `BoundaryTests`: the SignalR/WebSockets ban applies to the gateway, not to `layout-composition`; the context-isolation rule is
    unaffected (no new reference); `Domain_layer_does_not_reference_infrastructure_frameworks` is unaffected (Infrastructure, not Domain).
  - `EndpointScopeDeclarationTests` parses `Program.cs`'s `Map*` chains and registers `MapHub<LayoutLifecycleHub>("/hubs/layouts")` as a
    route outside the chain. The plan **does not touch that line** (no `.RequireCors(...)` on `MapHub`), so the register's row still matches.
  - `ServedHealthHostCompletenessTests` reads each host's `Program.cs` for `MapDefaultEndpoints(`: unchanged.
  - `HealthChecks/ComposedHost.cs` composes hosts in-process; with no `LayoutHub:Cors` section the new extension registers a policy with no
    origins and throws nothing.
  - Coverage gate (ADR-0065): the new file is Infrastructure; its in-process test covers it.

## R3. Does the realm already allow sign-in at `http://localhost:5180` for the kiosk clients? (FR-007)

**Read**, `src/AppHost/Realms/smart-sentinel-eye-realm.json`: `kiosk-web` has `redirectUris` `http://localhost:5174/*`, `http://localhost:*`;
`kiosk-wall` has `http://localhost:5175/*`, `http://localhost:*`; both `webOrigins: ["+"]`. The redirect itself is covered by the wildcard.

**Not established, and not observable here** (Docker is not running on this machine and the owner asked that no stack be booted while
another agent works): whether `"+"` turns `http://localhost:*` into an allowed origin for the token endpoint's CORS check. `"+"` derives
web origins **from** the redirect URIs; for `http://localhost:5174/*` that gives `http://localhost:5174`, which is why the fixed ports work.
What it gives for a wildcard **port** is the question. If it gives nothing usable, the browser's code-for-token `POST` from `:5180` fails
CORS after a successful login. **The same question applies to `management-web` at `:5180` (spec 491)**, whose signed-in console through the
one origin is also unobserved (491 verification, T018).

**Decision (owner, Plan gate, 2026-11-12).** Observe at Verify: sign in at `/kiosk/` and `/wall/` on a running stack and read the token
request. If it fails, a **separate issue** is filed against spec 491 and every app on the one origin (the console, the kiosk, the wall);
the realm is **not** changed in this spec. The likely fix, for whoever takes that issue: explicit `http://localhost:5180/*` entries, no
`description` change (the 255-character import limit), and the Keycloak volume deleted for it to apply.

**Alternative set aside:** adding the entries now. It would force every developer to delete their Keycloak volume for a change that may be
unnecessary, and it would fix the console's twin defect from inside the kiosk's spec.

## R4. Where does the instance's base come from, and how do the router and the OIDC callback learn it?

**Read.** The wall is selected by `VITE_KIOSK_MODE=wall` (`auth.ts:27`, AppHost `AddKioskWall`), and spec 052's rule is "one flag decides
both" client and scopes. Vite exposes the resolved `base` to the app as `import.meta.env.BASE_URL`. `vite.config.ts` already reads
`process.env` (PORT, the hub origin).

**Decision.**

- `vite.config.ts` derives `base` from the mode flag: `wall` → `/wall/`, anything else → `/kiosk/`, through one small pure function
  (`kioskBase(mode)`) that the config imports, read from `process.env` merged with `loadEnv(mode, cwd, 'VITE_')`, so a `.env` file that sets
  the flag for a build sets the base too. **No new setting.**
- The application reads its base **only** from `import.meta.env.BASE_URL`, never from the flag again: whatever Vite served it under is the
  truth, so the router, the callback and the asset URLs cannot disagree.
- **Vitest keeps base `/`**: the config sets the derived base only when not under Vitest (`process.env.VITEST`). The 39 existing kiosk test
  files were written against `/`, they are this phase's characterisation, and a base change under them would move their environment rather
  than their subject. New tests reach the base-aware logic through pure functions that take the base as an argument (plan W7, W8).

**Alternatives set aside:** a separate `VITE_BASE`/`--base` per instance (a second setting that can disagree with the mode: exactly what
spec 052 removed); deriving the base at runtime from `window.location` (Vite's asset URLs are fixed at serve/build time, so a runtime guess
cannot move them).

## R5. Gateway: how do `/kiosk/` and `/wall/` fit `UiHosting` without breaking spec 491's suites, and what does 493 change underneath?

**Read**, `src/ApiGateway/UiHosting.cs` (develop) and `origin/493-gateway-edge-cases:specs/493-gateway-edge-cases/spec.md` (493's plan was
not yet pushed on 2026-11-12).

- **Proxy mode** loads three routes (`ui-remote-cameras`, `ui-remote-event-ingestion`, `ui-shell` at Order 1000) and three required
  addresses `Ui:Proxy:{Shell,Cameras,EventIngestion}`. `GatewayUiProxyTests`/`UiProxyStack.cs` configure exactly those three.
- **Static mode** requires `Ui:Static:{ShellRoot,CamerasRoot,EventIngestionRoot}`; `GatewayUiStaticTests` configure exactly those three.
- `MapGatewayUi` maps `/api/{**path}` and `/remotes/{**path}` to 404 and, in Static mode, the shell fallback (`MapShellFallback`).
- **493 changes, in the same file:** `MapShellFallback` (a page navigation with a dotted last segment gets the shell; an extension-bearing
  non-navigation stays 404; `Cache-Control: no-cache` on every response serving `index.html`) and the static-file options (`no-cache` on
  `remoteEntry.js` and a directly requested `index.html`). In `GatewayHost.cs`: the `streams/authorize` carve-out for every method.

**Decision.**

- **The kiosk and wall settings are required** (owner, Plan gate, 2026-11-12, overriding the first proposal of optional keys):
  `Ui:Proxy:Kiosk` and `Ui:Proxy:Wall` in proxy mode, `Ui:Static:KioskRoot` and `Ui:Static:WallRoot` in static mode, read and validated
  where the shell's and remotes' are (`ProxyClusters` → `RequiredAddress`; `StaticRoots.Read` → `Root` with entry file `index.html`). A
  missing or invalid one stops the gateway at start with the same message shape, naming the key. There is no unconfigured-prefix state.
- **Consequence for spec 491's suites:** their fixtures (`UiProxyStack.cs`; the `StaticUiStack` nested in `GatewayUiStaticTests.cs`, which
  builds temp roots) must supply the two new settings, or every UI-hosted test fails at start. That edit is its **own commit**, adds settings
  only, and changes no assertion; the owner relaxed "unmodified" for fixtures only. The existing fail-fast theory over the three roots
  (`GatewayUiStaticTests` `[InlineData]` rows) is **not** extended; the kiosk and wall rows go in the new test class.
- **The AppHost must set both proxy destinations wherever it sets `Ui__Mode`** (`WithOneOriginUi`), in the same commit as the gateway
  change, or the run-mode gateway would refuse to start.
- **The per-instance fallback is 493's shell fallback, generalised, not copied.** After 493 merges, its fallback rule (navigation-aware,
  `no-cache`, `nosniff`) is extracted into one helper that takes a route prefix and an `index.html` path, as a behaviour-preserving step whose
  characterisation is 493's own tests, unmodified. The shell calls it at `/`; the kiosk at `/kiosk/{**path}`, the wall at `/wall/{**path}`.
- **The kiosk's and the wall's `index.html` get `no-cache`** by the same rule (they are fixed-name entry points that name hashed chunks,
  which is 493's reason). The kiosk has no `remoteEntry.js`.
- **`/kiosk` and `/wall` without a slash** answer a redirect to the slashed path in both modes (R1: Vite does not).
- **No rate-limit or CORS policy** on the new UI routes (ADR-0192 decision 8, spec 491 D5); static files keep running before the limiter.

## R6. What stays exactly as it is, and how is that shown?

- **REST**: `VITE_API_GATEWAY_URL` on both instances stays the gateway's endpoint (Q1, option A). From `/kiosk/` the calls are same-origin
  `fetch`es to bare routes; spec 491's navigation rule passes them (`Sec-Fetch-Dest` is `empty`, not `document`).
- **WHEP**: the kiosk posts to the server-provided `whepUrl` (`apps/shared/src/streaming/WhepClient.ts`). `src/AppHost/Resources/mediamtx.yml`
  sets no allowed origins, so MediaMTX's default applies. Observed at Verify (plan W14), not assumed.
- **e2e hub interception**: `kiosk-live-updates.spec.ts` aborts `**/hubs/**`, a glob that matches the absolute hub URL on
  `layout-composition` as it matched the relative one.
- **The kiosk and wall e2e specs** now open the hub cross-origin at `layout-composition` from `:5174`/`:5175`; they pass unmodified only if
  the AppHost's allowed origins and the policy are right, which makes CI's `kiosk` and `wall` projects the full-stack evidence for R2/R7.
- **The kiosk's REST e2e** (`kiosk-latency-contract`) captures the gateway origin from a kiosk request and calls bare paths on it:
  unchanged, because the REST base is unchanged.
