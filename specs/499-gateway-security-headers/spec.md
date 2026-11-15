# Feature Specification: Security headers and a Content-Security-Policy for the gateway's HTML origin

**Feature Branch**: `feat/3252-gateway-csp`

**Created**: 2026-11-15

**Status**: Draft for the Plan gate; no `[NEEDS CLARIFICATION]` markers. Three choices the issue leaves open are decided here and listed
under Assumptions, because the autonomous lane cannot ask (A1–A4).

**Issue**: #3252, follow-up to epic #3248. **Governing decision**: ADR-0192, Consequences (negative): "the gateway becomes an HTML origin:
it needs security headers and a Content-Security-Policy it never needed as a JSON proxy". **Builds on**: spec 491 plan D9 (which shipped only
`X-Content-Type-Options: nosniff` on gateway-served static files and filed this issue), spec 494 (kiosk and wall at `/kiosk/` and `/wall/`),
spec 497 (the e2e suite runs at the one origin). **Also cites**: ADR-0106 (gateway as the edge; TLS at the deploy edge), ADR-0080 (browser
OIDC direct to Keycloak), ADR-0152 (SignalR hub, direct), ADR-0011/0012 (WHEP media, direct), ADR-0108 (Playwright), ADR-0103 (no
Testcontainers), ADR-0139/0144 (test colour, autonomous lane). **Lane**: autonomous (ADR-0144).

**Latency budget (§IV): legs touched: none.** The change adds response headers to HTML documents the gateway already serves. No
event-to-overlay, media, decode or presentation-buffer code moves; the hub and WHEP stay direct and the gateway gains no SignalR, WebSocket
or `Http.Connections` dependency (`BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` unmodified). The one way
this feature could touch a leg is by **availability, not latency**: a `connect-src` that omits the hub origin would stop the browser from
reaching the hub at all, which silences the event-to-overlay leg. That is precisely the failure FR-008 and SC-002 exist to catch.

## Premises checked against the tree (2026-11-15, `origin/develop` at `46af2b65`)

The issue was read, not trusted. Each claim below was checked against the code.

| #   | Claim                                                                                | Verdict                                            | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| --- | ------------------------------------------------------------------------------------ | -------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P1  | The gateway sends no CSP or security header beyond `nosniff`                         | **True**                                           | `src/ApiGateway/UiRoot.cs` sets only `X-Content-Type-Options: nosniff` (static files, `OnPrepareResponse`) and on the fallback `index.html`. No CSP, `X-Frame-Options`, `Referrer-Policy` or `Permissions-Policy` anywhere in `src` or `apps`. No ADR or constitution text mentions CSP or security headers (grep of `docs/adr`, constitution).                                                                                                                                                                                                                                                                                                                                                                                                                |
| P2  | Kiosk and wall paths are out of scope (#3246)                                        | **Stale**                                          | #3246 landed (spec 494): `UiHosting.cs` proxies `/kiosk/` and `/wall/` in `Proxy` mode and serves `Ui:Static:KioskRoot`/`WallRoot` in `Static` mode. They share the origin with the console, so **they are in scope**: a CSP is per document, and a same-origin page without one is the weak point for the whole origin.                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| P3  | `Proxy` mode is dev; `Static` mode is production                                     | **True**                                           | `src/AppHost/WebApplications.cs` `WithOneOriginUi` sets `Ui__Mode=Proxy` in run mode (dev and CI's e2e lane, port 5180). `Static` is wired nowhere in the AppHost; it is exercised only by the in-process `GatewayUiStaticTests`/`GatewayKioskUiStaticTests`. There is no production deployment (ADR-0192 decision 10).                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| P4  | `connect-src` needs the gateway, Keycloak, the hub and the WHEP/media origin         | **True, and those are all**                        | Every browser network call traced: REST via `apps/shared/src/api/gateway.ts` (the gateway, `/api` or the gateway's absolute origin = the page's origin at `:5180`); OIDC token/userinfo/discovery via `oidc-client-ts` to `VITE_KEYCLOAK_URL`; the hub via `VITE_LAYOUT_HUB_URL` (absolute, `layout-composition`, HTTP negotiate **and** a `ws://` upgrade); WHEP `POST`/`DELETE` in `apps/shared/src/streaming/WhepClient.ts` to the `whepUrl` the stream-distribution API returns (`MediaMtxWhepUrlBuilder`, base `services:mediamtx:whep:0`); the shell's `nav-manifest.json` fetch and `kioskLatency.ts` beacon go to the page's own origin. No `EventSource`, `sendBeacon`, raw `WebSocket`, worker, OTLP exporter or third-party origin in `apps/*/src`. |
| P5  | The WHEP origin reaches the browser through the API, not through a `VITE_*` variable | **True**                                           | The `whepUrl` field of `apps/shared/src/api/streams.api.ts` responses. A guard that only reads the apps' environment would miss it, so FR-008 names it separately.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| P6  | The apps use inline styles                                                           | **True in two forms, and only one matters**        | React `style={{…}}` props (e.g. `TileFrame.tsx`) are applied through CSSOM and are **not** subject to CSP. But Radix Dialog/AlertDialog pull `react-remove-scroll-bar` → `react-style-singleton`, which **injects `<style>` elements** at runtime (no nonce is plumbed: `get-nonce` reads a webpack global Vite never sets), and Vite dev injects every CSS import as a `<style>` element. Both need `style-src 'unsafe-inline'`. Tailwind compiles to a file (`'self'`).                                                                                                                                                                                                                                                                                      |
| P7  | Built bundles contain no inline script                                               | **True for the bundles on disk; to be re-checked** | `apps/kiosk-web/dist/index.html` (stale, 2026-11-11) and `apps/management-cameras/dist/index.html` load every script by `src`. `@module-federation/vite` 1.2 injects an inline `<script type="importmap">` only under `experiments.hostProvidesAllShared`, which no `vite.config.ts` sets. `@module-federation/runtime-core` uses `new Function` only when `FEDERATION_ALLOW_NEW_FUNCTION` is defined; otherwise plain `import()`. No `eval`/`new Function`/`innerHTML`/`dangerouslySetInnerHTML` in `apps/*/src`. CI never builds the bundles (`.github/workflows` has no `vite build`), so the plan re-checks this with a fresh build (T001).                                                                                                                |
| P8  | Dev (`Proxy` mode) needs a different script policy                                   | **True**                                           | `@vitejs/plugin-react` 6.2.1 `transformIndexHtml` injects an inline React-refresh preamble module script; `@module-federation/vite` injects inline module scripts in dev (`FEDERATION_MODULE_CACHE_CLEAR_SCRIPT`, a refresh-runtime shim). Hashes are not stable across versions, and the gateway does not rewrite proxied HTML, so a nonce is not available. Dev needs `script-src 'unsafe-inline'`.                                                                                                                                                                                                                                                                                                                                                          |
| P9  | Vite's hot-reload socket needs a `connect-src` entry                                 | **Covered by `'self'`**                            | No `server.hmr` is configured, so Vite's client connects to `ws://<page host>/<base>`, i.e. the gateway at `:5180` (spec 491 proved HMR through the gateway). CSP Level 3 matches `'self'` for `ws:` to the page's host. Verified in a browser by T001 rather than assumed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| P10 | Images come only from the origin                                                     | **False: `data:` is used**                         | `apps/shared/src/ui/composites/FrameGrabber.tsx` produces stills with `canvas.toDataURL('image/png')`, rendered as `<img src>` in the console's tile view (`TileFrame.tsx`). `img-src` needs `data:`. Fonts are self-hosted (`self-hosted-fonts.spec.ts`, `/assets/*.woff2`).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| P11 | Something frames the apps                                                            | **Only possibly our own silent-renew page**        | No `<iframe>` in `apps/*/src`. `oidc-client-ts` defaults `automaticSilentRenew: true`; it renews with the refresh token when one exists (Keycloak issues one to these public clients) and falls back to an iframe on the app's own `redirect_uri` otherwise. That iframe would be framed by our own origin, so `frame-ancestors 'self'`, not `'none'`.                                                                                                                                                                                                                                                                                                                                                                                                         |
| P12 | Browser features the apps need                                                       | **None that a policy would deny**                  | No `getUserMedia`, `getDisplayMedia`, `requestFullscreen`, wake lock, geolocation, clipboard, notifications or service worker in `apps/*/src`. WHEP is receive-only and needs no camera or microphone permission.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| P13 | HSTS is possible now                                                                 | **No**                                             | The gateway is plain HTTP in every environment that exists (`GatewayHost.cs`: TLS terminates at the deploy edge, ADR-0106; there is no edge yet, ADR-0192 decision 10). HSTS over HTTP is ignored by browsers and would be misleading here. Deferred to epic phase 4 with TLS.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |

## What a CSP does and does not fix here (the reviewer's comment)

The owner's reviewer noted that with the console, kiosk and wall on one origin, a script-injection bug in one app can act as the others:
open a hidden iframe to Keycloak's authorize endpoint with `client_id=management-web&prompt=none`, or read the wall's `offline_access`
refresh token from `localStorage`. This spec's honest position:

- **What this policy does**: `script-src 'self'` (production) removes inline and `eval` script execution, the usual way an injection becomes
  code; `frame-src` falling back to `default-src 'self'` stops our HTML pages from framing Keycloak, which raises the bar for the hidden-iframe
  path the reviewer describes but does not close it (see below); `frame-ancestors 'self'` stops another site from framing ours; `base-uri 'none'`
  and `object-src 'none'` close the two classic CSP bypasses.
- **What it cannot do**: same-origin pages share `localStorage` and can script each other. The headers are HTML-only (FR-001), so a
  same-origin iframe of a response that carries no policy (an `/api` JSON answer, an asset, `favicon.svg`) is a stepping stone that the
  policy of the framing page does not stop from being framed, and from there reaching Keycloak is not governed. A popup or top-level navigation to Keycloak with
  `prompt=none` is not governed by CSP, and the code comes back to a `redirect_uri` on our own origin. **CSP is not isolation.** The only
  isolation is a separate origin for the kiosk and the wall, which reverses part of ADR-0192 decision 1 and is **an ADR-level change this
  spec does not make** (FR-014). Per-path policies for `/kiosk/` and `/wall/` would add nothing for the same reason, so there is one policy.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - Every page the gateway serves carries an enforcing policy that names exactly what the apps use (Priority: P1)

An operator opens the console, a kiosk or a wall display at the gateway's address. Every HTML document arrives with a
`Content-Security-Policy` that allows the gateway itself, Keycloak, the layout hub (HTTP and WebSocket) and the WHEP media origin, and
nothing else, plus the companion headers. The apps work exactly as before: sign-in, REST, live updates over the hub, live video over WHEP,
the federated remotes, the captured stills.

**Why this priority**: it is the issue. Without it the gateway is an HTML origin with no browser-side defence (ADR-0192).

**Independent Test**: start the dev stack (`aspire run`), open `http://localhost:5180/`, `/cameras` (a federated remote route),
`/kiosk/` on a published live-video layout and `/wall/`; read the response headers in DevTools and confirm zero CSP violations in the
console while sign-in, a REST list, a live tile and a hub push all work. Automated by the e2e spec in FR-010.

**Acceptance Scenarios**:

1. **Given** the gateway runs with `Ui:Mode=Static` and all three origin settings, **When** a browser requests `/`, a deep link such as
   `/layouts/123`, `/kiosk/` or `/wall/`, **Then** the response carries `Content-Security-Policy` with the production directives of FR-002, and
   `connect-src` lists `'self'`, the identity-provider origin, the hub origin and its `ws`/`wss` twin, and the media origin.
2. **Given** the gateway runs with `Ui:Mode=Proxy`, **When** a browser requests any page the dev servers answer with `text/html`, **Then**
   the response carries the same policy except that `script-src` adds `'unsafe-inline'` (FR-003), and Vite's hot-reload socket still
   connects.
3. **Given** either mode, **When** the response is not HTML (a JS chunk, a CSS file, a font, `/api/...` JSON), **Then** no
   `Content-Security-Policy` header is added by the gateway, and REST responses are byte-for-byte as before.
4. **Given** either mode, **When** an HTML page is served, **Then** it also carries `X-Content-Type-Options: nosniff`,
   `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy: same-origin` and the `Permissions-Policy` of FR-005; it carries **no**
   `Strict-Transport-Security`.
5. **Given** `Ui:Mode` is unset (the integration stack's gateway, REST only), **When** any request is served, **Then** no header of this
   feature is added (the JSON proxy is unchanged).
6. **Given** the upstream dev server already sent a `Content-Security-Policy`, **When** the gateway forwards the page, **Then** the gateway's
   policy replaces it (one policy per document, the gateway's).

### User Story 2 - A missing origin fails a test, not a fab (Priority: P1)

A developer adds a new browser-facing origin to an app (a new `VITE_*` endpoint, or a new direct service the browser calls) or drops one from
the gateway's wiring. A test goes red, naming the origin, before the browser ever blocks it in production.

**Why this priority**: the issue's own acceptance criterion: "a test that fails when an origin the apps use is absent from `connect-src`".
A permissive or stale policy is decoration; one that silently omits the hub silences every overlay.

**Independent Test**: delete the gateway's media-origin wiring in `src/AppHost/WebApplications.cs`, run the AppHost model test; it fails
naming `mediamtx`/`whep`. Restore it; green. Add a dummy `VITE_X` endpoint reference to `kiosk-web`; red naming it.

**Acceptance Scenarios**:

1. **Given** the run-mode AppHost model, **When** the guard collects every endpoint any browser app resource is handed in its environment
   (except the gateway's own, which is `'self'`) plus the WHEP endpoint the stream-distribution API hands browsers, **Then** each has a
   matching entry in the gateway's content-security-policy settings, compared as resource and endpoint name, never as a resolved string.
2. **Given** one of those origins is not wired to the gateway, **When** the guard runs, **Then** it fails and its message names the resource
   and endpoint that is missing.
3. **Given** the gateway is started with `Ui:Mode` set and any of the three origin settings absent, empty, relative, non-HTTP(S) or carrying
   anything beyond scheme, host and port that cannot be reduced to an origin, **Then** it refuses to start with an error naming the setting
   (same shape as today's `Ui:Proxy:*`/`Ui:Static:*` guards).
4. **Given** a configured value carries a path (e.g. the hub URL `…/hubs/layouts`) or a trailing slash, **When** the policy is built,
   **Then** only its origin appears in `connect-src`. **Given** a value containing `;`, a quote or whitespace, **Then** the gateway refuses it
   rather than writing it into the header (header injection).

### User Story 3 - The whole e2e suite runs under the enforced policy (Priority: P2)

Because CI's e2e lane runs the gateway in `Proxy` mode at the one origin (spec 497), every existing Playwright spec now runs under the
enforced dev policy, and one new spec asserts there were no violations at all across the four surfaces.

**Why this priority**: the AppHost guard proves the wiring is complete against what the model knows; only a browser proves the policy is
right about what the apps actually do (an iframe fallback, a library's injected style, a fetch nobody traced).

**Independent Test**: `pnpm test:e2e --grep "content security policy"` against the dev stack.

**Acceptance Scenarios**:

1. **Given** the stack runs with the policy, **When** the new spec signs in at `/`, opens a federated remote route, opens `/kiosk/` on the
   seeded live-video layout until a frame is decoded and a hub push lands, and opens `/wall/`, **Then** zero `securitypolicyviolation`
   events fired in any of those documents and each document's response carried an enforcing `Content-Security-Policy`.
2. **Given** the existing e2e suite, **When** it runs under the policy, **Then** it passes with no spec, assertion or retry budget edited to
   accommodate the policy (characterisation, SC-004).

### Edge Cases

- **Keycloak's own pages** are a different origin and carry Keycloak's headers; this policy does not reach them, and sign-in is a top-level
  navigation, which CSP does not govern.
- **The silent-renew iframe fallback** (P11): if Keycloak ever stops issuing a refresh token, `oidc-client-ts` would frame Keycloak and
  the policy would block it. That is the reviewer's attack path, so the block is intended; the e2e session-survival specs
  (`wall-outlives-its-session`, `wall-survives-a-lockout`) would surface it as a failure, not a silent degradation.
- **The hub served on the gateway origin in production** (`docs/deployment-frontend-env.md` allows an Ingress `/hubs`): the hub setting then
  equals the gateway's origin; the duplicate in `connect-src` is harmless.
- **`ws:` vs `http:`**: CSP Level 3 does not match `ws://host` against an `http://host` source, so the hub is emitted in both schemes
  (`https` → `wss`). Only the hub, because only the hub upgrades; Keycloak and media get no WebSocket twin.
- **Fixed ports (`:5173`–`:5177`)** are the Vite servers themselves, not the gateway; they carry no policy. Out of scope, and ADR-0192
  decision 7 leaves them as a dev convenience.
- **A `HEAD` request** for an HTML document carries the headers (the content type is set without a body). **A `304` revalidation** of
  `index.html` (served `no-cache` with an ETag) may carry no `Content-Type`; the browser then keeps the stored `200`'s headers, which carried
  the policy. The in-process tests pin the `200` and `HEAD` cases; a `304` is not asserted.
- **The remotes' own `index.html`** (served under `/remotes/<name>/`) is HTML on our origin and gets the policy like any other document.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001** When `Ui:Mode` is set, the gateway MUST add the headers of FR-002–FR-006 to every response whose `Content-Type` is
  `text/html`, whichever route produced it (static file, SPA fallback, or proxied dev server), replacing any upstream value of the same
  header. Non-HTML responses MUST NOT get a `Content-Security-Policy` from the gateway. When `Ui:Mode` is unset the gateway MUST add none of
  them.
- **FR-002** The production (`Static`) policy MUST be exactly, in this order:
  `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self';
connect-src 'self' <identity-provider> <hub> <hub-ws> <media>; object-src 'none'; base-uri 'none'; form-action 'self';
frame-ancestors 'self'`. No `'unsafe-eval'`, no wildcard, no scheme-only source other than `data:` for images.
- **FR-003** The dev (`Proxy`) policy MUST equal FR-002 except `script-src 'self' 'unsafe-inline' 'unsafe-eval'` (P8, corrected: CI run
  43752365165 showed `eval` blocked under `'self' 'unsafe-inline'`; the source of the eval was not identified). It MUST be served as an
  **enforcing** `Content-Security-Policy`, not `-Report-Only` (A1).
- **FR-004** `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN` and `Referrer-Policy: same-origin` MUST accompany the policy.
  The existing `nosniff` on static files (spec 491) MUST stay.
- **FR-005** `Permissions-Policy: camera=(), microphone=(), geolocation=(), display-capture=(), payment=(), usb=()` MUST accompany the
  policy.
- **FR-006** `Strict-Transport-Security` MUST NOT be sent (P13). Deferred to epic phase 4.
- **FR-007** The three origins MUST come from configuration, `Ui:ContentSecurityPolicy:IdentityProvider`, `:LayoutHub` and `:Media`,
  required whenever `Ui:Mode` is set (both modes), each an absolute `http`/`https` URL reduced to its origin; anything else stops the gateway
  at start with an error naming the key. The hub's origin is additionally emitted with `ws`/`wss`.
- **FR-008** A FixtureLogic test over the run-mode AppHost model MUST fail when any endpoint handed to a browser app resource (any
  `JavaScriptAppResource`'s `VITE_`-prefixed environment entry that is, or contains, an `EndpointReference`; Vite exposes only that prefix
  to browser code), other than the gateway's own, or the
  `mediamtx` `whep` endpoint handed to `stream-distribution`, has no matching endpoint reference among the gateway's
  `Ui__ContentSecurityPolicy__*` entries. Matching is by resource name and endpoint name, read raw and unresolved, as
  `AppHostKioskOneOriginTests` already does. The expected set MUST be derived from the browser apps' and stream-distribution's environments,
  never from the gateway's, so the test cannot check its own input.
- **FR-009** The run-mode AppHost MUST wire the three settings from endpoint references: Keycloak's `http` endpoint, `layout-composition`'s
  `http` endpoint, `mediamtx`'s `whep` endpoint, beside the existing `Ui__Mode=Proxy` wiring.
- **FR-010** A Playwright spec MUST assert, for the console root, a federated-remote route, `/kiosk/` with a decoded live tile and a hub
  push, and `/wall/`, that the document response carried an enforcing policy and that zero `securitypolicyviolation` events fired.
- **FR-011** `docs/deployment-frontend-env.md` MUST document the three new gateway settings next to the `VITE_*` origins they mirror, and
  say that a production deployment sets them to the same origins it builds into the bundles.
- **FR-012** The headers MUST be applied before the static-file middleware short-circuits, and MUST NOT put HTML or asset requests through
  the `per-source` rate limiter (ADR-0192 decision 8 unchanged).
- **FR-013** The gateway assembly MUST NOT gain a dependency on SignalR, WebSockets or `Http.Connections` (`BoundaryTests` unmodified).
- **FR-014** Origin isolation of the kiosk and the wall from the console is **out of scope** and MUST be recorded as a follow-up needing an
  ADR (it reverses part of ADR-0192 decision 1), not built and not silently dropped.

### Test colour (ADR-0139)

**Behaviour-changing → red** for FR-001–FR-010: the new gateway tests, the AppHost guard and the e2e spec are written first and observed
failing (no header today; no gateway settings today). **Characterisation → observed green, unmodified** for every existing gateway suite
(`GatewayUiStaticTests`, `GatewayKioskUiStaticTests`, `GatewayUiProxyTests`, `GatewayKioskUiProxyTests`, `GatewayNavigationRuleTests`,
`GatewayApiPrefixTests`, `GatewayHostCharacterisationTests`, `GatewayRoutingIntegrationTests`), the `AppHost*OneOrigin*` tests,
`BoundaryTests`, and the whole e2e suite. The only edit those suites may receive is the **fixture-only** addition of the three now-required
settings to `UiProxyStack` and `StaticUiStack` (no assertion, test method or `[InlineData]` changes), the precedent spec 494 T004 set (A3).

### Key Entities

- **Content-security-policy settings**: three named origins (identity provider, layout hub, media), configuration under
  `Ui:ContentSecurityPolicy`, validated at start.
- **Policy**: the header value, a pure function of the mode and the three origins.

## Success Criteria _(mandatory)_

- **SC-001** Every `text/html` response from a gateway with `Ui:Mode` set carries the FR-002/FR-003 policy and the FR-004/FR-005 headers,
  in both modes, for the shell, a deep link, a remote's HTML, `/kiosk/` and `/wall/` (in-process gateway tests).
- **SC-002** Removing any one of the three origin wirings from the AppHost turns the FR-008 guard red, naming it; adding a new endpoint
  reference to a browser app without wiring it turns it red too (counterfactuals recorded in `verification.md`).
- **SC-003** The FR-010 e2e spec observes zero CSP violations on all four surfaces in CI.
- **SC-004** The existing gateway suites, AppHost tests and the full e2e suite pass with no assertion modified.
- **SC-005** No REST (`/api/...` and bare-route) response changes, compared by header set in the in-process tests.

## Assumptions

- **A1 (decided, not asked): dev enforces rather than reports.** The issue asks whether dev should serve a report-only policy. Report-only
  never breaks anything, so it relies on someone reading violations; enforcing in `Proxy` mode makes CI's whole e2e suite (which runs at the
  one origin in `Proxy` mode, spec 497) a regression net for `connect-src`, which is the part that varies by deployment. The cost is that the
  dev policy is looser on scripts (`'unsafe-inline' 'unsafe-eval'`, P8 as corrected by CI run 43752365165: the first dev policy lacked
  `'unsafe-eval'` and the three new e2e specs went red on `securitypolicyviolation {blockedURI: "eval", violatedDirective: "script-src"}`
  on `/`, `/kiosk/` and `/wall/`; the apps still worked and the eval's source was not identified), so the strict production `script-src` is proved only by the in-process header test
  and T001's built-bundle check, not by a browser. Recorded as a known gap; closing it needs CI to build the bundles and run Playwright
  against `Static` mode, which is epic phase 4 territory.
- **A2 (decided): `style-src 'unsafe-inline'` in production.** Radix's scroll-lock injects `<style>` elements and no nonce can be
  plumbed through statically served HTML without the gateway templating `index.html`. Style injection is a far smaller risk than script
  injection; the alternative (templated nonces) is new gateway behaviour for a marginal gain and is not taken.
- **A3 (decided): fixture-only edit to two gateway fixtures.** Making the origins required (FR-007) means `UiProxyStack` and `StaticUiStack`
  must supply them or every existing UI test fails at start. The edit adds configuration keys only. In the supervised lane the owner
  approved the identical move for spec 494 (T004); here it is declared up front so phase 6 can check that nothing else changed.
- **A4 (decided): `Referrer-Policy: same-origin`.** No cross-origin party (Keycloak, the hub, MediaMTX) needs our paths; our own gateway logs
  keep the referrer.
- **A5:** Keycloak keeps issuing refresh tokens to `management-web`, `kiosk-web` and `kiosk-wall` (true today), so silent renew never
  frames Keycloak. If it did, the policy blocks it on purpose (Edge Cases).
- **A6:** No reporting endpoint (`report-to`) is added: there is nowhere to send reports, and adding a collector is an observability decision
  (ADR-0118), not part of this issue.
- **Not in scope**: TLS, HSTS, the production edge and Ingress (epic phase 4); origin isolation for kiosks (FR-014, needs an ADR); Trusted
  Types; nonces/hashes; headers on the fixed dev ports.
