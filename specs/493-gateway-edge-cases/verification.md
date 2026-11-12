# Verification: spec 493 gateway and shell dev proxy edge cases

Raw evidence recorded as observed during Phase 4. Phase 5 and 6 notes are appended later.

## T001 Baseline (clean branch, before any change)

- (a) `vitest run src/app/devProxy.test.ts` in `apps/management-web`: `Test Files 1 passed (1)`, `Tests 5 passed (5)`.
- (b) `dotnet test tests/Integration.Tests -c Release --filter <five gateway classes>`:
  `Passed!  - Failed: 0, Passed: 77, Skipped: 0, Total: 77`.
- (c) `dotnet test tests/Architecture.Tests -c Release`: `Passed! - Failed: 0, Passed: 3254, Skipped: 0, Total: 3254`.

## T003 Characterisation: devProxy.test.ts lookup helper, run against the unfixed devProxy.ts

`entryFor(table, url)` replaces key-spelling lookups; every assertion on the entry found is unchanged. Result against the unfixed
`devProxy.ts`: `Test Files 1 passed (1)`, `Tests 5 passed (5)` (same count as T001(a)).

## T004 Red: dev proxy (devProxy.ts unfixed)

Command: `pnpm exec vitest run src/app/devProxy.test.ts` in apps/management-web. The guard test (`Keeps proxying the paths the gateway forwards...`) passed before the fix. Note: the plan listed the navigation-with-query case as a guard, but it is red today (no entry matches `/system-variables?tab=x`), so it is recorded here as red evidence.

```text
 RUN  v5.1.0 D:/Github/smart-sentinel-eye/.claude/worktrees/agent-a049dfc7de3ffecff/apps/management-web
 ❯ src/app/devProxy.test.ts (9 tests | 3 failed) 24ms
   ❯ The shell dev-server proxy (spec 491) (9)
     × Proxies a bare REST context followed straight by a query string 9ms
     × Does not proxy a path that only starts with a proxied word 1ms
     × Hands a page navigation with a query string back to Vite 1ms
⎯⎯⎯⎯⎯⎯⎯ Failed Tests 3 ⎯⎯⎯⎯⎯⎯⎯
 FAIL  src/app/devProxy.test.ts > The shell dev-server proxy (spec 491) > Proxies a bare REST context followed straight by a query string
AssertionError: camera-catalog: expected undefined to match object { target: 'http://gateway.test:5180' }
- Expected:
{
  "target": "http://gateway.test:5180",
}
+ Received:
undefined
 ❯ src/app/devProxy.test.ts:63:63
     61|
     62|     for (const context of REST_CONTEXTS) {
     63|       expect(entryFor(table, `/${context}?limit=5`), context).toMatchO…
       |                                                               ^
     64|     }
     65|   });
⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[1/3]⎯
 FAIL  src/app/devProxy.test.ts > The shell dev-server proxy (spec 491) > Does not proxy a path that only starts with a proxied word
AssertionError: /api-tokens: expected { …(2) } to be undefined
- Expected:
undefined
+ Received:
{
  "changeOrigin": true,
  "target": "http://gateway.test:5180",
}
 ❯ src/app/devProxy.test.ts:71:41
     69|
     70|     for (const url of ['/api-tokens', '/apiary', '/remotes/cameras-x',…
     71|       expect(entryFor(table, url), url).toBeUndefined();
       |                                         ^
     72|     }
     73|   });
⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[2/3]⎯
 FAIL  src/app/devProxy.test.ts > The shell dev-server proxy (spec 491) > Hands a page navigation with a query string back to Vite
AssertionError: expected undefined to be '/system-variables?tab=x' // Object.is equality
- Expected:
"/system-variables?tab=x"
+ Received:
undefined
 ❯ src/app/devProxy.test.ts:95:108
     93|     const entry = entryFor(devProxy(GATEWAY, REMOTES)!, '/system-varia…
     94|
     95|     expect(entry?.bypass?.({ url: '/system-variables?tab=x', headers: …
       |                                                                                                            ^
     96|       '/system-variables?tab=x',
     97|     );
⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[3/3]⎯
 Test Files  1 failed (1)
      Tests  3 failed | 6 passed (9)
   Start at  21:26:45
   Duration  1.99s (environment 70%, setup 24%, transform 4%, tests 1%, worker 1%, import 1%)
```

## T005 Green: dev proxy fixed

`vitest run src/app/devProxy.test.ts`: 1 file, 9 tests passed. tsc exit 0, eslint src exit 0, prettier --check clean.

## T006 Red: authorize carve-out for every method (GatewayHost.cs unfixed)

Command: `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~ApiGateway.GatewayApiPrefixTests"`. The guard `Gateway_still_forwards_a_sibling_stream_path_to_the_destination` passed before the fix.

```text
  Failed SmartSentinelEye.Integration.Tests.ApiGateway.GatewayApiPrefixTests.Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "DELETE", path: "/stream-distribution/streams/authorize") [247 ms]
  Error Message:
   Shouldly.ShouldAssertException : response.StatusCode
    should be
HttpStatusCode.NotFound
    but was
HttpStatusCode.OK
  Stack Trace:
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "DELETE", path: "/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "PUT", path: "/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "PATCH", path: "/api/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "PATCH", path: "/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "HEAD", path: "/api/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "GET", path: "/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "DELETE", path: "/api/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "PUT", path: "/api/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "HEAD", path: "/stream-distribution/streams/authorize")
  Failed Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(method: "GET", path: "/api/stream-distribution/streams/authorize")
Failed!  - Failed:    10, Passed:    15, Skipped:     0, Total:    25, Duration: 2 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

## T007 Green: carve-out maps every method

`GatewayApiPrefixTests` + `GatewayHostCharacterisationTests` (Release): `Passed! - Failed: 0, Passed: 29, Skipped: 0, Total: 29`; the existing POST carve-out tests unedited.

## T008 Red: dotted deep link on navigation (UiHosting.cs unfixed)

Command: `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~ApiGateway.GatewayUiStaticTests"`. The four guards (non-navigation 404, navigation under /remotes and /api 404, POST 404) passed before the fix.

```text
  Failed SmartSentinelEye.Integration.Tests.ApiGateway.GatewayUiStaticTests.Gateway_shows_the_shell_when_a_browser_reloads_a_deep_link_whose_last_segment_has_a_dot [101 ms]
  Error Message:
   Shouldly.ShouldAssertException : response.StatusCode
    should be
HttpStatusCode.OK
    but was
HttpStatusCode.NotFound
  Stack Trace:
Failed!  - Failed:     1, Passed:    28, Skipped:     0, Total:    29, Duration: 3 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

## T009 Green: UiRoot with a catch-all fallback and navigation rule

Five gateway classes (Release): `Passed! - Failed: 0, Passed: 93, Skipped: 0, Total: 93` (77 baseline + 11 for the carve-out + 5 here); all pre-existing cases unedited. Not isolated: whether removing only the handler's extension check would also have turned T008 green (the `nonfile` claim in research R2), since the pattern and the handler changed in one commit.

The wider filter `FullyQualifiedName~ApiGateway` also picks up `GatewayRoutingIntegrationTests` and `GatewayRateLimitIntegrationTests`, which need the Aspire stack and fail in 1 ms here (not run by this spec).

## T010 Red: entry points carry no Cache-Control (UiRoot.cs without the rule)

Command: `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~ApiGateway.GatewayUiStaticTests"`. The two asset guards passed before the fix. Note: the first draft used `CacheControl?.NoCache.ShouldBeTrue()`, which passed vacuously on a null header (37 passed); it was replaced by `CacheControl.ShouldNotBeNull().NoCache...` before this red run.

```text
  Failed SmartSentinelEye.Integration.Tests.ApiGateway.GatewayUiStaticTests.Gateway_makes_a_browser_revalidate_every_entry_point(path: "/layouts/123") [110 ms]
  Error Message:
   Shouldly.ShouldAssertException : response.Headers.CacheControl
    should not be null but was
  Stack Trace:
  Failed Gateway_makes_a_browser_revalidate_every_entry_point(path: "/layouts/123")
  Failed Gateway_makes_a_browser_revalidate_every_entry_point(path: "/remotes/event-ingestion/remoteEntry.js")
  Failed Gateway_makes_a_browser_revalidate_every_entry_point(path: "/")
  Failed Gateway_makes_a_browser_revalidate_every_entry_point(path: "/remotes/cameras/remoteEntry.js")
  Failed Gateway_makes_a_browser_revalidate_every_entry_point(path: "/index.html")
  Failed Gateway_makes_a_browser_revalidate_every_entry_point(path: "/cameras/cam.01")
Failed!  - Failed:     6, Passed:    31, Skipped:     0, Total:    37, Duration: 3 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

## T011 Green: no-cache on entry points

Five gateway classes (Release): `Passed! - Failed: 0, Passed: 101, Skipped: 0, Total: 101` (93 + 8 new); earlier cases unedited.

## T012 Full local gate

- Five gateway classes (Release): 101 passed, 0 failed (baseline 77; +24 new cases).
- `dotnet test tests/Architecture.Tests -c Release`: `Passed! - Failed: 0, Passed: 3254` (baseline 3254, BoundaryTests unedited).
- Release build of Integration.Tests and everything it references (including ApiGateway): no errors, no ApiGateway warnings. `dotnet format --verify-no-changes` clean on src/ApiGateway and tests/Integration.Tests/ApiGateway.
- management-web: `tsc --noEmit` exit 0, `eslint src --max-warnings 0` exit 0, `prettier --check src` clean, full `vitest run` 103 files / 752 tests passed (devProxy.test.ts 5 -> 9).
- `pnpm test:guards`: 4 suites, 350 pass, 0 fail, exit 0.

## T013 Verify: quickstart step 2 on a real Vite dev server

Port 5173 was free (`netstat -ano | grep ":5173 "` empty before starting). No Aspire, no Docker. Latency: N/A (not on the §IV event-to-overlay path).

How: one throwaway Node server listened on 5899 (the gateway target) and on 5176/5177 (the two remote origins `vite.config.ts` hard-codes), answering `STUB:<port> <url received>`. The dev server ran as `VITE_GATEWAY_PROXY_TARGET=http://localhost:5899 npx vite` in `apps/management-web` (Vite 8.3.6, port 5173). "After" used the tree's `devProxy.ts`; "before" used an untracked copy of `git show origin/develop:apps/management-web/src/app/devProxy.ts` plus a copy of `vite.config.ts` importing it (`npx vite --config vite.config.before.ts`); both copies were deleted afterwards. Each probe was `curl -s localhost:5173<path>`; "navigation" adds `-H "Sec-Fetch-Dest: document"`; the upgrade was `curl --http1.1 -H "Connection: Upgrade" -H "Upgrade: websocket" ...`.

| Request                              | Before (origin/develop)                        | After (this branch)                            |
| ------------------------------------ | ---------------------------------------------- | ---------------------------------------------- |
| `/system-variables?limit=5`          | shell HTML (not proxied)                       | `STUB:5899 /system-variables?limit=5`          |
| `/system-variables/x?limit=5`        | `STUB:5899 /system-variables/x?limit=5`        | `STUB:5899 /system-variables/x?limit=5`        |
| `/api/x`                             | `STUB:5899 /api/x`                             | `STUB:5899 /api/x`                             |
| `/apiary`                            | `STUB:5899 /apiary` (wrongly proxied)          | shell HTML                                     |
| `/api-tokens`                        | `STUB:5899 /api-tokens` (wrongly proxied)      | shell HTML                                     |
| `/remotes/cameras/x`                 | `STUB:5176 /remotes/cameras/x`                 | `STUB:5176 /remotes/cameras/x`                 |
| `/remotes/camerasx`                  | `STUB:5176 /remotes/camerasx` (wrong)          | shell HTML                                     |
| navigation `/system-variables?tab=x` | shell HTML                                     | shell HTML                                     |
| WS upgrade `/remotes/cameras/ws`     | `101`, `STUB:5176 UPGRADE /remotes/cameras/ws` | `101`, `STUB:5176 UPGRADE /remotes/cameras/ws` |

Observed: the bare-context-with-query case (the bug) now reaches the upstream with its query intact, and the three prefix-collision paths (`/apiary`, `/api-tokens`, `/remotes/camerasx`) no longer leak to the gateway or remote. Unchanged routes and the WebSocket upgrade still forward. Note the quickstart's `/audit-observability` rows were replaced by the equivalent `/system-variables` rows from the task brief; both are in `REST_CONTEXTS`.

Cleanup: dev server and stub killed; `netstat` shows 5173, 5176, 5177, 5899 free.

Not covered: the gateway halves (US2-US4) were observed only by the in-process `FixtureLogic` tests against the real gateway host in Static mode, not against a running stack; Static mode was not run against a built management-web bundle; the stubbed upstream proves what URL is forwarded, not that a real gateway or remote answers it.
