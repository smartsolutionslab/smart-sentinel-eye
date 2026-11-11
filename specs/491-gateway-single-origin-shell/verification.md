# Verification — spec 491 (#3245)

Work in progress; written up as each task is observed. §IV legs touched: **none**.

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
