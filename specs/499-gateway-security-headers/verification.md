# Verification: spec 499 / issue #3252 (Phase 5)

Branch `feat/3252-gateway-csp`, HEAD `e232bf75`. **No Aspire stack was booted** (the machine runs out of memory; CI is the first proof for
anything that needs a browser or the live stack). What follows is what was observed without one. The verdict is "the gateway and AppHost
behaviour holds in-process; the browser behaviour is unobserved", not "verified end to end".

**Latency**: N/A — not on the event-to-overlay path (HTTP response headers on HTML pages).

## Observed

### 1. Build and suites (T002 baseline, post-change)

`dotnet build -c Release`: `67 Warning(s)  0 Error(s)` (all pre-existing S-rule advisories).

```text
dotnet test tests/Integration.Tests -c Release --no-build --filter "FullyQualifiedName~GatewaySecurityHeaders|FullyQualifiedName~AppHostContentSecurityPolicy"
Passed!  - Failed:     0, Passed:    43, Skipped:     0, Total:    43, Duration: 2 s - SmartSentinelEye.Integration.Tests.dll (net10.0)

dotnet test tests/Integration.Tests -c Release --no-build --filter "Category=FixtureLogic&(FullyQualifiedName~ApiGateway|FullyQualifiedName~OneOrigin|FullyQualifiedName~AppHostComposition)"
Passed!  - Failed:     0, Passed:   195, Skipped:     0, Total:   195, Duration: 7 s - SmartSentinelEye.Integration.Tests.dll (net10.0)

dotnet test tests/Architecture.Tests -c Release --no-build
Passed!  - Failed:     0, Passed:  3258, Skipped:     0, Total:  3258, Duration: 4 m 43 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

The existing suites pass with no assertion edited (the only edit to them is the fixture-only commit `94518780`, spec A3).

### 2. Real HTTP headers from the real gateway pipeline (in-process Kestrel, `GatewayTestHost`)

A throwaway test (not committed, deleted afterwards) issued `GET`s through the gateway's `HttpClient` and printed every response header.
Test origins: `identity.test`, `hub.test:5000`, `media.test:8889`. The policy string is identical on every HTML page; shown in full once.

Static mode, `/`, `/kiosk/`, `/wall/`, `/layouts/123` (deep link): all `200`, identical security headers.

```text
  Cache-Control: no-cache
  X-Content-Type-Options: nosniff
  Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' http://identity.test http://hub.test:5000 ws://hub.test:5000 http://media.test:8889; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'
  X-Frame-Options: SAMEORIGIN
  Referrer-Policy: same-origin
  Permissions-Policy: camera=(), microphone=(), geolocation=(), display-capture=(), payment=(), usb=()
  Content-Type: text/html; charset=utf-8
```

Static `/assets/app.js` (JS asset): no policy. Its `nosniff` comes from the static-file middleware, not this feature.

```text
  X-Content-Type-Options: nosniff
  Content-Type: text/javascript
```

Static `/api/camera-catalog/x` (JSON): none of the five headers, no HSTS.

```text
  Transfer-Encoding: chunked
  Content-Type: application/json; charset=utf-8
```

Proxy mode, shell `/`, upstream stub answering `text/html; charset=utf-8` **and its own** `Content-Security-Policy: default-src 'none'`.
The upstream value is replaced (one value only) by the development policy (`script-src 'self' 'unsafe-inline' 'unsafe-eval'`):

```text
  Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' http://identity.test http://hub.test:5000 ws://hub.test:5000 http://media.test:8889; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'
  X-Content-Type-Options: nosniff
  X-Frame-Options: SAMEORIGIN
  Referrer-Policy: same-origin
  Permissions-Policy: camera=(), microphone=(), geolocation=(), display-capture=(), payment=(), usb=()
  Content-Type: text/html; charset=utf-8
```

Proxy `/api/camera-catalog/x`: JSON, none of the headers.

### 3. T009 counterfactuals (both reverted; `git status` clean; tests green again, `Passed: 2`)

(a) The `__Media` line removed from `src/AppHost/WebApplications.cs`:

```text
  Failed ...AppHostContentSecurityPolicyTests.Every_origin_a_browser_app_reaches_directly_is_wired_into_the_gateways_policy
   Shouldly.ShouldAssertException : missing
["mediamtx/whep"]
    the gateway's Ui__ContentSecurityPolicy__* entries lack: mediamtx/whep
  Failed ...AppHostContentSecurityPolicyTests.The_three_policy_slots_are_wired_to_keycloak_the_layout_hub_and_the_media_server
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2
```

(b) A dummy `VITE_PROBE` on `kiosk-web`. **Deviation from T009's text**: the kiosk-web builder does not receive `mediamtx`, so the probe used
`keycloak.GetEndpoint("management")` instead of `mediamtx.GetEndpoint("api")`. It proves the same property: a new browser-visible endpoint
absent from the policy is named.

```text
  Failed ...Every_origin_a_browser_app_reaches_directly_is_wired_into_the_gateways_policy
   Shouldly.ShouldAssertException : missing
    the gateway's Ui__ContentSecurityPolicy__* entries lack: keycloak/management
Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2
```

### 4. The e2e spec parses

```text
pnpm exec playwright test --list e2e/content-security-policy.spec.ts
  [chromium] › content-security-policy.spec.ts:39:7 › the console is served with a content security policy and raises no violation
  [chromium] › content-security-policy.spec.ts:39:7 › the kiosk is served with a content security policy and raises no violation
  [chromium] › content-security-policy.spec.ts:39:7 › the wall display is served with a content security policy and raises no violation
  [chromium] › content-security-policy.spec.ts:50:5 › the signed-in console raises no violation on its cameras page
  (three cleanup teardowns)
Total: 7 tests in 4 files
```

It lands in the `chromium` project as intended.

## NOT observed

- **The e2e spec has never run**: not against a stack without the feature (T011's red) and not with it (T012's green). CI is the first run.
- **Premise P9, the HMR websocket connecting under `connect-src 'self'`**: needs a browser against Vite. (That `'self'` covers same-origin
  `ws:` is the spec's reading of CSP3, not a measurement.)
- **Premise P7, no inline `<script>` in built bundles** (so production `script-src 'self'` suffices): T001(a) was not run in this pass.
- **Premise that YARP-proxied HTML has `Content-Type` set before `OnStarting` fires (T001c)**: partly covered. The Proxy-mode test and the
  probe above go through the real YARP forwarder from an in-process stub answering `text/html`, and the headers appeared, so the ordering
  holds for YARP and that stub. Not covered: a real Vite dev server (its content type, content encoding, 304 revalidation of the shell).
- **Premise P8, Vite / Module Federation inline scripts under `'unsafe-inline'` in dev**: no browser; **refuted by CI run 43752365165**,
  where the three new e2e specs went red on `securitypolicyviolation {blockedURI: "eval", violatedDirective: "script-src"}` on `/`, `/kiosk/`
  and `/wall/`. The dev policy now includes `'unsafe-eval'`; the eval's source was not identified and the apps still worked.
- **Radix scroll-lock style injection (P6), still capture (P10), sign-in and silent renew without framing Keycloak (P11)** in a real browser:
  none observed.
- **T011 is partial**: the spec exists and parses, but the kiosk case's live-video seed ("until a decoded frame and one hub push") is
  unexercised.
- **T001(b), T011's red run, T012, T014, T015**: not done (stack, full e2e, follow-up issue, live `:5180` header read and HMR reload).
- **A1-A6 are decisions recorded in the spec, not measurements**: enforce rather than report in dev (A1), `style-src 'unsafe-inline'` (A2),
  `Referrer-Policy: same-origin` (A4), Keycloak keeps issuing refresh tokens for silent renew (A5), no `report-to` (A6). A3 is the declared
  fixture-only edit, commit `94518780`.
- **Deviation: no `Ensure.That` guards in the gateway.** plan.md and T006 call for them, but `src/ApiGateway` does not reference
  `Shared.Kernel`. Configuration validation throws `InvalidOperationException` naming the key, as `UiHosting`'s existing guards do.
- The origins in the headers above are the test fixtures' (`*.test`), not the AppHost's real endpoint references. Their equality with the
  browser-facing URLs is guarded only at model level, by the two AppHost tests.

## Phase 6 follow-up

Commit `c079a197` added six red theory rows (origin with userinfo or a non-ASCII host started the gateway instead of refusing); commit
`6286a882` makes `ContentSecurityPolicyOrigins` refuse both, and the 43 tests above are green with no test edited. The spec's claim that the
policy closes the hidden-iframe path was reworded to "raises the bar" (a policy-less same-origin document is a stepping stone). T011 is
still not run: the e2e spec has never executed against a stack.
