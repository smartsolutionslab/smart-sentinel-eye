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
