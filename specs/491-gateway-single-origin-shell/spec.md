# Feature Specification: The operator console on one origin

**Feature Branch**: `491-gateway-single-origin-shell`

**Created**: 2026-11-11

**Status**: Draft, clarified (Q1 answered by the owner, 2026-11-11); no markers remain

**Issue**: #3245, phase 1 of epic #3248. **Governing decision**: ADR-0192 (accepted, amends ADR-0106). **Lane**: supervised (ADR-0037).

**Input**: User description: "Serve the operator console on ONE origin through the API gateway. `/` is the management shell,
`/remotes/cameras/` and `/remotes/event-ingestion/` are the two federated remotes, the REST API moves to `/api/<context>/...` with the
prefix stripped. Dev proxies the Vite dev servers including hot reload; production serves the built bundles. The fixed dev ports keep
working. Out of scope: the kiosk and the wall (#3246), the end-to-end suite cut-over (#3247), the production edge, Keycloak, the SignalR
hub, WebRTC media and `@microsoft/signalr`."

**Latency budget (§IV): legs touched: none.** The event-to-overlay path is the kiosk, RabbitMQ and SignalR (ADR-0152); media is WebRTC to
the SFU. Neither moves, and `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` stays in force **unmodified**
(ADR-0192 decision 3). Click-to-first-frame (spec 002 FR-013, spec 316 FR-019) is affected only through how the remote is fetched; see SC-005.

## Clarifications

### Q1 — what claims `/system-variables` and `/event-ingestion` when the shell lives at `/`? (answered by the owner, 2026-11-11: option A)

Two of the shell's own pages have the same first path segment as two of the gateway's bare REST routes: the shell page `/system-variables`
(and its `event-ingestion/*` remote mount) and the gateway's `/system-variables/**` and `/event-ingestion/**`. ADR-0192 adds `/api/<context>`
to remove this collision, but also keeps the bare `/<context>/...` routes "while the fixed dev ports exist". Both cannot hold at one root: a
browser reload or bookmark of `/system-variables` would be answered by the REST route, not the shell. The three options considered:

| Option | What it means                                                                                                                                                                                                             | Cost                                                                                                                     |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **A**  | The bare `/<context>/...` routes answer only requests that are not page navigations (a browser reload sends `Sec-Fetch-Dest: document`; scripts, `fetch` and test clients do not). Navigations fall through to the shell. | One routing rule on nine routes. Both modes work at once. Relies on a browser header for the split.                      |
| **B**  | The bare routes exist only when a gateway setting is on (dev fixed-port mode); in the one-origin mode they are off.                                                                                                       | A mode switch, so the two modes cannot run against the same gateway at once.                                             |
| **C**  | The bare routes are removed in this phase; the fixed-port apps move to `/api` too.                                                                                                                                        | Breaks FR-012 (the cross-origin kiosk, the wall and fixed-port mode would have to change in this phase, which is #3246). |

**Decision: A.** The bare `/<context>/...` routes answer only requests that are **not page navigations**; a navigation to one of those
paths falls through to the shell. B and C are set aside: B because the two modes could no longer share one gateway, C because it would
pull #3246 and #3247 into this phase. Residual risk, stated: the split relies on a header the browser sends, so a non-browser client that
sends no such header to a bare path still gets the REST route, which is the behaviour it has today.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - An operator uses the whole console from one address (Priority: P1)

An operator opens one URL, signs in, and uses the console: the shell, the cameras pages and the event-ingestion pages, with every data
call going to the same address. They never see a second host or port, and a reload on any page, including a deep link, lands on that page.

**Why this priority**: This is the owner's request, and everything else in the epic builds on it.

**Independent Test**: Start the dev stack, open the one address in a browser, sign in as an entitled operator, open a cameras page and an
event-ingestion page, reload each, and watch the network: every request is to the one origin except sign-in (identity provider) and, where
used, the live-update hub.

**Acceptance Scenarios**:

1. **Given** the stack is running, **When** an operator opens the one address, **Then** the console's sign-in starts and returns to the
   same address after login.
2. **Given** a signed-in operator entitled to cameras, **When** they open a cameras page, **Then** the cameras remote is loaded from the
   one origin under `/remotes/cameras/` and the page works.
3. **Given** a signed-in operator entitled to event ingestion, **When** they open an event-ingestion page, **Then** the event-ingestion
   remote is loaded from `/remotes/event-ingestion/` and the page works.
4. **Given** a signed-in operator on any console page, **When** they reload the browser, **Then** the same page is shown (including
   `/system-variables` and `/event-ingestion/...`, see Q1).
5. **Given** an operator who holds no entitlement for a remote, **When** they use the console, **Then** that remote is never requested
   (unchanged from spec 316 FR-013).
6. **Given** any console data call, **When** it is sent, **Then** it goes to `/api/<context>/...` on the one origin and the request's
   `Authorization` header reaches the service unmodified.

---

### User Story 2 - A developer keeps hot reload, and the old ports keep working (Priority: P1)

A developer working on the shell or a remote edits code and sees the change in the browser through the one address, without a manual
reload. Someone still using `localhost:5173`, `:5176` and `:5177` directly is not broken.

**Why this priority**: ADR-0192 names this the riskiest assumption (the hot-reload socket and the remote entry both working under a path
prefix, with the architecture test still green). It is proven first, and everything else depends on it.

**Independent Test**: With the one address open, edit a component in the shell and in a remote; the page updates without a full reload.
Open `localhost:5173` directly and sign in; the console works as before. Run the architecture tests; they pass unmodified.

**Acceptance Scenarios**:

1. **Given** the one address is open in dev, **When** a shell source file changes, **Then** the browser updates in place.
2. **Given** the one address is open in dev, **When** a remote's source file changes, **Then** the page that uses it updates.
3. **Given** the stack is running, **When** a person opens `localhost:5173` directly, **Then** the console works exactly as it does today
   (remotes at 5176 and 5177, API at the gateway's own origin).
4. **Given** this change, **When** the architecture tests run, **Then** `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`
   passes without any edit to it. If it cannot, work stops and ADR-0192 is amended; the test is not weakened.

---

### User Story 3 - A production build is served from the same origin (Priority: P2)

A release build of the shell and the two remotes is served at the same paths as in dev, with the same origin for pages and API, and a
build that forgets to say where things are fails loudly instead of quietly pointing at `localhost`.

**Why this priority**: Required for the epic to mean anything outside a developer's machine, but no production deployment exists yet, so
it is proven with a built bundle served locally, not with a deployment.

**Independent Test**: Build the three apps for production with the one-origin settings, serve them as the stack would, and repeat the
User Story 1 flow against the built bundles. Then build with one of the required settings missing and see the build or the app fail
naming the setting.

**Acceptance Scenarios**:

1. **Given** production builds of the shell and both remotes, **When** they are served at `/` and `/remotes/*`, **Then** the User Story 1
   flow works against them.
2. **Given** a production build with no API base set, **When** the app starts, **Then** it fails at load naming the missing setting
   (the guard in `docs/deployment-frontend-env.md` is preserved).
3. **Given** the one-origin build, **When** its settings are read, **Then** the API base is the explicit `/api` and each remote is an
   explicit relative path under `/remotes/`.

---

### User Story 4 - Loading a page does not trip the rate limit, and nothing that was protected is exposed (Priority: P2)

A single page load fetches many files. They must not be throttled like API calls, and the API stays limited as today. The route carved out
for the media server stays a hard 404.

**Why this priority**: Without it, the one-origin console would answer 429 to a normal page load from a shared fab address (ADR-0192
decision 8; the limit's suitability for a fab is #2563).

**Independent Test**: From one source address, load the console cold and visit every remote page more times than the API limit would
allow for API calls; no page asset is refused. Then exceed the API limit with API calls and see them refused as today.

**Acceptance Scenarios**:

1. **Given** the production API limit (100 per minute per source), **When** one source loads the console repeatedly, **Then** no page or
   remote asset request is answered 429.
2. **Given** the same source, **When** it sends more API calls than the limit, **Then** the surplus is answered 429 as today.
3. **Given** `POST /stream-distribution/streams/authorize` and `POST /api/stream-distribution/streams/authorize`, **When** called through
   the gateway, **Then** both are a hard 404.
4. **Given** a console API call with an `X-Fab` header, **When** it passes through, **Then** the existing edge header handling is
   unchanged under `/api` as well.

---

### Edge Cases

- A deep link or reload on a shell page that has no matching file (history routing): the shell is served, not a 404, except for paths
  that belong to `/api`, `/remotes` and the carve-out.
- The same path as page and as API (Q1): a browser reload of `/system-variables` shows the shell page; a `fetch` or script request to
  `/system-variables/...` on the same gateway still reaches the REST route.
- A request under `/api/` for a context that does not exist: it is not answered by the shell; it is a 404 from the gateway.
- A remote that is down or slow: one contained load failure on the page that needs it, as today (spec 316 FR-007/FR-008); the shell
  still loads.
- The remote entry fetched under a path prefix: its own chunks and assets resolve under `/remotes/<name>/`, not from `/`.
- The hot-reload socket through the proxy in dev only; nothing in production opens a socket through the gateway.
- Sign-in redirect and post-login return use the one origin; the identity provider's allowed redirects include it.
- Both modes at once: a request to the old fixed ports and a request to the one address in the same session do not interfere.
- The gateway is one instance, so a restart takes pages and API down together (ADR-0192 negative consequence); not changed here.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The gateway MUST serve the management shell at `/`.
- **FR-002**: The gateway MUST serve the cameras remote under `/remotes/cameras/` and the event-ingestion remote under
  `/remotes/event-ingestion/`, including each remote's entry file and every asset it loads.
- **FR-003**: Every existing REST route MUST also be reachable at `/api/<context>/...`, with the `/api` prefix removed before the request
  reaches the service. All nine contexts are covered; each keeps its existing CORS and rate-limit policy.
- **FR-004**: The `Authorization` header MUST reach the service unmodified, and the existing `X-Fab` removal MUST behave identically
  under `/api`.
- **FR-005**: In dev, the gateway MUST proxy the three Vite dev servers (shell 5173, cameras 5176, event-ingestion 5177), including the
  hot-reload socket, so that editing code updates the page through the one address.
- **FR-006**: The shell and each remote MUST build and run under their paths (a per-app base path), and the shell's remote locations MUST
  be explicit relative paths under `/remotes/`.
- **FR-007**: The one-origin build MUST set the API base to the explicit `/api`. A production build that sets none of the required
  settings MUST still fail at load, naming the setting (the guard in `docs/deployment-frontend-env.md` is kept).
- **FR-008**: The identity provider's client for the console MUST allow sign-in and return on the one origin. In dev this must work
  without a manual realm edit.
- **FR-009**: Static page and asset requests MUST NOT pass through the `per-source` rate limiter; `/api/...` MUST stay under it.
- **FR-010**: `POST /stream-distribution/streams/authorize` MUST remain a hard 404 at the gateway, under the `/api` prefix as well as
  without it.
- **FR-011**: A request for a shell page with no matching file MUST be answered with the shell, except under `/api`, `/remotes` and the
  carve-out; a navigation to `/system-variables` or `/event-ingestion/...` MUST be answered by the shell, while a non-navigation request to the same bare path still reaches the REST route (Q1, option A).
- **FR-012**: The fixed dev ports 5173–5177 MUST keep serving their apps beside the one address, and the console MUST work when opened at
  `:5173`. Nothing in this phase removes a port. Two consequences are accepted (owner, Plan gate, 2026-11-11): the shell's own dev calls
  go through Vite's proxy and so are no longer cross-origin (the cross-origin path stays exercised by the kiosk, the wall and direct API
  calls until #3246 and #3247), and a remote's entry file moves under its base path, so a remote at its own port serves
  `/remotes/<name>/remoteEntry.js`, not `/remoteEntry.js`.
- **FR-013**: The gateway's CORS allowed-origins list MUST NOT be narrowed in this phase. The kiosk, the wall and the fixed-port mode are
  still cross-origin callers until #3246 and #3247. (This corrects the wording of #3245, which says the policy shrinks now; ADR-0192
  decisions 6 and 7 make it a later step.)
- **FR-014**: `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` MUST pass **unmodified**. If proxying the
  hot-reload socket cannot meet it, work stops and ADR-0192 is amended.
- **FR-015**: The dev stack MUST expose the one origin at a stable, documented address (not a random port), so it can be bookmarked and
  allowed in the identity provider.
- **FR-016**: The change MUST leave the SignalR hub, WebRTC media, Keycloak and `@microsoft/signalr` untouched.

### Key Entities

- **Origin**: the one scheme-host-port a person uses; owned by the gateway.
- **Path map**: `/` shell; `/remotes/cameras/`, `/remotes/event-ingestion/` remotes; `/api/<context>/...` REST (prefix stripped).
  `/kiosk/` and `/wall/` are reserved for #3246 and not served here.
- **Remote location**: a relative path under `/remotes/` the shell uses to load a federated remote.
- **Run mode**: one-origin or fixed-ports; both selectable in dev, neither breaks the other.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: An entitled operator completes sign-in, a cameras page and an event-ingestion page using **one** address; the browser's
  network log shows **no request to any other application origin** (the identity provider and, where used, the live-update hub excepted).
- **SC-002**: The existing frontend suites (shell 102 files / 740 tests, cameras 24 / 151, event-ingestion 4 / 27) pass with counts
  unchanged, and `Architecture.Tests` passes with the latency-legs test unmodified.
- **SC-003**: From one source address, a cold load of the console plus a visit to every remote page yields **zero** 429 responses at the
  production limit of 100 requests per minute, while exceeding that limit with API calls is still refused.
- **SC-004**: A code edit in the shell and in each remote appears in the browser through the one address without a manual reload, and the
  old fixed-port console still works in the same session.
- **SC-005**: Click-to-first-frame on the camera detail page, which loads from the cameras remote, is not worse through the one origin
  than through port 5176 beyond measurement noise (spec 002 FR-013, ≤ 3 s p95 stays the bar; measured twice, per the repo's measurement rule).
- **SC-006**: A production build missing a required origin setting fails at load, naming it, in all three apps.

## Assumptions

- "The fixed ports keep working" (FR-012) is read as: the apps are served and usable at their ports. It is not read as "unchanged URLs":
  a remote's entry URL moves under `/remotes/<name>/`, and one end-to-end spec's three URL expectations change in this phase (plan D8).
- A real production deployment does not exist; "production" here means a built bundle served the way the stack serves it, proven
  locally. Whether the gateway serves the built files itself or a small static container sits behind it is decided in the plan, with the
  reason recorded (ADR-0192 decision 4); the spec fixes only that the origin is the gateway's.
- The identity provider's console client already allows any `http://localhost:*` redirect in dev, so dev sign-in on a new local origin
  needs no realm change; a real deployment origin is added when one exists (phase 4).
- The kiosk and the wall (`/kiosk/`, `/wall/`) are phase 2 (#3246); the Playwright and CI cut-over, and a guard against hard-coded
  `localhost:517x` origins, are phase 3 (#3247).
- The gateway is a single instance (ADR-0153 clause 1) and stays one; the single-point-of-failure consequence is accepted in ADR-0192.
- Content-Security-Policy and security headers, which an HTML-serving gateway needs (ADR-0192 negative consequences), are not part of this
  phase unless the plan finds one is required to serve the pages safely; if so it is named in the plan, not added silently.
- Issue #3245's wording that the CORS list shrinks in this phase is superseded by FR-013 and corrected on the issue.
