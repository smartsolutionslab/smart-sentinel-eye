# Verification — spec 491 (#3245)

§IV legs touched: **none**. `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` is unedited (no diff to `BoundaryTests.cs`, or to
anything under `tests/Architecture.Tests`, against `origin/develop`) and passes.

## Outcome

The gateway serves the operator console at one origin, in two modes: `Ui:Mode=Proxy` (dev: the three Vite dev servers, hot reload included) and
`Ui:Mode=Static` (the built bundles). REST is also reachable at `/api/<context>/...`, and when the UI is hosted a page navigation to a path the shell
shares with a REST route reaches the shell. With no `Ui` section the gateway is what it was.

**Observed, by what:**

| Claim                                                                                                                                   | Observed by                                                                                                                    |
| --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| ADR-0192's three riskiest assumptions: the hot-reload socket through YARP, a remote under its prefix, `NotContains` on an absent header | real Vite dev servers + real gateway (T012); 53 in-process tests (T015/T016); `BoundaryTests` green and unedited               |
| `/api` twins, the carve-out under the prefix, `Authorization` untouched, `X-Fab` removed                                                | 13 in-process tests with a parity guard over `appsettings.json` (T013/T014)                                                    |
| `Static` mode, `nosniff`, no path traversal, fail-fast on a missing root or entry file                                                  | 24 in-process tests, and the real gateway over real production builds of all three apps (T019-T022)                            |
| Page loads are not rate limited; the API is, at exactly 100 a minute                                                                    | real gateway, production limit: 300 page requests all 200; the 101st API call of the window 429 (T024)                         |
| Counts and gates                                                                                                                        | 4 frontend suites, `tsc`, `eslint`, `prettier`, `lint:e2e`, `typecheck:e2e`; the shard guard emulation 1227 = 1227 (T026-T027) |

**Not observed here, and why (stated, not hidden):**

- **A signed-in operator loading a cameras page and an event-ingestion page through the one origin (T018), and click-to-first-frame through the one origin (T029,
  SC-005).** Both need the full Aspire stack, and Docker Desktop was not running on this machine for the whole session (`docker ps`: the engine pipe does not exist).
  CI's full-stack e2e job exercises the first with `e2e/operator-shell-federation.spec.ts`; SC-005 has **no** automated check and stays open until the stack is
  run once with Docker, twice per the measurement rule.
- The gateway integration tests that boot Aspire (the existing `GatewayRoutingIntegrationTests` and friends) run in CI, not here.

## Task log

## T001 — baseline (before any change)

Branch `491-gateway-single-origin-shell` cut from `develop`, lockfile-true install (`pnpm install --frozen-lockfile`), `jsdom` read from
`node_modules` in each app: 30.1.3. One vitest at a time, vitest's JSON reporter, 0 skipped, 0 todo.

| App                          | Files | Passed | Failed | Noise blocks |
| ---------------------------- | ----- | ------ | ------ | ------------ |
| `management-event-ingestion` | 4     | 27     | 0      | 2            |
| `management-cameras`         | 24    | 151    | 0      | 47           |
| `management-web`             | 102   | 740    | 0      | 846          |

`Architecture.Tests`: 3254 / 3254 passed on 2026-11-11 before the extraction (the C# tree was identical to `develop`; observed on the
jsdom 31 branch) and **3254 / 3254 after it** (T004, 5 m 43 s).

## T002 — admin

- #3245 and epic #3248 are on Project #13 (status Todo).
- Content-Security-Policy and security-headers follow-up filed as **#3252**, on Project #13, linked from #3248.
- #3245 commented: the CORS list does not shrink in this phase (ADR-0192 decisions 6 and 7).
- Port 5180 for the dev address: no `.cs`, `.ts`, `.tsx`, `.json`, `.yml`, `.md`, `.mjs` or `.props` file names it (build output and
  `node_modules` excluded), and nothing listened on it on 2026-11-11.

## T004 — the extraction (behaviour-preserving)

`Program.cs` became a thin call into `GatewayHost.Build`. The first run after the move failed **one** architecture test,
`ServedHealthHostCompletenessTests.Every_host_that_maps_health_is_guarded_and_every_guarded_host_maps_health`: it reads each host's
`Program.cs` for the text `MapDefaultEndpoints(`, and the extraction had moved the call out. The guard was **not** edited. The call went
back into `Program.cs` (endpoint mappings are independent of middleware order in minimal hosting), and the suite is 3254 / 3254, `BoundaryTests`
unedited. Release build of the gateway: succeeded, no warning from a file in this change (the warnings are in `ServiceDefaults`).

## T005 — the in-process harness is faithful

`GatewayHostCharacterisationTests` (4 tests) pins today's behaviour on the harness, green before any routing change: a bare route forwards
with the prefix removed, `Authorization` unchanged and `X-Fab` removed; an unrouted path is a 404; the anonymous `streams/authorize` hook is a
404 and never forwarded; a source over `RateLimiting:PermitLimit` gets 429. 4 / 4.

## T006 / T007 — Ui proxy mode (red, then green)

`GatewayUiProxyTests`, 11 cases. **Red** against the gateway as it stood (9 failed, 2 passed: the no-`Ui` 404 is a characterisation, and the
unknown-remote 404 holds trivially while no UI exists). Failures, verbatim from the run: five HTTP cases `response.StatusCode should be
HttpStatusCode.OK but was HttpStatusCode.NotFound`, and both WebSocket cases `WebSocketException : The server returned status code '404' when
status code '101' was expected`. **Green** after `UiHosting`: 15 / 15 with the characterisation tests. `BoundaryTests` green with **no diff** to
`tests/Architecture.Tests/BoundaryTests.cs` against `origin/develop`; the gateway code references none of the three forbidden namespaces.

## T008 / T009 — remotes under their base

Vite `base` constants added; `management-cameras` 24 / 151 and `management-event-ingestion` 4 / 27, unchanged. `remotes.test.ts` gained three
cases for the relative paths. They arrived **green**, because `remotes.ts` already passes a relative path through minus a trailing slash and
needed no change; they are therefore proven by counterfactual, not by an observed red: (c1) not dropping the trailing slash fails 1 test;
(c2) replacing the event-ingestion path with an absolute literal fails 2; the file is restored from git after each.

## T010 / T011 — the shell's proxy and the AppHost wiring

`management-web/vite.config.ts` proxies `/api`, `/remotes/cameras`, `/remotes/event-ingestion` when `VITE_GATEWAY_PROXY_TARGET` is set;
`tsc`, `eslint src`, `prettier` clean. `AppHostOneOriginTests` (4 tests): **red** against the previous `WebApplications.cs` (3 failed, the
fixture test passed as a characterisation; failures: `Ui__Mode` absent, `endpoint.Port should be 5180 but was null`, `VITE_API_GATEWAY_URL`
absent with the value `/api`), **green** after: with the AppHost model tests, 42 / 42.

**One existing test was changed on purpose**: `AppHostEventIngestionRemoteTests.Management_web_s_environment_carries_the_event_ingestion_remote_url_key`
asserted that the shell's env value was an endpoint reference to the remote. ADR-0192 makes it the relative path and hands the remote's endpoint
to the gateway instead, so the test now asserts both (and keeps its purpose, a guard against a copy-paste between the remotes). This is the
second existing test whose assertion moved with an accepted decision, beside the three e2e URL substrings (T028).

## T012 — the spike, observed on a lighter rig (Docker was not running)

Docker Desktop was not running on this machine (`docker info`: the engine pipe does not exist), so the full Aspire stack, and therefore a
signed-in operator, was not available. The spike was observed on a rig that exercises the same claims without a backend: the **real** shell,
cameras and event-ingestion Vite dev servers, and the **real** gateway (`dotnet run --project src/ApiGateway`) with `Ui__Mode=Proxy` on
`localhost:5180`. All processes were stopped afterwards (ports 5173, 5176, 5177, 5180 free) and the tree was clean (only mtimes were touched).

**(a) Hot reload through the gateway: observed.** The `vite-hmr` WebSocket upgrades on `:5180` (`protocol: vite-hmr`), the server sends
`{"type":"connected"}`, and after touching `apps/management-web/src/main.tsx` it pushes `{"type":"full-reload","triggeredBy":…main.tsx}`.
Full-reload (not `update`) is Vite's answer for the entry module. For a component with an HMR boundary:
`apps/management-cameras/src/CamerasSurface.tsx` through `:5180/remotes/cameras/` pushed a real
`{"type":"update","updates":[{"type":"js-update","path":"/src/CamerasSurface.tsx",…}]}`, and the same through the shell's own proxy at
`:5173/remotes/cameras/`, and `EventIngestionSurface.tsx` through `:5180/remotes/event-ingestion/`. `BoundaryTests` is green and unedited (T007).

**(b) The remote loads under its prefix: observed at the HTTP level.** `remoteEntry.js` returns 200 for both remotes on `:5180` and on `:5173`
with identical sizes (187 522 B and 187 626 B). Every root-absolute URL inside the cameras entry is under `/remotes/cameras/`
(`…/@id/virtual:mf-exposes:…`, `…/node_modules/.vite/deps/@module-federation_runtime.js?v=…`, `…/@module-federation_runtime_helpers.js`), none at `/`,
which, from how Vite emits root-absolute URLs, is what would have hit the shell at the gateway origin without the `base` (plan D6/D8; this inference was not run without the `base`). The four URLs I fetched (`@id/virtual:mf-exposes`, the
runtime chunk, `@vite/client`, `src/CamerasSurface.tsx`) are 200 through both origins.

**Not observed here, and stated as such:** a signed-in browser rendering a cameras page and an event-ingestion page through the one origin
(needs Keycloak and the services). CI's full-stack e2e job exercises that path with `e2e/operator-shell-federation.spec.ts` (T028), and it
is re-attempted locally if Docker is started. **(c)** the header rule is automated in T015/T016 and recorded there.

## T013 / T014 — the `/api` routes (red, then green)

`GatewayApiPrefixTests`, 13 cases. **Red** against the gateway as it stood: 11 failed (nine `/api/<context>/things` cases and the rate-limit case
`first.StatusCode should be HttpStatusCode.OK`-style failures, plus the parity guard `twins should be [the nine contexts] but was …`),
3 passed trivially (an unknown `/api/…` is a 404 and the carve-out twin is a 404 while no route exists; they guard the later states). **Green**
after nine `api-` twin routes (same cluster, `CorsPolicy`, `RateLimiterPolicy`, `PathRemovePrefix` `/api/<context>`, `X-Fab` removal),
generated from the existing definitions, and the literal carve-out twin `POST /api/stream-distribution/streams/authorize` → 404. The parity test
reads `appsettings.json`: a tenth bare route without a twin, or an orphan twin, fails it.

## T015 / T016 — the navigation rule (Q1 option A), and spike (c)

`GatewayNavigationRuleTests`, 25 cases. **Red**: 11 failed, exactly the document-navigation cases (the nine contexts and the two shared paths
`/system-variables`, `/event-ingestion/types`), because a bare route proxied a navigation to the REST service; 14 passed. **Green** after
`NavigationRuleFilter`, which adds `Sec-Fetch-Dest NotContains document` to the bare routes.

**Spike (c): observed.** `NotContains` **does** match a request that has no `Sec-Fetch-Dest` at all (the nine "no fetch destination" cases reach
the REST service), and not one with `document`; `empty`, `script` and `iframe` reach the REST service.

**A design correction found while writing the tests:** the rule is **not** in `appsettings.json` as the contract first said. With no UI
hosted there is no page to fall through to, so an unconditional rule would make a gateway without `Ui` answer a browser that opens a REST path
(for example a `/health` check) with 404, a change to today's behaviour. It is a YARP config filter registered only when `Ui:Mode` is set; a test
pins that a UI-less gateway still answers such a navigation from the REST service. `contracts/gateway-paths.md` is updated accordingly.
The Release build caught analyzer S927 (the filter's parameter names must match YARP's `cancel`), which a Debug build does not.

## T017 — the one-origin value reaches the request URLs

`gatewayApiUrl` is `${origin}/${route}`, so `VITE_API_GATEWAY_URL=/api` yields `/api/camera-catalog/…`. No test pinned a relative origin or the
production guard, so `gatewayOneOrigin.test.ts` (3 cases, `apps/shared`) was added; the existing `gateway.test.ts` is untouched. The cases arrived
**green** (`gateway.ts` already handled it), so they are proven by counterfactual: (c1) not dropping the trailing slash fails 1; (c2) removing the
production guard fails 1; (c3) no separator between base and route fails 2; `gateway.ts` restored from git after each.

## T019 / T020 — Ui static mode (red, then green)

`GatewayUiStaticTests`, 24 cases. **Red**: 24 / 24 with `InvalidOperationException : Ui:Mode must be 'Proxy' when set; got 'Static'.`
(19 of them; 5 `exception.Message should contain …`). **Green** after the implementation: 24 / 24, and 77 / 77 across all the in-process
gateway suites. One real finding on the way: with routing selecting the `/remotes/{**path}` 404 endpoint first, the static-file middleware skipped
the request (ASP.NET skips static files once an endpoint is selected), so remote files came back 404 while the shell's worked; the fix is an
explicit `UseRouting()` after the static files, which is the implicit pipeline when no `Ui` is set. `BoundaryTests` green with no diff; Release
build and `dotnet format` clean.

## T021 / T022 — real production builds served by the real gateway

The three apps were built for production with `VITE_API_GATEWAY_URL=/api`, `VITE_CAMERAS_REMOTE_URL=/remotes/cameras`,
`VITE_EVENT_INGESTION_REMOTE_URL=/remotes/event-ingestion` (`tsc` 0 for the shell; `vite build` exit 0 for all three): shell 94 files / 1.8 MB,
cameras 46 / 912 KB, event-ingestion 33 / 628 KB. Every asset URL in each remote's `index.html` is under `/remotes/<name>/`; the remote
entries hold no root-absolute path (they import relatively, so they resolve beside the entry); the shell's index is at `/`.

The real gateway (`Ui__Mode=Static` over those three directories, port 5180) answered: `/`, `/layouts/123` and (`Sec-Fetch-Dest: document`)
`/system-variables` → 200 `text/html` with `nosniff`; `/favicon.svg` 200; `/remotes/cameras/remoteEntry.js`, a cameras chunk,
`/remotes/event-ingestion/remoteEntry.js` and `nav-manifest.json` → 200 with the right content types and `nosniff`; `/assets/does-not-exist.js`,
`/remotes/cameras/nope.js`, `/remotes/nope/x`, `/api/nope/x` → 404 (never the shell); a bare `/camera-catalog/health` with no header reached the proxy
(502, no backend), not the shell. The `VITE_API_GATEWAY_URL`-missing guard is pinned by `gatewayOneOrigin.test.ts` and the two remote variables by
the existing `remotes.test.ts`.

**Not observed:** the production shell and a remote _loading each other in a signed-in browser_ (needs Keycloak); the same as T012.

## T024 (partly) — page loads against the production limit

Same real gateway, default limit (100 per minute per source, `appsettings.json`): **300** requests from one source to `/`, `/favicon.svg` and
`/remotes/cameras/remoteEntry.js` → **300 × 200, zero 429**. The API half is below.

### T024, the API half

Two false starts first, so the figure is read correctly. (1) With the backend absent every `/api` call waited on service discovery, ran under 30 requests a minute, never filled a window, and returned 130 × 502 and **no 429**. That run was **inconclusive, not a pass**. (2) A destination on a closed loopback port is not instant on Windows (a refused connect takes about two seconds), so that run was slow too. The measurement below points the `camera-catalog` cluster at the gateway's own address (a fast 200, about 15 ms) via an environment override.

Real gateway in `Static` mode, default limit 100 per minute per source, one source address: after 3 probe calls, **130 calls to `/api/camera-catalog/x` in 11 s gave 97 × 200 then 33 × 429, the first 429 being the 101st request of the window**, so the limit is exactly 100. In the same throttled window, `/`, `/remotes/cameras/remoteEntry.js` and `/favicon.svg` from the same source all returned **200**, and a bare `/camera-catalog/x` was also limited (429). SC-003 (zero 429 on page loads at the production limit while API calls are still refused) is observed: 300 static requests earlier gave 300 × 200.

## T026 — shard coverage

The CI guard compares `--list-tests` with the union of the four shard filters. Emulated locally on the Debug build with the same filter:
**1227 tests in the full list, 1227 in the union, 0 missing from any shard, 0 listed twice.** All six new `Integration.Tests` classes
(`GatewayHostCharacterisationTests`, `GatewayUiProxyTests`, `GatewayApiPrefixTests`, `GatewayNavigationRuleTests`, `GatewayUiStaticTests`,
`AppHostOneOriginTests`) are registered in `shard-2.filter`.

## T027 — frontend suites and gates (one vitest at a time)

| App                          | Baseline (T001)       | After                 | Reason for any difference                                  |
| ---------------------------- | --------------------- | --------------------- | ---------------------------------------------------------- |
| `management-event-ingestion` | 4 files, 27 passed    | 4 files, 27 passed    | none                                                       |
| `management-cameras`         | 24 files, 151 passed  | 24 files, 151 passed  | none                                                       |
| `shared`                     | 92 files, 1395 passed | 93 files, 1398 passed | +1 file, +3 tests: `gatewayOneOrigin.test.ts`              |
| `management-web`             | 102 files, 740 passed | 103 files, 748 passed | +3 in `remotes.test.ts`; +1 file, +5 in `devProxy.test.ts` |

`tsc --noEmit` and `eslint src --max-warnings 0` exit 0 in all four apps; `prettier --check` exits 0 on every changed `apps/` and `e2e/` file;
`lint:e2e` and `typecheck:e2e` exit 0. Every pre-existing test file passes **unmodified** (the three new cases in `remotes.test.ts` were appended in
their own `describe`).

The final `management-web` run, after `devProxy.ts` and its tests were added (see T028): **103 files, 748 passed, 0 failed, 0 skipped** (baseline 102 / 740: +1 file, +8 tests = 3 in `remotes.test.ts` and 5 in `devProxy.test.ts`).

## T028 — the e2e spec, and what looking for it turned up

`e2e/operator-shell-federation.spec.ts` asserted `:5176/remoteEntry.js`, `:5177/remoteEntry.js` and `:5176/nav-manifest.json`. They now read
`/remotes/cameras/remoteEntry.js`, `/remotes/event-ingestion/remoteEntry.js` and `/remotes/cameras/nav-manifest.json` (four matchers, two titles, two
comments); what each test asserts is unchanged. That is more than the "three substrings" the plan counted. Not run locally (needs the Docker stack);
CI's e2e job is the observation.

Two risks the search for those strings found, and what was done:

1. **A readiness gate that could have failed.** `scripts/wait-for-e2e-stack.sh` waits on `curl -fsS http://localhost:5176/` and `:5177/`. With a Vite
   `base`, a remote no longer serves its own root. **Observed** (the two real dev servers): `/` answers `302` to `/remotes/<name>/`, and the script's
   curl exits **0**, so the gate still opens, unchanged. (It still proves only that something answers, as before.)
2. **Roughly ten e2e specs would have broken.** They capture the origin of an operator-page request and then `fetch(`${origin}/overlay-designer/...`)`
   with **bare** paths (`overlays`, `overlay-colour`, `overlay-layer-list`, `overlay-shapes`, `rule-switches-a-wall`, three `kiosk-*` specs that use the
   operator page). Once the shell calls `/api/...` on its own dev origin, that captured origin is the Vite server's, which proxied only `/api` and
   `/remotes/*`: bare paths would 404. Rewriting a dozen specs is the e2e cut-over (#3247). Instead the shell's dev proxy also answers the bare
   `/<context>/...` paths, with the gateway's own rule (`Sec-Fetch-Dest` not `document`), so `/system-variables` and `/event-ingestion/...` reloads still get the
   shell. It is `src/app/devProxy.ts` with 5 unit tests, proven by 3 counterfactuals (a navigation no longer recognised: 1 failure; the context key
   without its slash/end anchor: 2; `/api` pointed at the wrong target: 1). **Observed on the real shell dev server** (a stand-in on :5180): `/api/camera-catalog/cameras`
   and bare `/overlay-designer/overlays/1`, `/system-variables/variables`, `/event-ingestion/types` (no header) are forwarded unchanged;
   `/system-variables` and `/event-ingestion/types` with `Sec-Fetch-Dest: document` and `/layouts/123` return the shell's HTML.

Not changed, and stated: comments in some e2e specs still say the shell reaches a "cross-origin gateway"; true for the kiosk, no longer for the shell in
dev. They are descriptions, not assertions, and are left for the cut-over.

## T030 — final backend gates

- `dotnet build tests/Integration.Tests -c Release`: succeeded (the Release analyzers are errors in CI). `ApiGateway` Release build and `dotnet format` clean (T007, T016, T020).
- **A real miss, caught by an existing guard.** The full `Architecture.Tests` run on the final code failed **1 of 3254**:
  `IntegrationTestSelectionTests.Every_integration_test_class_declares_where_it_runs`. It requires every integration-test class to say where it
  runs, and five of my in-process gateway classes (`GatewayApiPrefixTests`, `GatewayHostCharacterisationTests`, `GatewayNavigationRuleTests`,
  `GatewayUiProxyTests`, `GatewayUiStaticTests`, 34 tests then) declared neither `[Collection(AspireCollection.Name)]` nor a category. They need no
  stack, so each now carries `[Trait("Category", "FixtureLogic")]` (the declaration the guard names for exactly that case; `AppHostOneOriginTests` already had it).
  After the fix: the guard class passes (19 / 19) and the gateway tests run under CI's `Category=FixtureLogic` selector: **78 / 78**.
- **What was not rerun.** A second full `Architecture.Tests` run was stopped by the harness for low memory (not a test failure), and was not restarted. The first run
  had **one** failure, the guard above, and the log shows 3217 passes before it stopped; so the claim is: the only failing test is fixed and verified by running that
  class and the gateway suite, and **a full 3254 / 3254 run on the final tree has not been observed here**. CI's backend job runs it.
- `tests/Architecture.Tests` has no diff against `origin/develop`; `BoundaryTests.cs` is unedited.
