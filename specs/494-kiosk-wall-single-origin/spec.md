# Feature Specification: The kiosk and the wall display on one origin

**Feature Branch**: `494-kiosk-wall-single-origin`

**Created**: 2026-11-12

**Status**: Draft; one `[NEEDS CLARIFICATION]` open (Q1, the kiosk's REST base), with a recommended answer

**Issue**: #3246, phase 2 of epic #3248. **Governing decision**: ADR-0192 (accepted, amends ADR-0106). **Builds on**: spec 491 (#3245,
merged in #3253), whose path map reserves `/kiosk/` and `/wall/` for this phase. **Lane**: supervised (ADR-0037).

**Input**: User description: "Serve the kiosk and the wall display on the same origin: `/kiosk/` and `/wall/` (two instances of
`apps/kiosk-web`; the wall runs with `VITE_KIOSK_MODE=wall`). The kiosk OIDC `redirect_uri` must become base-aware and the two realm
clients need the new redirect URIs; the router needs a `basename` per instance; the SignalR hub stays direct (`VITE_LAYOUT_HUB_ORIGIN`,
ADR-0152) and must keep working under the new base; WebRTC/WHEP stays direct. Not in scope: Keycloak behind the gateway, the hub through
the gateway, `@microsoft/signalr`."

**Latency budget (§IV): legs touched: none.** Every leg is stated, because the kiosk is the screen the whole budget ends on:

| Leg                   | Why it does not move                                                                                                                                                 |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Camera → SFU          | Not a browser leg.                                                                                                                                                   |
| SFU → kiosk decode    | The kiosk opens WHEP at the absolute `whepUrl` the stream-distribution service returns; that URL is not relative to the page and does not pass the gateway (FR-009). |
| Presentation buffer   | In the kiosk, against the SFU's RTCP clock (ADR-0128); independent of where the page was loaded from.                                                                |
| Event → overlay state | The layout hub is reached **without passing the gateway, and through no more hops than today** (FR-008). `@microsoft/signalr` is not touched.                        |
| Composite + render    | In the kiosk; independent of the page's origin.                                                                                                                      |
| Headroom              | Untouched.                                                                                                                                                           |

`BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` stays in force **unmodified** (ADR-0192 decision 3). What does
pass the gateway is the page itself and its static files, which are not a §IV leg; that is bounded by SC-006.

## Premises checked against the tree (2026-11-12, `origin/develop` at `fe07298c`)

The issue was written before phase 1 merged. Each claim it makes was checked before anything was specified on it.

| #   | Claim in #3246                                                                                                       | Verdict                                | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| --- | -------------------------------------------------------------------------------------------------------------------- | -------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | The kiosk OIDC `redirect_uri` is `${window.location.origin}/oidc/callback` at `auth.ts:76`                           | **True** (lines 76–77)                 | `apps/kiosk-web/src/app/auth.ts`. Under `/kiosk/` it would return the browser to `/oidc/callback` at the origin root, which the gateway answers with the **shell**.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| 2   | The two realm clients (`kiosk-web`, `kiosk-wall`) need the new redirect URIs                                         | **False for dev; open for one detail** | `src/AppHost/Realms/smart-sentinel-eye-realm.json`: both clients already list `http://localhost:*` in `redirectUris`, which admits `http://localhost:5180/kiosk/oidc/callback` and `/wall/oidc/callback`. Spec 491 relied on the same wildcard for `management-web`. **Open:** `webOrigins` is `["+"]`, which derives allowed origins _from_ the redirect URIs, and it is not established that a wildcard entry yields `http://localhost:5180` for the cross-origin token exchange. Nothing has observed a sign-in at `:5180` for **any** app yet: spec 491's verification lists the signed-in console (T018) as not observed, and no e2e spec names `5180`. FR-007 makes it an observation, not an assumption.                                                                                                 |
| 3   | `createBrowserRouter` has no `basename` in `router.tsx`                                                              | **True**                               | `apps/kiosk-web/src/app/router.tsx`: four routes, no options argument.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| 4   | `vite.config.ts` builds the `/hubs` dev proxy from `VITE_LAYOUT_HUB_ORIGIN`; it must keep working under the new base | **True, and incomplete**               | The proxy is built from it (`apps/kiosk-web/vite.config.ts:24–51`). But in dev the kiosk resolves the hub to the **page-relative** `/hubs/layouts` (`apps/shared/src/realtime/hubUrl.ts`: `VITE_LAYOUT_HUB_URL` unset → `/hubs/layouts`). A kiosk page opened at the gateway origin would send that to the **gateway**, whose lowest-order catch-all forwards it to the **shell's** Vite server, which has no `/hubs` proxy. So keeping the proxy is not enough: the hub URL itself must stop being relative to the page when the page is at the one origin. And `LayoutComposition` has no CORS policy at all (no `AddCors`/`UseCors` outside `src/ApiGateway`), so the browser cannot simply be pointed at the service from a different origin. FR-008 states the constraint; the plan chooses the mechanism. |
| 5   | WebRTC/WHEP stays direct                                                                                             | **True, by construction**              | `apps/shared/src/streaming/WhepClient.ts` posts to the server-provided `whepUrl`; `src/AppHost/Resources/mediamtx.yml` sets no allowed-origins list, so MediaMTX's default applies. Not observed from `:5180`; FR-009 makes it an observation.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| 6   | (Implied) phase 1 is merged and the gateway can host UI                                                              | **True**                               | `src/ApiGateway/UiHosting.cs` (`Ui:Mode` = `Proxy`/`Static`), `WebApplications.WithOneOriginUi`, fixed dev port `5180`. `/kiosk/**` and `/wall/**` are reserved and unrouted: today they fall to the shell's catch-all.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |

Found while checking, not named by the issue:

- **The sign-in return falls back to `/`** (`auth.ts:126–130`, `onSigninCallback`). At the one origin `/` is the shell, so a kiosk whose
  stored return path is unusable would land on the console's page. `returnTo` itself is `window.location.pathname`
  (`useSessionExpiry.ts:161,413`), which already includes any base. FR-004.
- **The dev font preload is root-absolute** (`apps/shared/src/ui/fonts/fontPreload.ts`: `/@fs/...` in `serve`), so under a base it would
  be fetched from the gateway's root and answered by the shell's dev server. A preload miss is a console warning, not a broken page; FR-011.
- **`sse.auth.wasAuthenticated` is in `localStorage`** (`useSessionExpiry.ts:10`), and `localStorage` is per origin. At one origin the
  kiosk and the wall share it; their OIDC grants do not collide (the key names the client). See Edge Cases.

## Clarifications

### Q1 — what REST base does the kiosk use at the one origin?

**[NEEDS CLARIFICATION: Does the kiosk keep its absolute gateway base in this phase (option A), or move to `/api` like the shell (option B)?]**

Today `kiosk-web` and `kiosk-wall` receive `VITE_API_GATEWAY_URL` = the gateway's endpoint (in run mode `http://localhost:5180`) and call
the bare `/<context>/...` routes. ADR-0192 decision 5 says the value "becomes the explicit `/api` base"; spec 491 applied that to the shell.

| Option              | What it means                                                                                                                                                                                                                                                          | Cost                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **A (recommended)** | Keep the absolute gateway origin for the kiosk in this phase. Opened at `/kiosk/` the calls are **same-origin** to the bare routes (a `fetch` is not a navigation, so spec 491's navigation rule lets it through); opened at `:5174` they stay cross-origin, as today. | Nothing changes in the kiosk's REST path, so e2e specs that capture the gateway origin from a kiosk request and then call bare paths on it (at least `kiosk-latency-contract`; the `kiosk-shows-*`/`kiosk-paints-*` specs capture it from the **operator** page, which spec 491's shell dev proxy already covers) keep passing unmodified. Leaves the kiosk on the bare routes, which ADR-0192 keeps "while the fixed dev ports exist"; the move to `/api` lands with the e2e cut-over (#3247). Reads ADR-0192 decision 5 as satisfied by its guard (still required, still fails loudly), not yet by its value. |
| **B**               | Move the kiosk to `/api` now, with a Vite `/api` proxy so `:5174`/`:5175` keep working (spec 491 D7).                                                                                                                                                                  | A spec capturing the origin from a kiosk request would then capture the Vite origin and call bare paths on it, which 404; spec 491 met the same problem with a bare-path dev proxy (`apps/management-web/src/app/devProxy.ts`). The kiosk would need a second copy of it, or the specs change now, pulling part of #3247 into this phase.                                                                                                                                                                                                                                                                       |

Recommendation: **A**, because it is the smaller change, keeps every kiosk and wall e2e spec a characterisation, and leaves the one
remaining REST move to the phase that already owns the e2e cut-over. Requirements below are written for A; FR-010 says what B would change.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - A kiosk screen runs from the one address (Priority: P1)

A technician sets up a kiosk screen by opening one address under `/kiosk/`, signs in, picks a layout and sees live video with its overlay
labels updating. A reload, a deep link to a layout and a return from an expired session all land back on the kiosk, never on the operator
console that shares the address.

**Why this priority**: This is the issue. A kiosk is the screen the 800 ms budget ends on, so it is also the one where "one address" must
be proven not to cost anything on the live path.

**Independent Test**: Start the dev stack. Open `http://localhost:5180/kiosk/`, sign in as a kiosk account, open a layout with a live
camera and a bound overlay, trigger a variable change, reload the layout page, then open `http://localhost:5180/kiosk/layouts/<id>` in a
new tab. Watch the network log: pages, scripts and REST are on `:5180`; sign-in is Keycloak; the hub connection and WHEP are **not** to
`:5180`. Read the gateway's request log for the run: no `/hubs/...` and no WHEP request reached it.

**Acceptance Scenarios**:

1. **Given** the stack is running, **When** a person opens `/kiosk/`, **Then** the kiosk's sign-in starts as the `kiosk-web` client and,
   after login, returns to `/kiosk/` (not to `/` and not to the console).
2. **Given** a signed-in kiosk, **When** it opens a layout, **Then** the address is `/kiosk/layouts/<id>`, live video plays and overlay
   labels update on a variable change.
3. **Given** a kiosk on `/kiosk/layouts/<id>`, **When** the browser reloads, **Then** the same layout is shown.
4. **Given** a kiosk whose session has expired on `/kiosk/layouts/<id>`, **When** it signs in again, **Then** it returns to
   `/kiosk/layouts/<id>`.
5. **Given** a sign-in return whose stored return path is missing, not a path, protocol-relative (`//...`) or outside `/kiosk/`, **When**
   the callback completes, **Then** the kiosk lands on `/kiosk/` (not `/`, and never another origin).
6. **Given** a signed-in kiosk at the one address, **When** live updates flow, **Then** the hub connection is not made to the gateway's
   origin and no hub request appears in the gateway's request log.
7. **Given** a kiosk opened at the one address, **When** it starts a stream, **Then** the WHEP request goes to the `whepUrl` the service
   returned, not to the gateway.

---

### User Story 2 - A wall display runs from `/wall/`, still as the wall (Priority: P1)

A wall display is set up by opening `/wall/` on the same address. It signs in as the wall client, with the long-lived grant and without
the authority to write events, exactly as it does on its own port today.

**Why this priority**: The wall is the same application in a different mode, and the mode is a security narrowing (spec 052: scopes belong
to clients). Serving it from a path must not blur which client it signs in as.

**Independent Test**: Open `http://localhost:5180/wall/`, sign in as a wall account, and read the sign-in request: client `kiosk-wall`,
scope `openid offline_access`, return address under `/wall/`. Open a wall and see it render. Open `/kiosk/` in another profile and see it
sign in as `kiosk-web` with scope `openid`.

**Acceptance Scenarios**:

1. **Given** the stack is running, **When** a person opens `/wall/`, **Then** sign-in uses client `kiosk-wall` with scope
   `openid offline_access` and returns to `/wall/`.
2. **Given** a signed-in wall display, **When** it opens `/wall/walls/<id>`, **Then** the wall renders, and a reload shows the same wall.
3. **Given** `/kiosk/` and `/wall/` on the same address, **When** each signs in, **Then** each uses its own client and its own return
   address; neither is ever answered by the other instance's pages.

---

### User Story 3 - The fixed ports keep working, and the existing kiosk suites prove it (Priority: P1)

Someone still using `localhost:5174` or `localhost:5175` directly is not broken, and the kiosk's existing unit and end-to-end suites pass
without being edited.

**Why this priority**: ADR-0192 decision 7: nothing cuts over until the end-to-end suite does (#3247). The existing suites are the
characterisation that this phase changed paths, not behaviour.

**Independent Test**: Open `localhost:5174/` and `localhost:5175/`; each reaches its app (under its base) and signs in. Run the kiosk-web
unit suite and the `kiosk` and `wall` Playwright projects without editing a test.

**Acceptance Scenarios**:

1. **Given** the stack is running, **When** a person opens `http://localhost:5174/`, **Then** they reach the kiosk (at `/kiosk/` on that
   port) and can sign in and open a layout as today.
2. **Given** the same, **When** a person opens `http://localhost:5175/`, **Then** they reach the wall display likewise.
3. **Given** this change, **When** the kiosk-web unit suite runs, **Then** every pre-existing test passes unmodified (new tests may be
   added beside them).
4. **Given** this change, **When** the `kiosk` and `wall` Playwright projects run in CI, **Then** they pass with no spec file edited.
5. **Given** this change, **When** the architecture tests run, **Then**
   `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` passes without any edit to it.

---

### User Story 4 - A developer keeps hot reload on the kiosk through the one address (Priority: P2)

A developer working on the kiosk edits a component and sees it update through `/kiosk/` (and `/wall/`) without a manual reload.

**Why this priority**: Spec 491 proved the socket passes the gateway for the console; the kiosk's dev servers now sit behind the same
proxy and should behave the same. Lower than P1 because it is a developer convenience, not a screen on the fab floor.

**Independent Test**: With `/kiosk/` open, change a kiosk component; the page updates in place. Repeat on `/wall/`.

**Acceptance Scenarios**:

1. **Given** `/kiosk/` is open in dev, **When** a kiosk component with a hot-reload boundary changes, **Then** the browser updates
   without a full reload.
2. **Given** `/wall/` is open in dev, **When** the same file changes, **Then** the wall updates too.

---

### User Story 5 - Built kiosk and wall bundles are served from the same paths (Priority: P2)

A release build of the kiosk and of the wall display is served at `/kiosk/` and `/wall/` in the gateway's static mode, with the same
behaviour as in dev, and a build that forgets a required setting fails loudly.

**Why this priority**: Required for the epic to mean anything off a developer machine, but no production deployment exists (ADR-0192
decision 10), so it is proven with built bundles served locally, as spec 491 proved the console.

**Independent Test**: Build the kiosk twice (ordinary and wall mode), start the gateway in static mode over both builds and the console's,
and repeat the User Story 1 and 2 page checks against the built bundles (reload, deep link, an unknown asset). Then build without a required
setting and see it fail at load, naming it.

**Acceptance Scenarios**:

1. **Given** production builds of the kiosk and the wall, **When** the gateway serves them in static mode, **Then** `/kiosk/` and `/wall/`
   each serve their own app, and each instance's assets resolve under its own path.
2. **Given** static mode, **When** a person reloads `/kiosk/layouts/<id>` or `/wall/walls/<id>`, **Then** that instance's page is served,
   never the shell's and never the other instance's.
3. **Given** static mode, **When** a request asks for a file that does not exist under `/kiosk/` or `/wall/`, **Then** it is a 404, not a
   page.
4. **Given** static mode configured for the kiosk or the wall with a missing directory or no `index.html`, **When** the gateway starts,
   **Then** it refuses to start, naming the setting.
5. **Given** a production kiosk build with no hub URL, no Keycloak URL or no API base, **When** it loads, **Then** it fails naming the
   missing setting (the existing guards, unchanged).

---

### Edge Cases

- **`/kiosk` and `/wall` without a trailing slash** reach their instance (redirect to the slashed path), not the shell's not-found page.
- **A path under `/kiosk/` or `/wall/` that the instance does not serve** is never answered by the shell's catch-all, in either gateway
  mode; in particular, with an instance not configured, `/kiosk/...` and `/wall/...` are a 404 as `/remotes/...` is today.
- **The OIDC callback at `/kiosk/oidc/callback`** is handled by the auth provider before the router; the router's `/oidc/callback`
  fallback route must still match it under the basename.
- **Sign-in returned to the shell's address**: impossible for a stored return path outside the instance's base (Story 1 scenario 5).
- **The kiosk and the wall share one `localStorage`** at one origin. Their OIDC users do not collide (`oidc.user:<authority>:<client>`).
  The `sse.auth.wasAuthenticated` flag does: a browser profile that ran `/wall/` makes a fresh `/kiosk/` behave as "had a session" (it tries
  a silent sign-in first and falls back to the sign-in button). Accepted, not changed: a physical screen runs one instance; a developer
  opening both in one profile sees one extra silent attempt. The flag is not a credential. Recorded here so the plan does not rediscover it.
- **The shell and the kiosk share one origin**: `management-web`'s OIDC user (`client management-web`) and the kiosk's do not collide; a
  person signed into the console is not thereby signed into a kiosk (different clients).
- **Hub and fixed port at once**: a kiosk at `:5174` and one at `:5180/kiosk/` both reach the hub, in the same session, without either
  connection passing the gateway.
- **The gateway restarts**: the kiosks lose their page origin as well as REST. Already accepted in ADR-0192 (one instance, ADR-0153
  clause 1); a loaded kiosk page keeps running and its hub and video do not depend on the gateway. Not changed here.
- **`Sec-Fetch-Dest`** is sent only in secure contexts (spec 491 review). It does not matter to the kiosk under option A: the kiosk's own
  pages live under `/kiosk/` and `/wall/`, which no bare REST route shares.

## Requirements _(mandatory)_

### Functional Requirements

**Gateway (both modes)**

- **FR-001**: The gateway MUST serve the kiosk instance under `/kiosk/` and the wall instance under `/wall/`, including every asset each
  loads, with no `per-source` rate-limit policy and no CORS policy on those routes (ADR-0192 decision 8, as spec 491 for the console).
- **FR-002**: In proxy (dev) mode the gateway MUST forward `/kiosk/**` to the `kiosk-web` dev server and `/wall/**` to the `kiosk-wall` dev
  server, including the hot-reload socket, and these routes MUST win over the shell's catch-all.
- **FR-003**: In static mode the gateway MUST serve a kiosk build and a wall build from configured directories, answer an extension-less
  path under each prefix with **that instance's** `index.html`, answer a missing file under each prefix with 404, and refuse to start when a
  configured directory is missing or lacks `index.html`. Whether the two directories are required in static mode or optional is a plan
  decision (spec 491's static mode already ships without them); either way, an unconfigured prefix is a 404, never the shell.

**Kiosk application**

- **FR-004**: Each instance MUST run under its own base path, `/kiosk/` for the ordinary kiosk and `/wall/` for the wall, and the base MUST
  be decided by the same flag that decides the client and scopes (`VITE_KIOSK_MODE`, spec 052: "one flag decides both"), so a wall bundle
  cannot be served at the kiosk's path or the reverse by a forgotten second setting.
- **FR-005**: The router MUST use the instance's base as its basename, so every existing route (`/`, `/layouts/:id`, `/walls/:id`,
  `/oidc/callback`) resolves under it and in-app navigation never leaves it.
- **FR-006**: The OIDC `redirect_uri` MUST be the page's origin plus the instance's base plus `oidc/callback`. The sign-in return MUST land
  on the stored return path only when it is a same-origin path under the instance's base; otherwise on the base itself (`/kiosk/` or
  `/wall/`), never `/`.
- **FR-007**: Sign-in MUST complete, token exchange included, for `kiosk-web` at `/kiosk/` and `kiosk-wall` at `/wall/` on the one origin,
  in dev, **without a manual realm edit**. The realm's `http://localhost:*` redirect wildcard is expected to cover the redirect; whether
  `webOrigins: ["+"]` admits `http://localhost:5180` for the token exchange MUST be observed, not assumed. If it does not, the realm is
  changed (explicit entries for the one origin), and that change is recorded with the two realm hazards: an edited realm takes effect only
  after the Keycloak volume is deleted, and a client `description` over 255 characters stops the realm import.
- **FR-008**: The layout hub connection from a kiosk or wall opened at the one origin MUST NOT pass the gateway, and MUST pass through **no
  more intermediaries than it does today** from `:5174`/`:5175` (today: the instance's own Vite `/hubs` proxy, then `layout-composition`).
  It MUST keep working from the fixed ports. `@microsoft/signalr`, the hub's server side and `BoundaryTests` are not changed. The mechanism
  (for example, an absolute hub URL at the instance's own dev server, or a CORS policy on `layout-composition`) is chosen in the plan, which
  must state the hop count it produces and why that does not touch the event → overlay leg.
- **FR-009**: WHEP MUST continue to go to the server-provided `whepUrl` and MUST work from a kiosk page at the one origin (the media
  server's allowed origins admit it); observed, not assumed.
- **FR-010**: Under Q1 option A, the kiosk's REST base MUST stay the gateway's absolute origin, still required in production builds and
  still failing loudly when unset. (Under option B it becomes `/api`, `:5174`/`:5175` gain an `/api` dev proxy and a bare-path dev proxy,
  and the kiosk e2e specs that call bare paths on a captured origin are re-examined.)
- **FR-011**: Every root-absolute URL the kiosk emits in dev or in a build (the HTML entry, scripts, the favicon, the font preloads) MUST
  resolve under the instance's base, so nothing the kiosk loads is answered by the shell at the origin root.

**Composition, compatibility, documentation**

- **FR-012**: The AppHost MUST declare the kiosk and wall destinations for the gateway's proxy mode (run mode only, as spec 491 did for
  the console), not wired by hand (constitution: Aspire is the composition root).
- **FR-013**: The fixed dev ports 5174 and 5175 MUST keep serving their apps, now under their base (`:5174/` reaching `:5174/kiosk/`). The
  e2e baseURLs (`playwright.config.ts`) MUST NOT need to change in this phase; the cut-over is #3247.
- **FR-014**: The gateway's CORS allowed-origins list MUST NOT be narrowed in this phase (the fixed ports remain cross-origin callers;
  ADR-0192 decisions 6–7, spec 491 FR-013).
- **FR-015**: `docs/deployment-frontend-env.md` and spec 491's path contract (or a contract in this spec that supersedes its `/kiosk/` and
  `/wall/` row) MUST record the kiosk and wall paths, the base per instance, the static-mode settings and how the hub is reached.
- **FR-016**: The change MUST leave `@microsoft/signalr`, the hub's server side, Keycloak's position (direct) and WebRTC media untouched,
  and `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` unmodified. If any requirement above cannot be met
  without changing that test, work stops and ADR-0192 is amended; the test is not weakened.

### Test colour (ADR-0139)

The phase-3 architect confirms this split; ambiguity resolves to red.

**Behaviour-changing, red first** (a test observed failing before the change, its failure quoted in the PR):

- Gateway: `/kiosk/**` and `/wall/**` in proxy mode (HTTP and the hot-reload upgrade), precedence over the shell, no rate-limit policy
  (FR-001, FR-002); static mode for both instances, per-instance deep-link fallback, 404 for a missing file and for an unconfigured prefix,
  fail-fast on a bad root, the no-slash redirect (FR-003, Edge Cases).
- Kiosk: base derived from the mode flag (FR-004); router basename (FR-005); base-aware `redirect_uri` and the return-path rule, including
  `/`, `//x`, a path outside the base and a missing value (FR-006); the hub URL the kiosk resolves at the one origin (FR-008); root-absolute
  URLs under the base, where a unit or build-output test can see them (FR-011).
- AppHost: the gateway's kiosk and wall destinations and any new kiosk environment value (FR-012).

**Behaviour-preserving, characterisation observed green before and after, unmodified:**

- The kiosk-web unit suite (39 test files today; counts recorded at baseline).
- The `kiosk` and `wall` Playwright projects (`playwright.config.ts`, baseURLs `:5174`, `:5175`) and the cross-project kiosk specs: they
  navigate only to `/` or `http://localhost:5175/`, which reach the base on the fixed ports; they are the evidence for FR-013. CI is the
  observation (they need the full stack).
- `Architecture.Tests`, `BoundaryTests` with no diff; spec 491's gateway suites (`GatewayUiProxyTests`, `GatewayUiStaticTests`,
  `GatewayNavigationRuleTests`, `GatewayApiPrefixTests`, `GatewayHostCharacterisationTests`); the console suites.

**Expected to move with an accepted decision, recorded rather than hidden** (spec 491's precedent): the AppHost composition snapshots
(`tests/Integration.Tests/AppHostComposition/*.snapshot.txt`) gain the gateway's kiosk and wall destinations and any new kiosk environment
value. Any other pre-existing assertion that has to change is a stop-and-report, not an edit.

### Key Entities

- **Instance**: one running copy of `apps/kiosk-web`: the kiosk (`kiosk-web` client, `/kiosk/`, dev port 5174) or the wall (`kiosk-wall`
  client, `/wall/`, dev port 5175). Mode flag, base path and client are one decision.
- **Base path**: the instance's path prefix; the Vite `base`, the router basename and the OIDC callback all derive from it.
- **Path map** (extends spec 491's): `/` shell; `/remotes/<name>/` remotes; `/api/<context>/...` and bare `/<context>/...` REST;
  **`/kiosk/` kiosk; `/wall/` wall**.
- **Hub route**: how an instance reaches `/hubs/layouts`; off the gateway, hop count unchanged.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: A kiosk at `/kiosk/` and a wall at `/wall/` each complete sign-in, open their layout or wall and show live video with an
  overlay update, using one address; the network log shows **no request to any other application origin** except the identity provider,
  the hub route and the media server.
- **SC-002**: Over the whole SC-001 run, the gateway's request log contains **zero** hub (`/hubs/...`) and **zero** WHEP requests.
- **SC-003**: The kiosk-web unit suite passes with every pre-existing file unmodified (count recorded at baseline, delta equal to the new
  tests only), and the `kiosk` and `wall` Playwright projects pass in CI with no spec edited.
- **SC-004**: `Architecture.Tests` passes with `BoundaryTests.cs` showing no diff against `origin/develop`.
- **SC-005**: From one source address, a cold load of `/kiosk/` and `/wall/` repeated past the production API limit (100 per minute per
  source) yields **zero** 429 responses on page and asset requests.
- **SC-006**: Kiosk click-to-first-frame (opening a layout to the first decoded frame) through `/kiosk/` is not worse than through `:5174`
  beyond measurement noise, measured twice per the repo's measurement rule. The page load is not a §IV leg; this bounds what passing the
  gateway costs the screen.
- **SC-007**: A reload or deep link to any kiosk or wall route, in both gateway modes, shows that instance's page; none shows the console.

## Assumptions

- **Phase 1 is in place** (spec 491, merged in #3253): `Ui:Mode`, the fixed dev address `http://localhost:5180`, the navigation rule and
  the `/api` twins exist and are reused, not re-specified.
- **One flag, one base.** The wall's base is derived from `VITE_KIOSK_MODE=wall`; no second setting is introduced (FR-004). A Vite dev
  server has one base, so the fixed port moves under it too (FR-013); Vite redirects the port's root to the base, which is what keeps the
  e2e baseURLs and their `goto('/')` calls working. This is believed from how Vite treats a non-root `base` and was observed for the two
  remotes in spec 491 (`/` answered with a redirect); for the kiosk it is a plan-time spike, and if it fails the e2e consequence is
  reported at the Plan gate, not absorbed silently.
- **The hub stays a different origin in production**, as ADR-0192 records. The production hub is already an absolute
  `VITE_LAYOUT_HUB_URL` (or an Ingress serving `/hubs` beside the app, `docs/deployment-frontend-env.md`); this phase does not change that.
  It is noted, not fixed, that `layout-composition` has no CORS policy, so a production kiosk on a different origin from the hub would need
  either that Ingress route or a policy. That belongs to the production edge (phase 4), not here.
- **Overlap with spec 493** (#3259, branch `493-gateway-edge-cases`, specified in parallel). 493 changes `src/ApiGateway/UiHosting.cs`:
  `MapShellFallback` (a dotted deep link gets the shell instead of 404; `Cache-Control: no-cache` on `index.html`) and the static-file options
  (`no-cache` on fixed-name entry files), and `GatewayHost.cs` (the carve-out for every method). This spec changes the **same** file in the
  same places: new proxy routes and clusters, two more static roots and `UseStaticFiles` calls, and a per-instance fallback that is the
  shell fallback generalised. The two will conflict textually, and they should agree in substance: the kiosk's and the wall's
  `index.html` are fixed-name entry points too, and their fallback should follow whatever rule 493 settles for dotted paths. **Recommended
  order: 493 merges first and this spec's implementation rebases onto it**; the plan reuses 493's fallback rather than copying today's.
  `apps/management-web/src/app/devProxy.ts` (493 story 1) is not touched here.
- **Content-Security-Policy** for the HTML origin stays with #3252; the kiosk adds origins it will have to name (the hub route, the media
  server), recorded there, not built here.
- **The production edge** (TLS, Ingress, replicas) stays deferred (ADR-0192 decision 10).
- **Spec number 494** was assigned by the orchestrator (493 is in use in parallel).
