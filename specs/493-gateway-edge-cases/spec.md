# Feature Specification: Gateway and shell dev proxy edge cases

**Feature Branch**: `493-gateway-edge-cases`

**Created**: 2026-11-12

**Status**: Specify gate approved by the owner (2026-11-12); no clarification markers remain

**Issue**: #3259 (found by `/code-review medium` on #3257; the code under review was merged by #3253, spec 491). **Lane**: supervised
(ADR-0037). **Kind**: behaviour-changing, so every fix starts with a test observed **red** first, failure quoted in the PR (ADR-0139).

**Decisions this builds on**: ADR-0192 (the gateway fronts the UIs at one origin; `/api/<context>` and the bare `/<context>` REST paths;
the navigation rule; `Ui:Mode=Proxy|Static`), ADR-0106 (gateway as the REST front door), spec 208 / #2284 (the `streams/authorize`
carve-out). No new architectural decision is needed: every fix below makes existing code do what ADR-0192 and spec 491 already say it
does. **No ADR required.**

**Latency budget (§IV): legs touched: none.** The dev-server proxy, the gateway's static UI hosting and the carve-out are all off the
event-to-overlay path (ADR-0192 fact 1; `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`). SignalR
(`@microsoft/signalr`) is out of scope and not touched.

## Premise check

The issue says its five findings were "only read from the code, not verified against a running stack". Each was checked against the tree at
`origin/develop` `fe07298c` before this spec was written. Checked by reading code and the pinned Vite source, **not** by running a stack;
phase 4a's red tests are the first execution.

| #   | Claim                                                                       | Verdict                                        | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| --- | --------------------------------------------------------------------------- | ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | A bare REST call with a query string is not proxied by the shell dev server | **True, narrower than stated**                 | Vite 8.3.6 (`apps/management-web/node_modules/vite/dist/node/chunks/node.js`, `createProxyContextMatcher` and its caller): a key starting with `^` is tested as a regular expression against `req.url`, which carries the query. The key `^/<context>(/                                                                                                                                                                                                                                                                                                                                             | $)`therefore misses`/<context>?…`(query straight after the context name). It does **not** miss`/<context>/things?…`, because the `/`alternative matches first. A real call of the missed shape exists:`apps/shared/src/api/audit.api.ts` lists audit entries at the context root with query parameters (`url: ''`, `params: …`). |
| 2   | A deep link whose last segment contains a dot gets 404 in Static mode       | **True, latent**                               | `src/ApiGateway/UiHosting.cs` `MapShellFallback` answers 404 whenever `Path.HasExtension(path)`, and `Path.HasExtension("/cameras/cam.01")` is true. No route the console links to produces such a path today (every route parameter is a Guid v7 identifier or an integer revision number), so it is reached only by a hand-typed or externally produced URL, where the shell's own not-found view is the right answer and a bare 404 is not. In dev (Vite) the same URL already gets the shell, so the two modes disagree.                                                                        |
| 3   | The shell's `index.html` carries no `Cache-Control`                         | **True, and wider than stated**                | Neither the fallback (`SendFileAsync`) nor the static-file options set `Cache-Control`; nothing under `src/ApiGateway` mentions it. `index.html` is not the only non-hashed entry point: each federated remote's `remoteEntry.js` (`/remotes/cameras/remoteEntry.js`, `/remotes/event-ingestion/remoteEntry.js`) is also fixed-name and names hashed chunks, and is served through the same static-file options. Included here as the same defect.                                                                                                                                                  |
| 4   | The plain-prefix proxy keys capture neighbouring words                      | **True, with a worse consequence than stated** | Same Vite matcher: a key without `^` is `url.startsWith(key)`, so `/api` captures `/api-tokens`, `/apiary`; `/remotes/cameras` captures `/remotes/cameras-x`. No console page has such a path today. But the gateway on :5180 (`Ui:Mode=Proxy`) forwards every unclaimed path to the shell dev server (`Ui__Proxy__Shell`, `src/AppHost/WebApplications.cs:105`), and the shell dev server proxies `/api…` to the gateway (`VITE_GATEWAY_PROXY_TARGET`, line 64). So `/apiary` would be passed between the two indefinitely. **Inferred from reading, not observed.**                               |
| 5   | The `streams/authorize` carve-out covers POST only                          | **True, low impact**                           | `src/ApiGateway/GatewayHost.cs` `MapStreamAuthorizeCarveOut` uses `MapPost` on both paths. Any other method skips the carve-out and is proxied by the `stream-distribution` / `api-stream-distribution` catch-all routes. The service maps the route POST only (`StreamEndpoints.cs` `MapAuthorizeWhep`) and its `whep-authorize` rate limit is an endpoint policy, not a global limiter, so a forwarded GET gets the service's 405 and spends nothing. The harm is that the carve-out's stated promise ("never through this gateway") does not hold. The bare-path half predates #3253 (spec 208). |

None is false and none is already fixed. All five are in scope.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - The shell's dev server forwards exactly what the gateway forwards (Priority: P1)

An engineer, or the Playwright suite, working against the shell dev server (:5173) sends REST calls the gateway would answer and opens
pages the shell owns. Every REST call the gateway routes is forwarded, query string or not. No path that is only a neighbouring word of a
proxied prefix is forwarded.

**Why this priority**: claims 1 and 4. Claim 1 breaks a real call (audit list on the bare path) at :5173 only, which is exactly the kind
of dev-only difference ADR-0192's "dev and prod use the same paths" exists to remove. Claim 4 can make two dev processes forward a request
back and forth. Both live in one file.

**Independent Test**: unit tests that apply each proxy key the way the pinned Vite version applies it (a regular expression when the key
starts with `^`, otherwise a plain prefix test) to full request URLs, query included, and assert which entry, if any, takes the request.

**Acceptance Scenarios**:

1. **Given** the dev proxy table, **When** a request for `/<context>?limit=5` arrives (for each of the nine REST contexts), **Then** it is
   forwarded to the gateway, exactly as `/<context>` and `/<context>/things?limit=5` are.
2. **Given** a page navigation (`Sec-Fetch-Dest: document`) to `/system-variables?tab=x`, **When** it reaches the dev server, **Then** the
   dev server serves it as the shell (the navigation rule is unchanged by the query string).
3. **Given** the dev proxy table, **When** a request for `/api-tokens`, `/apiary`, `/remotes/cameras-x` or `/remotes/event-ingestion-x`
   arrives, **Then** no proxy entry takes it.
4. **Given** the dev proxy table, **When** a request for `/api`, `/api/system-variables`, `/api?x=1`, `/remotes/cameras/remoteEntry.js` or
   `/remotes/event-ingestion/x?y` arrives, **Then** it is forwarded as today (no regression).
5. **Given** `/<context>-other` (the case spec 491 already guards), **When** it arrives, **Then** it is still not forwarded.

---

### User Story 2 - A reload of any console page gets the console in Static mode (Priority: P2)

An operator reloads, bookmarks or is sent a console URL whose last segment contains a dot. The gateway in Static mode serves the shell,
which shows its own page or its own not-found view. A missing script or stylesheet is still a 404, never HTML.

**Why this priority**: claim 2. Latent today (no linked route produces such a path), but a bare 404 instead of the console is a
dead end for an operator, and it makes Static mode disagree with dev.

**Independent Test**: start the gateway in Static mode against fixture roots (the existing `StaticUiStack`) and request dotted paths with
and without the browser's page-navigation signal.

**Acceptance Scenarios**:

1. **Given** Static mode, **When** a page navigation (`Sec-Fetch-Dest: document`) GETs `/cameras/cam.01`, **Then** the answer is 200 with
   the shell's `index.html`, `nosniff`, HTML content type.
2. **Given** Static mode, **When** a non-navigation GET (a script or fetch: no `Sec-Fetch-Dest: document`) asks for `/assets/missing.js`,
   **Then** the answer is 404 and is not the shell (unchanged).
3. **Given** Static mode, **When** a page navigation asks for a path under `/api/` or `/remotes/` that nothing serves, dotted or not,
   **Then** the answer is still 404, never the shell (unchanged).
4. **Given** Static mode, **When** a request with no navigation header asks for an extensionless deep link (`/layouts/123`), **Then** it
   still gets the shell (unchanged; existing test).
5. **Given** Static mode, **When** a non-read method (POST) targets a dotted or extensionless unrouted path, **Then** it is 404 (unchanged).

---

### User Story 3 - A redeploy never leaves a browser running yesterday's entry points (Priority: P2)

After the console is redeployed, an operator's browser that had cached the shell's entry page or a remote's entry script revalidates it on
the next load, so it never asks for chunks that no longer exist.

**Why this priority**: claim 3. Hashed chunks are replaced on every build; a cached fixed-name entry point that names them turns a
redeploy into a blank or broken console until the cache expires.

**Independent Test**: Static mode against fixture roots; read the `Cache-Control` header on each entry point and on a hashed asset.

**Acceptance Scenarios**:

1. **Given** Static mode, **When** the shell's entry page is fetched as `/`, as a deep link (`/layouts/123`), as a dotted deep link
   navigation (Story 2), or directly as `/index.html`, **Then** each response carries `Cache-Control: no-cache`.
2. **Given** Static mode, **When** `/remotes/cameras/remoteEntry.js` or `/remotes/event-ingestion/remoteEntry.js` is fetched, **Then** the
   response carries `Cache-Control: no-cache`.
3. **Given** Static mode, **When** a hashed asset (`/assets/app.js`, `/remotes/cameras/assets/chunk.js`) is fetched, **Then** its caching
   headers are unchanged by this spec.
4. **Given** every response above, **Then** `X-Content-Type-Options: nosniff` is still present (no regression of spec 491's rule).

---

### User Story 4 - MediaMTX's authorize hook is unreachable through the gateway for every method (Priority: P3)

Anyone reaching the gateway with any HTTP method on `/stream-distribution/streams/authorize` or `/api/stream-distribution/streams/authorize`
gets 404 from the gateway; nothing is forwarded to the service.

**Why this priority**: claim 5. The service already refuses non-POST, so this is defence in depth that makes the carve-out's own promise
true. Smallest change, lowest impact.

**Independent Test**: the existing gateway integration tests (`GatewayHostCharacterisationTests`, `GatewayApiPrefixTests`) with a
recording downstream: send each method to both paths and assert 404 and no forwarded request.

**Acceptance Scenarios**:

1. **Given** the gateway, **When** GET, PUT, PATCH, DELETE or HEAD targets either authorize path, **Then** the answer is 404 and the
   stream-distribution destination receives nothing.
2. **Given** the gateway, **When** POST targets either path, **Then** the answer is still 404 with nothing forwarded (unchanged).
3. **Given** the gateway, **When** any other stream-distribution path (`/stream-distribution/streams/{id}`, `/api/stream-distribution/streams`)
   is requested, **Then** it is still forwarded (the carve-out stays one path wide).

### Edge Cases

- A query string directly after `/api` or a remote prefix (`/api?x`) must keep matching after Story 1 tightens those keys.
- A navigation to a bare REST path with a query string must still be handed to the shell (Story 1 scenario 2).
- A navigation to a dotted path that **is** a real file in the shell root (e.g. `/favicon.ico`) is served as that file by static files
  before the fallback runs; Story 2 does not change that.
- Path matching for the carve-out stays case-insensitive as ASP.NET Core routing already makes it.
- Proxy mode (`Ui:Mode=Proxy`) caching is the Vite dev servers' business and is out of scope for Story 3.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The shell dev-server proxy MUST forward a request for any of the nine bare REST contexts when the context name is followed
  by `/`, by `?`, or by the end of the URL, and MUST NOT forward any other path. (Claim 1.)
- **FR-002**: The `/api`, `/remotes/cameras` and `/remotes/event-ingestion` proxy entries MUST take a request only when the prefix is
  followed by `/`, by `?`, or by the end of the URL. (Claim 4.)
- **FR-003**: The dev proxy's tests MUST exercise each key with the matching rule the pinned Vite version applies to it, against full
  request URLs including the query, not merely test the key's spelling.
- **FR-004**: In Static mode, a GET or HEAD page navigation (`Sec-Fetch-Dest: document`) to a path that no static file, `/api/…` or
  `/remotes/…` claims MUST get the shell's `index.html`, whether or not its last segment contains a dot. A non-navigation request to an
  unclaimed path with an extension MUST stay 404. (Claim 2.) The navigation signal is the one the gateway already uses (ADR-0192,
  `NavigationRuleFilter`).
- **FR-005**: In Static mode, every response that serves the shell's `index.html` (root, deep link, direct `/index.html`) and every
  response that serves a remote's `remoteEntry.js` MUST carry `Cache-Control: no-cache`. (Claim 3.)
- **FR-006**: The `streams/authorize` carve-out MUST answer 404 for every HTTP method on both the bare and the `/api` path, and MUST NOT
  forward any of them. (Claim 5.)
- **FR-007**: Each FR above MUST have a test observed failing before its fix, its failure output quoted in the PR (ADR-0139). Existing
  spec 491 and spec 208 tests MUST pass unmodified.

### Key Entities

None. No domain model, contract or persisted state changes.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: For all nine REST contexts, the dev proxy's forward/not-forward decision equals the gateway's for the URL set in Story 1
  (9 × 3 query/path shapes plus the 4 neighbour-word paths): 0 disagreements.
- **SC-002**: A Static-mode reload of a dotted console path shows the console: 1 of 1 navigations answered with the shell, 0 missing
  assets answered with HTML.
- **SC-003**: 100% of Static-mode responses for the 3 fixed-name entry points (shell index, two `remoteEntry.js`) carry the no-cache
  directive.
- **SC-004**: 0 requests reach the stream-distribution service through the gateway on either authorize path, across the 6 methods tested.
- **SC-005**: Every new test was seen red before its fix (failure quoted in the PR); the pre-existing gateway and dev-proxy suites pass
  without an edited assertion.

## Assumptions

**Owner-confirmed at the Specify gate (2026-11-12)**: the first, second and fourth bullets below (`no-cache`; a dotted deep link gets
the shell only with `Sec-Fetch-Dest: document`; `remoteEntry.js` is in scope) are decisions, no longer guesses.

- `no-cache` (revalidate on every use) rather than `no-store` is the intended directive: static files already send validators, so a
  revalidation of an unchanged entry point costs a 304. Hashed assets are left alone; giving them a long-lived
  `immutable` policy is an optimisation, not a defect, and not in scope.
- The navigation signal for Story 2 is `Sec-Fetch-Dest: document`, the same one ADR-0192's navigation rule and the dev proxy already use.
  A client that sends no such header (curl, an old browser) on a dotted path keeps today's 404. Extensionless paths keep getting the shell
  without the header, as spec 491 tests require.
- Story 1's loop consequence (claim 4) was inferred from `WebApplications.cs` wiring and not reproduced; the red test is at unit level
  (which entry takes the URL), not a reproduced loop.
- The `remoteEntry.js` half of Story 3 goes beyond the issue's text; it is the same defect in the same response path and is included rather
  than filed separately.
- The four stories touch disjoint files (`apps/management-web/src/app/devProxy.ts` + its test; `src/ApiGateway/UiHosting.cs` + the static
  tests, shared by Stories 2 and 3; `src/ApiGateway/GatewayHost.cs` + its tests) and can be planned as parallel slices, Stories 2 and 3 in
  sequence because they share `UiHosting.cs`.

## Out of Scope

- The fixed one-origin port 5180 and the shell/gateway mutual endpoint reference (ADR-0192's chosen design; the issue considered and did
  not file them).
- `Ui:Mode=Proxy` caching, the kiosk and wall paths, `@microsoft/signalr`.
- Long-lived caching for hashed assets.
