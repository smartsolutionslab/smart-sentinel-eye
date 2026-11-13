# Verification: The kiosk and the wall display on one origin

Written as observed, task by task. Stage 1 (T001-T004) only.

## T001 — rebase over spec 493 (merged as PR #3264)

Rebased onto `origin/develop`; no textual conflict in the spec docs. `.specify/feature.json` took develop's value
(`specs/493-gateway-edge-cases`) and was set back to `specs/494-kiosk-wall-single-origin` in the T001 commit.

Re-check of plan.md "What 494 rebases over" against 493's **merged** diff (`git diff fe07298c..origin/develop -- src apps tests`;
7 files, +291/-61):

| Plan row                                           | Merged reality                                                                                                                                                                                                                         | Verdict                    |
| -------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- |
| `MapShellFallback` in `UiHosting.cs`               | **Gone.** The fallback moved into a new `src/ApiGateway/UiRoot.cs`: `record UiRoot(RequestPath, Directory, EntryFile)` with `ServeFiles(app)` and `MapFallback(app)`. `MapFallback` maps `RequestPath + "/{**path}"`.                  | Differs (better than plan) |
| Static-file options (`StaticFiles` helper)         | **Gone** with its `PhysicalFileProvider` using; `UiRoot.ServeFiles` carries `RequestPath`, `nosniff`, and `no-cache` on `{RequestPath}/{EntryFile}` (the entry file of every root, `index.html` or `remoteEntry.js`).                  | Differs                    |
| Fallback rule                                      | Claims a path when the request is a read (GET/HEAD) **and** (`Sec-Fetch-Dest` contains `document` **or** the last segment has no extension); `no-cache`, `nosniff`, `text/html; charset=utf-8`. Non-reads and other dotted paths: 404. | Holds (matches 493's spec) |
| `StaticUiStack`/`UiProxyStack` new required keys   | None added by 493. The fixtures still supply only Shell/Cameras/EventIngestion keys.                                                                                                                                                   | Holds (no new keys)        |
| `streams/authorize` carve-out for every method     | `GatewayHost.cs`: `MapPost` became `Map` for both paths.                                                                                                                                                                               | Holds, untouched by 494    |
| `devProxy.ts` anchored keys, query string accepted | `apps/management-web/src/app/devProxy.ts` (edited) and its test (+76).                                                                                                                                                                 | Holds, untouched by 494    |
| Extended tests                                     | `GatewayUiStaticTests.cs` (+98) and `GatewayApiPrefixTests.cs` (+53). No change to `GatewayHostCharacterisationTests`. `GatewayNavigationRuleTests` already existed on develop.                                                        | Differs slightly           |
| Shard filter entries for new 493 classes           | None: 493 added no test class, so `shard-*.filter` is unchanged and there is no conflict to resolve.                                                                                                                                   | Differs                    |

Consequences for 494:

- Plan D5 ("reuse the fallback, prefix-taking helper") is **already satisfied**: `UiRoot` is per-root and takes the prefix. T003 is a
  no-op (below). Kiosk and wall become two more `UiRoot` values (`/kiosk`, `/wall`) with entry file `index.html`.
- A prefixed `MapFallback("/kiosk/{**path}")` is a route like any other; the plan's risk "the `/kiosk` redirect endpoint loses to the YARP
  `/kiosk/{**catch-all}` route" is unchanged and still pinned by the red test in W3.
- **`iframe` is not a navigation.** `IsPageNavigation` accepts `Sec-Fetch-Dest: document` only; a request with `iframe` (or `frame`)
  gets the extension rule instead, so a dotted deep link loaded inside a frame would 404. The kiosk and the wall are top-level
  documents: `grep -rniE iframe apps/kiosk-web/src apps/kiosk-web/*.html apps/shared` finds nothing, and the gateway has no framing
  code. The rule is therefore accepted unchanged for the `/kiosk/` and `/wall/` roots.
- Source comments: `UiRoot.cs` carries no spec/task numbers; 494 code follows suit.

## T002 — baseline on the rebased branch

`pnpm install --frozen-lockfile --prefer-offline` clean. One process at a time, no Aspire, no Docker.

| Suite                                                                                                                                                                    | Result                              |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------- |
| `apps/kiosk-web` vitest                                                                                                                                                  | 39 files, 392 tests passed          |
| `apps/shared` vitest                                                                                                                                                     | 93 files, 1398 tests passed         |
| `apps/kiosk-web` `tsc --noEmit` / `eslint src --max-warnings 0` / `prettier --check .`                                                                                   | exit 0 / 0 / 0                      |
| Repo root `pnpm test:guards`                                                                                                                                             | exit 0, 350 tests, 350 pass, 0 fail |
| Five gateway classes, Release (`GatewayHostCharacterisationTests`, `GatewayUiProxyTests`, `GatewayApiPrefixTests`, `GatewayNavigationRuleTests`, `GatewayUiStaticTests`) | 104 passed, 0 failed, 0 skipped     |
| `tests/Architecture.Tests` Release                                                                                                                                       | 3254 passed, 0 failed, 0 skipped    |

The CI baseline for the `kiosk`/`wall` Playwright projects and the three `layout-composition` push integration classes is read from
`develop`'s CI at PR time, not rerun here (needs Aspire).

## T003 — extract the fallback into a prefix-taking helper

**No-op.** 493's merged `UiRoot.MapFallback` is already the per-prefix helper this task asked for: it takes the route prefix
(`RequestPath`) and the entry file, and the shell calls it as `StaticRoots.Read(...).Shell.MapFallback(app)`. Extracting it again would
be an invented refactor. No source change and no commit for T003; the task is ticked on this note.

## T004 — gateway fixtures supply the four new keys

Owner-approved fixture-only edit (spec Q4). The gateway does not read `Ui:Proxy:Kiosk/Wall` or `Ui:Static:KioskRoot/WallRoot` yet, so
the edit must be a no-op for it. Observed: the same five gateway classes, Release, before and after: see below.

Five gateway classes, Release: **104 passed, 0 failed, 0 skipped before and 104/0/0 after** the fixture edit
(`UiProxyStack` gains `Kiosk` and `Wall` stub destinations and the two `Ui:Proxy` keys; the `StaticUiStack` in `GatewayUiStaticTests`
gains `kiosk` and `wall` temp roots, each with an `index.html`, and the two `Ui:Static` keys). The diff touches no assertion, method or
`[InlineData]` row. The gateway does not read the keys yet, so the change is a no-op for it, as intended.

## T005 and T006 — hub CORS on layout-composition

**Red, observed (T005, commit 12fea266).** The type did not exist, so the first run failed to compile (`CS0246`). With the empty stub
(`AddLayoutHubCors`/`UseLayoutHubCors` returning their input) `dotnet test tests/Integration.Tests -c Release --filter
FullyQualifiedName~LayoutHubCorsTests` ran and failed on assertions, four of seven:

```text
Failed ...LayoutHubCorsTests.A_token_less_preflight_from_an_allowed_origin_is_answered_with_the_policy
   Shouldly.ShouldAssertException : response.StatusCode
    but was
Failed ...LayoutHubCorsTests.An_actual_request_to_the_hub_path_from_an_allowed_origin_carries_the_echoed_origin_and_credentials
   at System.Net.Http.Headers.HttpHeaders.GetValues(HeaderDescriptor descriptor)   (no Access-Control-Allow-Origin header)
Failed ...LayoutHubCorsTests.A_wildcard_origin_in_the_configuration_refuses_to_start_naming_the_key(origin: "*")
   Shouldly.ShouldAssertException : Task `async () => { await using var host = await CorsHost.StartAsync(origin); }` (nothing thrown)
Failed ...LayoutHubCorsTests.A_wildcard_origin_in_the_configuration_refuses_to_start_naming_the_key(origin: "https://*.fab.example")
   (same)
```

The three negative cases (disallowed origin, REST path, no `LayoutHub` section) assert an absence and arrive green against the stub by
nature; the harness is proven live by the positive cases above, which fail against the stub and pass once the policy exists (a
counterfactual by construction: the same host, with only the policy added).

**Green (T006).** `LayoutHubCorsTests` 7/7. `Architecture.Tests` Release 3254/3254 (`BoundaryTests` no diff, `EndpointScopeDeclarationTests`
and `ServedHealthHostCompletenessTests` included). `dotnet format --verify-no-changes` clean on LayoutComposition Infrastructure, Api and
Integration.Tests. The `MapHub` line is untouched.

## T013 and T014 — the kiosk and wall routes in proxy mode

**Red, observed (T013, commit 1a2c7540).** `dotnet test tests/Integration.Tests -c Release --filter FullyQualifiedName~GatewayKioskUiProxyTests`
against the unchanged gateway: 12 of 13 failed, every one on an assertion (the shell's catch-all answered `/kiosk/...` and `/wall/...`):

```text
Failed ...GatewayKioskUiProxyTests.Gateway_sends_a_kiosk_or_wall_path_to_its_dev_server_with_the_path_unchanged(path: "/kiosk/", expectedDestination: "kiosk")
   Shouldly.ShouldAssertException : echo.Name  (expected "kiosk", but was "shell")
   ... the same for /kiosk/layouts/abc, /kiosk/src/main.tsx, /wall/, /wall/walls/x, /wall/@vite/client
Failed ...Gateway_redirects_the_bare_prefix_to_the_slashed_path(path: "/kiosk" | "/wall")
   Shouldly.ShouldAssertException : (int)response.StatusCode  (200 from the shell, not 3xx)
Failed ...Gateway_refuses_to_start_in_proxy_mode_when_an_instance_is_not_configured(configurationKey: "Ui:Proxy:Kiosk" | "Ui:Proxy:Wall")
   Shouldly.ShouldAssertException : Task `UiProxyStack.StartAsync(...)` (nothing thrown)
Failed ...Gateway_carries_a_hot_reload_websocket_to_the_kiosk_or_wall_dev_server(pathAndQuery: "/kiosk/?token=abc" | "/wall/?token=abc")
   Shouldly.ShouldAssertException : Encoding.UTF8.GetString(...) (expected "kiosk:ping", but was "shell:ping")
```

The one that arrived green is `Gateway_does_not_rate_limit_or_cors_the_kiosk_and_wall_routes`: the shell's catch-all carries no policy
either, so today it passes for the wrong destination. It is a guard on the new routes not acquiring a policy, not a red; it is
characterisation by construction (and is the one test that went red mid-way, below, when the redirect loop answered 302).

**Plan claim that proved false: the redirect as an endpoint.** The plan allowed a `/kiosk` redirect endpoint with an explicit order. A
`MapGet("/kiosk")` endpoint also matches `/kiosk/` (ASP.NET Core ignores the trailing slash), so every page request was redirected to
itself (5 failures: 302 where 200 was expected, including the WebSocket upgrade). The redirect is therefore a small middleware matching
the exact path `/kiosk` / `/wall` on GET, registered in `MapGatewayUi`; the YARP routes stay the plain `/kiosk/{**catch-all}` and
`/wall/{**catch-all}`.

**Green (T014).** `GatewayKioskUiProxyTests` 13/13. Fixture-logic gateway classes with assertions unmodified (`GatewayHostCharacterisationTests`,
`GatewayUiProxyTests`, `GatewayApiPrefixTests`, `GatewayNavigationRuleTests`, `GatewayUiStaticTests`, `GatewayEdgeHeaderTransformTests`),
`AppHostOneOriginTests`, `AppHostWebAppHostingTests`: 144 passed, 0 failed. `BoundaryTests` 29/29, no diff (the gateway gains no
assembly reference). `dotnet format --verify-no-changes` clean on ApiGateway and AppHost. The `AppHost` gives the gateway
`Ui__Proxy__Kiosk` / `Ui__Proxy__Wall` in the same commit.

Not run: `GatewayRoutingIntegrationTests` and `GatewayRateLimitIntegrationTests` (Aspire fixture, no Docker here); both fail instantly
in this environment with or without the change.

## T015 and T016 — AppHost wiring for the hub

**Red, observed (T015, commit 24284618).** `--filter FullyQualifiedName~AppHostKioskOneOriginTests` against the AppHost as of T014:
3 of 4 failed on assertions:

```text
Failed ...Each_kiosk_instance_reaches_the_hub_on_layout_composition_directly(instance: "kiosk-web" | "kiosk-wall")
   Shouldly.ShouldAssertException : environment  (no VITE_LAYOUT_HUB_URL key)
Failed ...Layout_composition_admits_the_kiosk_the_wall_and_the_gateway_origins_from_endpoint_references
   Shouldly.ShouldAssertException : origins  (expected ["api-gateway","kiosk-wall","kiosk-web"], was [])
```

`The_gateway_is_told_where_the_kiosk_and_the_wall_dev_servers_are` arrived green: T014 landed the `Ui__Proxy__Kiosk`/`Wall` keys in its
own commit, as the task order requires. Counterfactual by construction: before 349d744e `WithOneOriginUi` set exactly three
`Ui__Proxy__*` keys (the diff of that commit and the old snapshot, which listed three), so the same assertions fail on 1a2c7540.

**Green (T016).** `AppHostKioskOneOriginTests` 4/4; `AppHostOneOriginTests` and `AppHostWebAppHostingTests` pass with assertions
unmodified; `dotnet format --verify-no-changes` clean on AppHost. The allowed origins and the hub URL are endpoint references
(`LayoutHub__Cors__AllowedOrigins__0..2` = `kiosk-web`, `kiosk-wall`, `api-gateway`; `VITE_LAYOUT_HUB_URL` = `{layout-composition http
url}/hubs/layouts`); `VITE_LAYOUT_HUB_ORIGIN` is kept. No `WaitFor` was added to `layout-composition`.

**Composition snapshots moved by the decision (recorded, not adjusted).** The characterisation test has no regeneration switch, so the
composed model was rendered from the test's own `ArgumentsFor`/`RenderAsync` by a throwaway reflection test (deleted, not committed),
in Release, and written over `Developer`, `EndToEndJob` and `Unparseable` (the `IntegrationFixture` and `Publish` lanes are unchanged:
they carry no web apps). The diff is, in each of the three lanes, only additions and index shifts:

- `layout-composition`: three `Reference` + `EnvironmentCallbackAnnotation` pairs, `LayoutHub__Cors__AllowedOrigins__0/1/2`
  (`{kiosk-web.bindings.http.url}`, `{kiosk-wall...}`, `{api-gateway...}`), at [44]-[49].
- `api-gateway`: `Ui__Proxy__Kiosk={kiosk-web.bindings.http.url}` and `Ui__Proxy__Wall={kiosk-wall.bindings.http.url}` at [55]-[58].
- `kiosk-web` and `kiosk-wall`: a `Reference` to `layout-composition` plus `VITE_LAYOUT_HUB_URL={layout-composition.bindings.http.url}/hubs/layouts`,
  the following annotation indices shifted by two.
- Nothing else moved (`git diff --stat`: 35 lines changed in each file).

## T018 and T019 — static mode serves the kiosk and the wall bundles

**Red, observed (T018, commit 96554fca).** `--filter FullyQualifiedName~GatewayKioskUiStaticTests` against the gateway as of T016: 19 of 20
failed on assertions:

```text
Failed ...Gateway_falls_back_to_the_prefixs_own_index_for_a_deep_link_without_an_extension(path: "/kiosk/" | "/kiosk/layouts/abc" | "/wall/" | "/wall/walls/x")
   Shouldly.ShouldAssertException : body  (the shell's index answered, not "kiosk-index"/"wall-index")
Failed ...Gateway_makes_a_browser_revalidate_the_kiosk_and_wall_entry_point(six paths, incl. /kiosk/layouts/cam.01 and /wall/walls/x.y with Sec-Fetch-Dest: document)
   Shouldly.ShouldAssertException : await response.Content.ReadAsStringAsync()  (shell index again)
Failed ...Gateway_serves_a_present_kiosk_asset_with_nosniff_and_answers_a_missing_one_with_404
   Shouldly.ShouldAssertException : asset.StatusCode  (404: the root is not served)
Failed ...Gateway_redirects_the_bare_prefix_to_the_slashed_path(path: "/kiosk" | "/wall")
   Shouldly.ShouldAssertException : (int)response.StatusCode  (200, the shell's fallback)
Failed ...Gateway_refuses_to_start_when_a_root_is_not_configured / ..._is_not_a_directory / ..._has_no_index_html (Kiosk and Wall each)
   Shouldly.ShouldAssertException : Task `StaticUiStack.StartAsync(...)` (nothing thrown)
```

The one that arrived green is `Gateway_does_not_rate_limit_kiosk_and_wall_pages` (300 page requests at `PermitLimit` 100, zero 429): the shell's
fallback carries no limiter either, so it passes for the wrong body. It guards the new fallbacks against acquiring a policy; it is
characterisation by construction, not a red.

**Outranking pinned.** `Gateway_falls_back_to_the_prefixs_own_index...` asserts `shell-index` is absent for `/kiosk/...` and `/wall/...`:
the per-prefix `MapFallback("/kiosk/{**path}")` outranks the root `MapFallback("/{**path}")` by template specificity, and the test fails
(shell body) before the prefixes are mapped. The `/kiosk` bare path is answered by the same exact-path middleware as in proxy mode.

**Fixture edit beyond T004 (flagged).** The nested `StaticUiStack` in `GatewayUiStaticTests.cs` was `private`, so a new class could not use
it: it is now `internal`, and `StartAsync` gained an optional `kioskFiles` parameter (the kiosk asset case). No assertion, method or
`[InlineData]` row of `GatewayUiStaticTests` changed.

**Green (T019).** `GatewayKioskUiStaticTests` 20/20. Gateway and AppHost fixture-logic classes with assertions unmodified (the six gateway
classes, `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostWebAppHostingTests`, `AppHostCompositionCharacterisationTests`): 252
passed, 0 failed. `Architecture.Tests` Release 3254/3254 (`BoundaryTests` no diff). `dotnet format --verify-no-changes` clean on ApiGateway,
AppHost, Integration.Tests and the two LayoutComposition projects; `dotnet build -c Release src/AppHost` succeeds.

## T020 — built bundles through the real gateway in static mode

Observed on the integrated tree (branch `494-kiosk-wall-single-origin`). No Aspire, no Docker. Gateway started with
`dotnet run --project src/ApiGateway -c Release --no-build --no-launch-profile`, `ASPNETCORE_URLS=http://127.0.0.1:5380` (a free
port; 5180 was not used), `Ui__Mode=Static` and the five `Ui__Static__*Root` variables pointing at five `vite build` output
directories in the scratchpad. Gateway stopped afterwards; nothing listens on 5380 or 5180.

Builds, all from the worktree, `VITE_API_GATEWAY_URL`, `VITE_KEYCLOAK_URL` set (and `VITE_LAYOUT_HUB_URL` for the two kiosk builds):

- `apps/kiosk-web`: plain build, and `VITE_KIOSK_MODE=wall` build, each with its own `--outDir`.
- `apps/management-web`, `apps/management-cameras`, `apps/management-event-ingestion`: plain builds (the remotes use their own constant base).

Built `index.html` asset URLs (every `href`/`src`): plain build: `/kiosk/favicon.svg`, `/kiosk/assets/IBMPlexSans-Regular-Latin1-*.woff2`,
`/kiosk/assets/IBMPlexSans-SemiBold-Latin1-*.woff2`, `/kiosk/assets/index-Cie0L7Ps.js`, `/kiosk/assets/index-D-LEwbHq.css`. Wall build: the same
five under `/wall/` (`/wall/assets/index-CNo3pni-.js`). Nothing under `/` in either.

Requests (`curl -s -o /dev/null -D -`, Content-Type, Cache-Control, Location; every 200 also carried `X-Content-Type-Options: nosniff`):

| Request                                              | Status | Content-Type               | Cache-Control | Location  |
| ---------------------------------------------------- | ------ | -------------------------- | ------------- | --------- |
| `/kiosk`                                             | 302    |                            |               | `/kiosk/` |
| `/kiosk/`                                            | 200    | `text/html; charset=utf-8` | `no-cache`    |           |
| `/kiosk/layouts/abc` (deep link)                     | 200    | `text/html; charset=utf-8` | `no-cache`    |           |
| `/kiosk/layouts/cam.01` + `Sec-Fetch-Dest: document` | 200    | `text/html; charset=utf-8` | `no-cache`    |           |
| `/kiosk/assets/index-Cie0L7Ps.js`                    | 200    | `text/javascript`          | none          |           |
| `/kiosk/assets/IBMPlexMono-Regular-Latin1-*.woff2`   | 200    | `font/woff2`               | none          |           |
| `/kiosk/assets/nope.js`                              | 404    |                            |               |           |
| `/wall`                                              | 302    |                            |               | `/wall/`  |
| `/wall/`, `/wall/walls/x`                            | 200    | `text/html; charset=utf-8` | `no-cache`    |           |
| `/wall/assets/index-CNo3pni-.js`                     | 200    | `text/javascript`          | none          |           |
| `/wall/assets/nope.js`                               | 404    |                            |               |           |
| `/` (shell)                                          | 200    | `text/html; charset=utf-8` | `no-cache`    |           |
| `/remotes/cameras/remoteEntry.js`                    | 200    | `text/javascript`          | `no-cache`    |           |
| `/remotes/event-ingestion/remoteEntry.js`            | 200    | `text/javascript`          | `no-cache`    |           |

Which bundle answered: the body of `/kiosk/layouts/abc` contains `src="/kiosk/assets/` once, that of `/wall/walls/x` contains
`src="/wall/assets/` once, and `/` contains `src="/assets/mf-entry-bootstrap-*.js"` (the shell's): each prefix returns its own `index.html`.

Production guards, observed: `vite build` does **not** fail without `VITE_KEYCLOAK_URL`, `VITE_API_GATEWAY_URL` or `VITE_LAYOUT_HUB_URL`
(three builds with one variable unset each: `built`, exit 0). The guards are module-load throws (`import.meta.env.PROD` and an
empty value), so they fire when the bundle is first loaded, as `docs/deployment-frontend-env.md` already says (the task text said
"throws" for the build; it is the load). The bundle of the build without `VITE_API_GATEWAY_URL` contains the message
`VITE_API_GATEWAY_URL must be set in production builds`; the Keycloak message is in the same bundle. **Not covered:** loading a bundle in a browser to see the throw, and the hub
message in a built bundle (the unit test of `resolveLayoutHubUrl` covers the hub guard; the code is unchanged by this spec).
Not covered either: a browser rendering the built kiosk through the gateway (needs Keycloak and the services).

## T021 — the T002 set re-run on the integrated tree

Foreground, Release for .NET. The two vitest runs and their `tsc`/`eslint`/`prettier` were started in one call and ran concurrently once
(counts are unaffected); everything else ran alone.

| Check                                                                                                                                                                                                                                                                                                                                                                                      | Baseline (T002) | Integrated tree                          |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------- | ---------------------------------------- |
| `apps/kiosk-web` vitest                                                                                                                                                                                                                                                                                                                                                                    | 39 files / 392  | **42 files / 420** (+3 files, +28 cases) |
| `apps/shared` vitest                                                                                                                                                                                                                                                                                                                                                                       | 93 files / 1398 | **94 files / 1402** (+1 file, +4 cases)  |
| kiosk-web `tsc --noEmit` / `eslint src --max-warnings 0` / `prettier --check .`                                                                                                                                                                                                                                                                                                            | 0 / 0 / 0       | 0 / 0 / 0                                |
| shared `tsc --noEmit` / `eslint src --max-warnings 0` / `prettier --check .`                                                                                                                                                                                                                                                                                                               | not recorded    | 0 / 0 / 0                                |
| `pnpm test:guards`                                                                                                                                                                                                                                                                                                                                                                         | 350 / 350       | **350 pass, 0 fail**                     |
| Gateway classes (the five), Release                                                                                                                                                                                                                                                                                                                                                        | 104 passed      | included in the 180 below                |
| Gateway + AppHost + hub-CORS classes (`GatewayHostCharacterisationTests`, `GatewayUiProxyTests`, `GatewayApiPrefixTests`, `GatewayNavigationRuleTests`, `GatewayUiStaticTests`, `GatewayKioskUi*`, `GatewayEdgeHeaderTransformTests`, `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostWebAppHostingTests`, `AppHostCompositionCharacterisationTests`, `LayoutHubCorsTests`) |                 | **180 passed, 0 failed, 0 skipped**      |

`git diff --stat origin/develop -- apps/kiosk-web/src apps/shared/src` lists four new test files (`authBase`, `kioskBase`, `signinPaths`, `fontPreload`), two new
sources (`kioskBase.ts`, `signinPaths.ts`) and three edited sources (`auth.ts`, `router.tsx` for the basename, `fontPreload.ts`). No pre-existing
test file appears in the diff; `auth.test.ts` and `wallMode.test.ts` are untouched.

Remaining T021 rows, observed:

| Check                                                                                                                                                                | Result                                                                                                                                                                                                                    |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `tests/Architecture.Tests` Release                                                                                                                                   | **3254 passed, 0 failed** (baseline 3254); `git diff origin/develop -- tests/Architecture.Tests` empty, so `BoundaryTests.cs` has no diff                                                                                 |
| `dotnet format --verify-no-changes`: `src/ApiGateway`, `src/AppHost`, `src/LayoutComposition/Api`, `src/LayoutComposition/Infrastructure`, `tests/Integration.Tests` | exit 0 each                                                                                                                                                                                                               |
| `pnpm lint:e2e` / `pnpm typecheck:e2e`                                                                                                                               | exit 0 / exit 0                                                                                                                                                                                                           |
| Shard filters                                                                                                                                                        | `shard-2.filter` is the only filter changed and gains the four new classes (`LayoutHubCorsTests`, `GatewayKioskUiProxyTests`, `AppHostKioskOneOriginTests`, `GatewayKioskUiStaticTests`)                                  |
| Release build                                                                                                                                                        | `Integration.Tests` (which builds AppHost, ApiGateway and LayoutComposition) builds in Release; AppHost reports advisory S107 (6 parameters) on `WebApplications.cs`, a warning carved out of Release's errors (ADR-0084) |

**Not run:** a whole-solution `dotnet build -c Release`, and the shard-coverage emulation script (the filters were inspected by diff, not
unioned). The `kiosk`/`wall` Playwright projects and the three `layout-composition` push integration classes are CI's (T022).

## T024 — docs

`docs/deployment-frontend-env.md`: the dev paragraph no longer says `VITE_LAYOUT_HUB_URL` stays unset (the AppHost sets it on both
kiosk instances); the one-origin section no longer says the kiosk and wall are not served; the static-root list names all five roots; a
new section covers `/kiosk/` and `/wall/`, the base from `VITE_KIOSK_MODE`, the fixed ports redirecting, `LayoutHub:Cors:AllowedOrigins`
(exact origins, no wildcard, production lists its own) and the four gateway keys. `specs/491-.../contracts/gateway-paths.md`: the
reserved row now points to `contracts/kiosk-paths.md`, and the "not in this contract" line no longer lists `/kiosk/`. `README.md`: the
walkthrough's way in to the kiosk is `http://localhost:5180/kiosk/` (`:5174` redirects). `prettier --check` is clean on the docs file
(it was not clean before: prettier also realigned the existing variable table). Searched `*.md` outside specs, ADRs and `.specify` for
`5174`/`5175`: only `README.md` and `.claude/agents/frontend-engineer.md` (an agent brief naming the dev port, still true), left as is.

## T025 — §IV evidence

`git diff --stat origin/develop` (37 files) lists **no** change to `LayoutLifecycleHub.cs`, the broadcaster or their tests,
`apps/shared/src/realtime/`, any `package.json`, or `pnpm-lock.yaml` (`git diff origin/develop -- pnpm-lock.yaml | grep -c signalr` = 0). The one file
under `LayoutComposition/Infrastructure/Broadcasting/` is the **new** `LayoutHubCors.cs` (+59); `Api/Program.cs` gains two lines and
the `MapHub` line is untouched. So `@microsoft/signalr` and the hub's send path are byte-identical.

Leg affected: **event → overlay state** (§IV, ≤ 200 ms). No leg gains a hop: in dev the hub moves from browser → Vite proxy →
`layout-composition` to browser → `layout-composition` (one hop fewer); production already went direct. CORS acts at connection
set-up only: one preflight at `negotiate` (answered by `LayoutHubCorsTests`, which asserts `Access-Control-Max-Age: 600`, so a
reconnect within ten minutes reuses it) and again only at a reconnect past that; none per message **over WebSockets and
Server-Sent Events**, because those carry no per-message CORS check. **Correction (QA):** on the long-polling fallback every
poll URL carries a timestamp and the non-safelisted `X-SignalR-User-Agent` header, so each poll pays its own preflight: one
extra round trip on the event → overlay leg per message delivered over long polling (the `Max-Age` cache is keyed on the URL).
Recorded in follow-up issue #3265. SFU → decode is unchanged. **Not observed:** the preflight count on a live connection and the push
integration tests (T023/T026 and T022).

## QA fixes (backend)

Branch `494-kiosk-wall-single-origin`, on top of `cac261d3`. One `dotnet` process at a time; no Aspire, no Docker.

**Guards, recorded green first (item 10, item 11).** `GatewayKioskUiProxyTests` before touching it: the class passed. After adding the
hot-reload socket deadline (15 s `CancellationTokenSource` on connect/send/receive), the assertion that the destination received
`?token=abc`, and the new `Gateway_keeps_the_query_when_it_redirects_the_bare_prefix` (`/kiosk?x=1` to `/kiosk/?x=1`, `/wall?x=1` to
`/wall/?x=1`): `Passed! 15, Failed 0`. Both are guards for behaviour that already held, so they arrive green by design.

**Item 3: the real host cannot be started in-process.** `LayoutComposition.Api` is not referenced by `Integration.Tests`, has no
`WebApplicationFactory` anywhere in the repository, and its `Program.cs` needs Postgres and RabbitMQ to start (`AddLayoutCompositionInfrastructure`,
Wolverine). `ComposedHost` (Architecture.Tests) captures the host only up to `Build()`, before any middleware is registered, so it
cannot see the pipeline either. The strongest feasible check is
`The_layout_composition_host_answers_the_hub_preflight_before_authentication_and_authorization`, which reads `Program.cs` and requires
`app.UseLayoutHubCors()` before both `UseAuthentication()` and `UseAuthorization()` and `builder.AddLayoutHubCors()` to be present. The
behavioural half (a token-less preflight gets 204 ahead of a denying authorization) was already in the test host of the same class.
Counterfactual: `UseLayoutHubCors()` moved below `UseAuthorization()` in `Program.cs`:

```
Failed ...LayoutHubCorsTests.The_layout_composition_host_answers_the_hub_preflight_before_authentication_and_authorization
   Shouldly.ShouldAssertException : authentication
    should be greater than
```

`Program.cs` restored byte-for-byte afterwards (`git status` clean for it). The first attempt of this counterfactual passed vacuously
because the test project did not compile (missing `using System.IO`), so `--no-build` ran the stale assembly; the 7-versus-8 test count
exposed it. The run above is on a build that compiled.

**Item 4.** `AppHostKioskOneOriginTests` now builds the hub format from `"{0}" + LayoutLifecycleHub.Path` and the origin-key prefix from
`LayoutHubCors.AllowedOriginsKey` (`:` replaced with `__`, plus the trailing `__`); the assertions mean what they did. `LayoutLifecycleHub.Path`
already existed, so nothing in the hub class changed. 12 passed across the two classes.

**Item 5.** The disallowed-origin test and the no-LayoutHub-section test now assert `204 NoContent`; the disallowed test also asserts
`Access-Control-Allow-Credentials` is absent. Counterfactual: `UseLayoutHubCors` body stubbed to `branch => { }` (no CORS at all):

```
Failed ...LayoutHubCorsTests.Without_a_layout_hub_section_it_starts_and_answers_no_cors_headers
   Shouldly.ShouldAssertException : response.StatusCode should be ... but was ...
Failed ...LayoutHubCorsTests.A_preflight_from_an_origin_that_is_not_configured_gets_no_allow_origin
   Shouldly.ShouldAssertException : response.StatusCode should be ... but was ...
Failed ...LayoutHubCorsTests.A_token_less_preflight_from_an_allowed_origin_is_answered_with_the_policy
Failed ...LayoutHubCorsTests.An_actual_request_to_the_hub_path_from_an_allowed_origin_carries_the_echoed_origin_and_credentials
Failed!  - Failed: 4, Passed: 4, Total: 8
```

`LayoutHubCors.cs` restored from a copy afterwards. Before the change both tests asserted only the absent allow-origin header, which a
host with no CORS at all also satisfies.

**Item 6: origin validation, red first (ADR-0139).** New `An_origin_that_is_not_an_absolute_http_origin_refuses_to_start_naming_the_key`
(8 cases) and the guard `An_absolute_http_or_https_origin_starts` (2 cases). Observed red against the unchanged `LayoutHubCors.cs`, commit
`37e0d138`:

```
Failed ...An_origin_that_is_not_an_absolute_http_origin_refuses_to_start_naming_the_key(origin: "")
   Shouldly.ShouldAssertException : Task `async () => { await using var host = await CorsHost.StartAsync(origin); }` should throw
 ... the same failure for "null", "fab.example", "ftp://fab.example", "http://fab.example/", "http://fab.example/hub",
     "http://fab.example?x=1", "http://user@fab.example"
Failed!  - Failed:     8, Passed:    10, Skipped:     0, Total:    18
```

After the fix: `Passed! 18, Failed 0`. The refusal names `LayoutHub:Cors:AllowedOrigins` and quotes the offending value (the wildcard refusal
now quotes it too). The test also asserts the value is in the message, which keeps it from being byte-identical to the wildcard theory
(S4144 failed the first build for exactly that).

**Item 2 and item 7 (comments).** `LayoutHubCors.cs` now says a preflight is cached per origin and URL, that WebSockets and SSE ask once per
connection, and that long polling cannot use the cache because every poll URL carries a timestamp. The T025 paragraph above carries the same
correction (extra round trip per message on the long-polling fallback, follow-up #3265). In `src/AppHost/WebApplications.cs` the stale
"until their own phase" sentence is deleted and `VITE_LAYOUT_HUB_ORIGIN` is described as the fallback for a Vite server started outside
Aspire, since `hubUrl.ts` prefers `VITE_LAYOUT_HUB_URL`. No spec, task or issue number was added to source.

**Gates.** Release build with analyzers as errors: clean (the only new analyzer hit, S4144, was fixed before the green run).
`dotnet format --verify-no-changes` on `LayoutComposition.Infrastructure`, `AppHost` and `Integration.Tests`: clean. `Integration.Tests`, the
seven gateway classes in the shard filter (host characterisation, UI proxy, API prefix, navigation rule, UI static, kiosk proxy, kiosk static),
the three AppHost classes and `LayoutHubCorsTests`: `Passed! 187, Failed 0`. `Architecture.Tests`: `Passed! 3254, Failed 0`
(`BoundaryTests` included, no diff). No shard-filter entry needed: no new class. **Not run:** anything on the Aspire fixture (Docker), and a
live long-polling connection to count preflights.

## T027 QA: findings and dispositions (owner-approved 2026-11-13)

Three read-only reviews (backend, frontend, security) of `origin/develop...HEAD`. No blocker and no exploitable issue above 80% confidence.
Red evidence for the fixes is in the two QA sections and in `verification-qa-frontend.md`.

| Finding                                                                                           | Disposition                                                                                                                                                                            |
| ------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Backend 1: the AppHost commit left `AppHostCompositionCharacterisationTests` red                  | Fixed by moving the api-gateway snapshot lines into the gateway commit (history rewritten on this unmerged branch; tree identical to before). The class passes at that commit, 5 of 5. |
| Backend 2: "one preflight per connection" is false for long polling                               | Comment and the T025 evidence corrected to WebSockets and SSE; the long-polling cost is filed as #3265 for a decision.                                                                 |
| Backend 3: no test pins the real `Program.cs` order                                               | The real host cannot start in-process (no `WebApplicationFactory`, needs Postgres and RabbitMQ); a source-order test reads `Program.cs`, proven by counterfactual.                     |
| Backend 4, 5: AppHost test copied two literals; negative CORS tests passed against the empty stub | Derived from `LayoutLifecycleHub.Path` and `LayoutHubCors.AllowedOriginsKey`; `NoContent` and credentials-absent asserted, proven by counterfactual.                                   |
| Backend 6: origin entries only checked for `*`                                                    | Red first (8 cases), then refused at startup: not an absolute http(s) origin, a path, query, user info, a trailing slash, `null`.                                                      |
| Backend 7, 10, 11                                                                                 | Stale comments fixed; hot-reload test bounded and the forwarded query asserted; query carry-over of the redirect pinned (guards, green first).                                         |
| Backend 8, 9, HEAD on `/kiosk` in static mode                                                     | Accepted: advisory analyzer warnings (S138, S107), middleware placed beside the hub as planned, harmless because Vite's base is absolute.                                              |
| Frontend 1: `returnPath` passed dot segments                                                      | Red first (3 cases), fixed by resolving against the origin before the prefix check.                                                                                                    |
| Frontend 2, `authBase` never failed                                                               | Router basename test red by removing the option; `authBase` counterfactual recorded.                                                                                                   |
| Frontend 3: the router does not follow the URL restored after sign-in (predates this branch)      | To be observed at T026 and then filed separately; not fixed here.                                                                                                                      |
| Frontend nits                                                                                     | `'/'` row added, old comment trimmed, docs sentence added; accepted: load-time guard for a `--base` override, module name, the font test's expression.                                 |
| Security: shared origin with the console lets a script bug in the kiosk obtain a console token    | Not a defect of this diff (ADR-0192 chose one origin); recorded on #3252 as a prerequisite for production use of `/kiosk/` and `/wall/`. No exploitable issue found.                   |

Final gates on the integrated tip: kiosk-web 43 files / 425 tests; shared 94 / 1402; tsc, eslint and prettier clean; `pnpm test:guards`
350 of 350; gateway, AppHost and hub CORS classes 177 of 177; `Architecture.Tests` 3254 of 3254, `BoundaryTests` unedited; `dotnet format` clean.
