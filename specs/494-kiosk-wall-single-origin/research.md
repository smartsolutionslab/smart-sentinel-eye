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
build is checked in the quickstart and by a build-output test (plan W7).

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

**Decision: an absolute hub URL at the instance's own Vite server.** The AppHost sets `VITE_LAYOUT_HUB_URL` on `kiosk-web` to
`<kiosk-web's own http endpoint>/hubs/layouts` and on `kiosk-wall` to `<kiosk-wall's own endpoint>/hubs/layouts`. The `/hubs` proxy in
`vite.config.ts` is unchanged and still built from `VITE_LAYOUT_HUB_ORIGIN`.

- **Hop count, from either page origin:** browser → the instance's Vite `/hubs` proxy → `layout-composition`. Identical to today from
  `:5174`/`:5175`; the gateway is not on it (spec FR-008, SC-002). The event → overlay leg is unchanged.
- **No application code changes for the hub.** `hubUrl.ts` already honours the variable in dev; `@microsoft/signalr`, the hub server and
  `BoundaryTests` are untouched.
- **Bounded to dev on localhost**, by the default CORS regex. Production is unchanged: `VITE_LAYOUT_HUB_URL` is already required and
  absolute there (`docs/deployment-frontend-env.md`).

**Alternatives set aside:**

- **A CORS policy on `layout-composition`, and the hub URL straight at the service.** One hop fewer, but it changes a service on the
  event → overlay path and adds an origin list there that production would then have to own; it also moves production's open question
  (the hub on a different origin) into this phase. ADR-0036: smallest change.
- **A base-relative hub path (`/kiosk/hubs/layouts`) proxied by the gateway to the Vite server.** Two hops and the gateway on the leg:
  forbidden by ADR-0192 decision 3 and the issue.
- **A gateway route `/hubs/**` straight to `layout-composition`.** Same hop count as today, but it is "the hub through the gateway",
  explicitly out of scope (#3246, ADR-0192 "Alternatives": deferred, needs its own ADR and a measurement).

**Residual risk, stated:** a page opened through a forwarded host whose name is not `localhost` (a remote dev box reached by IP, a
Codespace URL) would refuse the cross-origin hub call, where the relative path used to work at `:5174`. The devcontainer forwards
`localhost` ports (`.devcontainer/devcontainer.json`), so the supported dev set-ups are covered; the risk is recorded in
`docs/deployment-frontend-env.md`.

## R3. Does the realm already allow sign-in at `http://localhost:5180` for the kiosk clients? (FR-007)

**Read**, `src/AppHost/Realms/smart-sentinel-eye-realm.json`: `kiosk-web` has `redirectUris` `http://localhost:5174/*`, `http://localhost:*`;
`kiosk-wall` has `http://localhost:5175/*`, `http://localhost:*`; both `webOrigins: ["+"]`. The redirect itself is covered by the wildcard.

**Not established, and not observable here** (Docker is not running on this machine and the owner asked that no stack be booted while
another agent works): whether `"+"` turns `http://localhost:*` into an allowed origin for the token endpoint's CORS check. `"+"` derives
web origins **from** the redirect URIs; for `http://localhost:5174/*` that gives `http://localhost:5174`, which is why the fixed ports work.
What it gives for a wildcard **port** is the question. If it gives nothing usable, the browser's code-for-token `POST` from `:5180` fails
CORS after a successful login. **The same question applies to `management-web` at `:5180` (spec 491)**, whose signed-in console through the
one origin is also unobserved (491 verification, T018).

**Decision.** Observe first (plan W9, at Verify): sign in at `/kiosk/` and `/wall/` on a running stack and read the token request. If it
fails, add `http://localhost:5180/*` to both kiosk clients' `redirectUris` (that makes `"+"` yield `http://localhost:5180`), with no
`description` change (the 255-character import limit), and record that the Keycloak volume must be deleted for the change to apply. If it
fails, the console client has the same defect: it is **filed as a separate issue against spec 491**, not fixed here (scope).

**Alternative set aside:** adding the entries unconditionally now. It would force every developer to delete their Keycloak volume for a
change that may be unnecessary, and an unobserved fix is a guess (ADR-0036: define done by an observation).

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
  than their subject. New tests reach the base-aware logic through pure functions that take the base as an argument (plan W5, W6).

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

- **The kiosk and wall addresses and roots are optional**, each independently: `Ui:Proxy:Kiosk`, `Ui:Proxy:Wall`, `Ui:Static:KioskRoot`,
  `Ui:Static:WallRoot`. Absent, that prefix is a **404, never the shell**. Required keys would make every one of spec 491's proxy and static
  tests fail at start, i.e. edit a characterisation; optional keys leave them untouched. Present but invalid (a missing directory, no
  `index.html`) still stops the gateway at start, naming the key (spec FR-003).
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
  sets no allowed origins, so MediaMTX's default applies. Observed at Verify (W9), not assumed.
- **e2e hub interception**: `kiosk-live-updates.spec.ts` aborts `**/hubs/**`, a glob that matches the absolute hub URL as it matched the
  relative one.
- **The kiosk's REST e2e** (`kiosk-latency-contract`) captures the gateway origin from a kiosk request and calls bare paths on it:
  unchanged, because the REST base is unchanged.
