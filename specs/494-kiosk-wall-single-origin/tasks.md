# Tasks: The kiosk and the wall display on one origin

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md) | **Contract**:
[contracts/kiosk-paths.md](./contracts/kiosk-paths.md) | **Issue**: #3246 (epic #3248, ADR-0192), on Project #13 (Todo; checked
2026-11-12 by content URL with `--limit 2000`). No per-task issues.

**Order with spec 493: 493 merges first.** No task here starts until it has, and T001 re-checks the plan's rebase table against 493's
**merged** diff, not its spec.

**Phase 4a colour: mixed, declared per task** (ADR-0139). Red = a test written first, run, **observed failing**, its failure quoted in the
PR, then made green. Characterisation = captured green **before** the change and passing with its assertions unmodified after. A test that
arrives green where red is declared is a phase-4 failure: stop and report, or prove it by counterfactual as spec 491 did (and say so).

**Never touched**: `@microsoft/signalr`; the hub class `LayoutLifecycleHub`, its broadcaster and their tests; the `MapHub` line in
`src/LayoutComposition/Api/Program.cs`; Keycloak and the realm file; WebRTC/WHEP; `BoundaryTests.cs` (if a requirement cannot be met
without editing it, stop and amend ADR-0192). Never `git stash` or `git checkout -- .`. One vitest at a time. One Aspire stack per machine;
stop it before building. No `Co-Authored-By`. Every new `Integration.Tests` class carries `[Trait("Category", "FixtureLogic")]` (or the
Aspire collection) and an entry in `tests/Integration.Tests/ci-shards/shard-2.filter`. Release build before pushing (analyzers are errors).

**§IV: no leg gains a hop.** Event → overlay: the hub moves from browser → Vite proxy → `layout-composition` to browser →
`layout-composition`; CORS adds a preflight at connection set-up only (T032). SFU → decode: unchanged.

Format: `[ID] [P?] [Story] Description`. `[P]` = disjoint files from every other `[P]` task in the same phase, can run in parallel.
Stories: US1 kiosk on `/kiosk/`, US2 wall on `/wall/`, US3 fixed ports and suites, US4 hot reload, US5 built bundles.

## Phase 1 — Setup and baseline (blocks everything)

- [x] **T001** [Setup] Rebase onto `origin/develop` **after 493 has merged**. Diff 493's merged change (`git log`/`git diff` over its
      commits) against plan.md's "What 494 rebases over" table: `MapShellFallback`, the static-file options, `StaticUiStack`/`UiProxyStack`,
      `GatewayHost.cs`, `devProxy.ts`, shard filters. Record in `verification.md` every row that holds and every difference; if the fallback
      rule differs from 493's spec, plan D5 follows the merged code and the difference is written down before T003.
- [x] **T002** [Setup] Baseline on the rebased branch, lockfile-true install (`pnpm install --frozen-lockfile`), one vitest at a time:
      `apps/kiosk-web` (expect 39 files; record files and tests), `apps/shared`; `dotnet test tests/Architecture.Tests`; the in-process
      gateway suites (`Category=FixtureLogic`, `ApiGateway` namespace: 491's and 493's classes). Record counts in `verification.md`. Note the
      CI run of `develop`'s `kiosk`/`wall` Playwright projects and the `layout-composition` push integration tests
      (`OverlayPushIntegrationTests`, `SignalRRevocationIntegrationTests`, `ReconnectReconcileIntegrationTests`) as the green baseline.
      `git status` clean.

## Phase 2 — Foundational (blocks the gateway tasks)

- [x] **T003** (backend, **characterisation**) In `src/ApiGateway/UiHosting.cs`, extract 493's merged shell fallback into one helper that
      takes a route prefix and an `index.html` path; the shell calls it exactly as before. No behaviour change. 493's tests and 491's
      `GatewayUiStaticTests` pass **unmodified**, captured before and after. Commit alone. Depends on T001, T002.
- [x] **T004** (test, **characterisation, fixture-only, owner-approved**) Spec 491's gateway fixtures supply the four new required settings:
      `tests/Integration.Tests/ApiGateway/UiProxyStack.cs` adds `Ui:Proxy:Kiosk` and `Ui:Proxy:Wall` (two more stub destinations);
      the `StaticUiStack` nested in `GatewayUiStaticTests.cs` adds `Ui:Static:KioskRoot` and `Ui:Static:WallRoot` (temp directories each with
      an `index.html`), beside whatever 493 added. **No assertion, no test method, no `[InlineData]` row changes.** The suites stay green (the
      gateway ignores the keys until T006/T008). Commit alone, message saying it is a fixture edit approved by the owner (spec Q4). Depends
      on T003.

## Phase 3 — US1 (P1): a kiosk screen runs from `/kiosk/`

**Goal**: sign-in, layouts, reloads and the hub all work at `:5180/kiosk/`, with the hub off the gateway. **Independent test**: quickstart
"Dev" steps 1–5.

### Hub CORS on `layout-composition` (backend; independent of the gateway, can start right after T002)

- [x] **T005** [P] [US1] (test, **red**) New `tests/Integration.Tests/LayoutComposition/LayoutHubCorsTests.cs` (FixtureLogic, shard-2): an
      in-process `WebApplication` on a loopback port that calls `AddLayoutHubCors` / `UseLayoutHubCors` and maps stand-in endpoints at
      `LayoutLifecycleHub.Path + "/negotiate"` (behind `RequireAuthorization`) and at one REST path. With
      `LayoutHub:Cors:AllowedOrigins:0 = http://localhost:5180`:
      (a) a **token-less** `OPTIONS …/negotiate` from that origin requesting `POST` and
      `authorization,content-type,x-requested-with,x-signalr-user-agent` → 204 with `Access-Control-Allow-Origin: http://localhost:5180`,
      `Access-Control-Allow-Credentials: true`, the four headers, `GET,POST,DELETE` allowed and `Access-Control-Max-Age: 600`;
      (b) the same from `http://fab.example` → no `Access-Control-Allow-Origin`;
      (c) the same against the REST path → no CORS headers;
      (d) an actual `GET` on the hub path from the allowed origin carries the echoed origin and credentials header;
      (e) `AllowedOrigins:0 = *` (and `https://*.fab.example`) → start refused, message naming `LayoutHub:Cors:AllowedOrigins`;
      (f) no `LayoutHub` section → starts, no CORS headers (today's behaviour).
      Fails to compile first (the type does not exist): record that, then add an empty stub so the run shows **assertion** failures; quote
      them.
- [x] **T006** [US1] (backend) New `src/LayoutComposition/Infrastructure/Broadcasting/LayoutHubCors.cs`: `AddLayoutHubCors(this
WebApplicationBuilder)` (reads the array, refuses any entry containing `*` with an `InvalidOperationException` naming the key, registers
      the named policy: `WithOrigins`, `AllowCredentials`, the four headers, the three methods, `SetPreflightMaxAge(10 min)`) and
      `UseLayoutHubCors(this WebApplication)` (`UseWhen` on `StartsWithSegments(LayoutLifecycleHub.Path)` → `UseCors(policy)`). Guards with
      `Ensure.That`. In `src/LayoutComposition/Api/Program.cs`: `builder.AddLayoutHubCors()` beside the other `Add…` calls and
      `app.UseLayoutHubCors()` **before** `app.UseAuthentication()`; the `MapHub` line untouched. T005 green; Release build; `dotnet format`.
      `Architecture.Tests` green, including `EndpointScopeDeclarationTests` (its `MapHub` register row still matches) and
      `ServedHealthHostCompletenessTests`. Depends on T005.

### Kiosk application (frontend; disjoint from the backend and gateway tracks)

- [x] **T007** [P] [US1] (test, **red**) New `apps/kiosk-web/src/app/kioskBase.test.ts`: `kioskBase(undefined)`, `kioskBase('')`,
      `kioskBase('kiosk')` → `/kiosk/`; `kioskBase('wall')`, `kioskBase('WALL')` → `/wall/`. Fails (module missing → then a stub returning
      `/`); quote. Depends on T002.
- [x] **T008** [US1] (frontend) New `apps/kiosk-web/src/app/kioskBase.ts` (pure, named export); `apps/kiosk-web/vite.config.ts` sets
      `base: kioskBase(mode flag)` from `process.env` merged with `loadEnv(mode, cwd, 'VITE_')`, **except under Vitest** (`process.env.VITEST`),
      where the base stays `/`. `/hubs` proxy, `PORT`, `strictPort` unchanged. T007 green; the kiosk-web suite count equals T002 plus T007's
      cases. `tsc --noEmit`, `eslint`, `prettier`. Depends on T007.
- [x] **T009** [P] [US1] (test, **red**) New `apps/kiosk-web/src/app/signinPaths.test.ts` for two pure functions taking the base:
      `redirectUri(origin, base)` → `http://localhost:5180/kiosk/oidc/callback` (and `/wall/` likewise); `returnPath(state, base)` →
      the stored path when it starts with the base (`/kiosk/layouts/abc`, with a query), otherwise the base for `undefined`, a non-string,
      `''`, `/`, `//evil.example/x`, `/wall/x` under base `/kiosk/`, `/kioskx/y`, `https://evil.example/kiosk/`. Plus
      `routerBasename('/kiosk/')` → `/kiosk`. Quote the failures. Depends on T002.
- [x] **T010** [US1] (frontend) New `apps/kiosk-web/src/app/signinPaths.ts`; `auth.ts` uses `redirectUri` over
      `window.location.origin` and `import.meta.env.BASE_URL` (the SSR fallback becomes base-aware too) and `returnPath(user?.state?.returnTo, BASE_URL)` in
      `onSigninCallback`; `router.tsx` passes `{ basename: routerBasename(import.meta.env.BASE_URL) }`. Scope, client, storage, silent renew
      unchanged; `auth.test.ts` and `wallMode.test.ts` pass **unmodified**. T009 green. Depends on T008, T009.
- [x] **T011** [P] [US1] (test, **red** for `/kiosk/`, **characterisation** for `/`) New test beside
      `apps/shared/src/ui/fonts/fontPreload.ts`: in `serve` with base `/kiosk/` every injected preload `href` starts with `/kiosk/@fs/`; with
      base `/` the tags are byte-identical to today's (capture the current output first). Quote the red. Depends on T002.
- [x] **T012** [US1] (frontend) `fontPreload.ts` prefixes `config.base` in `serve`; build output unchanged. T011 green; `apps/shared` suite
      count equals T002 plus T011's cases. Depends on T011.

### Gateway, proxy mode (the routes US1, US2 and US4 all need)

- [x] **T013** [US1] (test, **red**) New `tests/Integration.Tests/ApiGateway/GatewayKioskUiProxyTests.cs` (FixtureLogic, shard-2), on
      `UiProxyStack` (T004): `/kiosk/`, `/kiosk/layouts/abc` and `/kiosk/src/main.tsx` reach the kiosk destination **with the path
      unchanged**; `/wall/...` reaches the wall destination; neither is answered by the shell destination; `/kiosk` and `/wall` → redirect
      to the slashed path; the kiosk and wall routes carry no `RateLimiterPolicy` and no `CorsPolicy` (requests beyond `PermitLimit` are not
      429); a WebSocket upgrade on `/kiosk/` is carried to the kiosk destination; with `Ui:Mode=Proxy` and `Ui:Proxy:Kiosk` (then
      `Ui:Proxy:Wall`) removed, start is refused naming the key. Quote the failures. Depends on T004.
- [x] **T014** [US1] (backend) `UiHosting.cs` proxy mode: two clusters from `RequiredAddress(…, "Ui:Proxy:Kiosk"/"Wall")`, routes
      `ui-kiosk` `/kiosk/{**catch-all}` and `ui-wall` `/wall/{**catch-all}` ahead of the shell, no policies; the `/kiosk` and `/wall` redirects
      (if the YARP catch-all wins over the redirect endpoint, give the redirect an explicit order or narrow the YARP match; T013 decides).
      **In the same commit**, `src/AppHost/WebApplications.cs` `WithOneOriginUi` gains the kiosk and wall instances and sets
      `Ui__Proxy__Kiosk` / `Ui__Proxy__Wall` from their endpoints (`AddKioskWeb`/`AddKioskWall` return their builders), or the run-mode
      gateway would refuse to start. T013 green; 491's and 493's gateway suites green with assertions unmodified; `BoundaryTests` green, no
      diff. Release build. Depends on T013.

### AppHost wiring for the hub (needs T006's setting name and T014's builder changes)

- [x] **T015** [US1] (test, **red**) New `tests/Integration.Tests/AppHostKioskOneOriginTests.cs` (FixtureLogic, shard-2), over the AppHost
      model as `AppHostOneOriginTests` does: the gateway carries `Ui__Proxy__Kiosk`/`Wall` referencing the `kiosk-web`/`kiosk-wall` endpoints
      (green after T014: written here, observed red against the pre-T014 commit, or proven by counterfactual, and said which); `kiosk-web` and
      `kiosk-wall` each carry `VITE_LAYOUT_HUB_URL` = the `layout-composition` http endpoint plus `/hubs/layouts`; `layout-composition` carries
      `LayoutHub__Cors__AllowedOrigins__N` covering the `kiosk-web`, `kiosk-wall` and `api-gateway` endpoints; none is set by a literal port;
      `VITE_LAYOUT_HUB_ORIGIN` is still present (fallback kept, owner). Quote the failures. Depends on T006, T014.
- [x] **T016** [US1] (backend) `WebApplications.cs`: set `VITE_LAYOUT_HUB_URL` on both kiosk instances and the allowed origins on
      `layout-composition` (endpoint references, no `WaitFor` from `layout-composition`) inside `AddWebApplications`. T015 green. Re-generate
      the three `AppHostComposition/*.snapshot.txt` and record the diff in `verification.md` as moved by the decision (spec, "Expected to
      move"). `AppHostWebAppHostingTests` and 491's `AppHostOneOriginTests` pass **unmodified**. Depends on T015.

## Phase 4 — US2 (P1): the wall display runs from `/wall/`, still as the wall

**Goal**: `/wall/` signs in as `kiosk-wall` with `openid offline_access`. **Independent test**: quickstart "Dev" step 6.

- [x] **T017** [US2] (test, **red**) Add wall cases to the T009 file only if T009 did not already cover them (`redirectUri(…, '/wall/')`,
      `returnPath` with base `/wall/` rejecting `/kiosk/x`), and a case that the client and scope still follow the mode flag while the base
      follows `BASE_URL` (no second flag): if `wallMode.test.ts` already pins client and scope, it stays **unmodified** and is cited as the
      characterisation. Quote any red. Depends on T010.

(The wall's gateway route, hub URL and allowed origin are delivered by T013–T016; US2 adds no separate production code.)

## Phase 5 — US5 (P2): built kiosk and wall bundles from the same paths

**Goal**: static mode serves both builds. **Independent test**: quickstart "Production bundles, locally".

- [x] **T018** [US5] (test, **red**) New `tests/Integration.Tests/ApiGateway/GatewayKioskUiStaticTests.cs` (FixtureLogic, shard-2), on
      `StaticUiStack` (T004): `/kiosk/` and `/kiosk/layouts/abc` (navigation and extension-less) → the kiosk's `index.html`, `/wall/walls/x`
      → the wall's, never the shell's or each other's; each such response has `Cache-Control: no-cache` and `nosniff` (493's rule via T003's
      helper, including its dotted-path navigation case); `/kiosk/assets/x.js` (present) → the file with `nosniff`; `/kiosk/assets/nope.js` →
      404; `/kiosk` → redirect; `Ui:Static:KioskRoot` (then `WallRoot`) missing, not a directory, or without `index.html` → start refused
      naming the key; 300 page requests from one source at `PermitLimit` 100 → zero 429. Quote the failures. Depends on T004, T014.
- [x] **T019** [US5] (backend) `UiHosting.cs` static mode: `StaticRoots` gains `Kiosk` and `Wall` read by `Root(…, "index.html")`;
      `UseGatewayUiStatic` serves both at `/kiosk` and `/wall` before the limiter; `MapGatewayUi` maps the two prefixes to T003's helper with
      each root's `index.html`, and the redirects. T018 green; every gateway suite green with assertions unmodified. Release build,
      `dotnet format`. Depends on T018.
- [x] **T020** [US5] (verification) Build the kiosk twice from `apps/kiosk-web` (plain, and `VITE_KIOSK_MODE=wall`) with the production
      variables; check every asset URL in each `index.html` is under `/kiosk/` or `/wall/`; run the real gateway in static mode over the
      five builds on port 5180 and walk the quickstart requests; confirm a production build without `VITE_LAYOUT_HUB_URL`,
      `VITE_KEYCLOAK_URL` or `VITE_API_GATEWAY_URL` throws naming it (existing guards, unchanged). Record in `verification.md`; stop every
      process afterwards. Depends on T008, T012, T019.

## Phase 6 — US3 (P1) and US4 (P2): fixed ports, unchanged suites, hot reload

- [x] **T021** [US3] (**characterisation**) Re-run the T002 set: kiosk-web and shared (counts = baseline + new cases only, every
      pre-existing file unmodified: `git diff --stat origin/develop -- apps/kiosk-web/src apps/shared/src` lists only new files and the source
      files named above), `Architecture.Tests` (`BoundaryTests.cs` no diff), all FixtureLogic gateway and AppHost classes, the shard-coverage
      emulation (full list = union of the four filters). `tsc`, `eslint --max-warnings 0`, `prettier --check`, `lint:e2e`, `typecheck:e2e`,
      `dotnet build -c Release`, `dotnet format --verify-no-changes`. Depends on T006, T010, T012, T016, T017, T019.
- [ ] **T022** [US3] (**characterisation**, CI) The `kiosk` and `wall` Playwright projects, the cross-project kiosk specs and the
      `layout-composition` push integration tests pass in CI with **no spec or test edited**. They are also the full-stack evidence that the
      cross-origin hub works from `:5174`/`:5175` (live-update specs). A failure is a stop-and-report, not an edit. Depends on T021 (runs on
      the PR).
- [ ] **T023** [US4] (observation, needs the stack, phase 5) With `/kiosk/` and `/wall/` open through `:5180`, edit a kiosk component with a
      hot-reload boundary; record the `vite-hmr` frames as spec 491 T012 did. Depends on T021.

## Phase 7 — Docs, §IV evidence, Verify

- [x] **T024** [P] (docs) `docs/deployment-frontend-env.md`: the `/kiosk/` and `/wall/` paths, the base per instance from
      `VITE_KIOSK_MODE`, `VITE_LAYOUT_HUB_URL` now set in dev to `layout-composition`'s address, the `/hubs` proxy as a fallback only,
      `LayoutHub:Cors:AllowedOrigins` for the deploy layer (no wildcard; refused at start), the two required static roots. In
      `specs/491-gateway-single-origin-shell/contracts/gateway-paths.md`, the reserved `/kiosk/`, `/wall/` row points to
      `contracts/kiosk-paths.md`. Prettier. Depends on T016, T019.
- [x] **T025** (§IV evidence, plan W12) In `verification.md`: `git diff --stat origin/develop` shows no change to
      `LayoutLifecycleHub.cs`, the broadcaster or their tests, nor to `@microsoft/signalr` or `apps/shared/src/realtime/`; T005's `Max-Age`
      assertion cited; T022's push integration tests cited. State the leg (event → overlay state), the hop change (one fewer in dev, none
      added) and that CORS acts at connection set-up only. Depends on T022.
- [ ] **T026** (Verify, needs the stack, phase 5) Quickstart "Dev" steps 1–7 on one Aspire stack (none other running): sign-in at
      `/kiosk/` and `/wall/` (if the token exchange fails at `:5180`, record it verbatim and the orchestrator files the separate issue against
      spec 491 and all apps; do **not** edit the realm); live overlay; `negotiate` and the socket on `layout-composition`, **one `OPTIONS` per
      connection and none during a burst of variable changes**; WHEP on the media server; the gateway's traces show no `/hubs/` or WHEP path
      (SC-002); SC-005 zero 429 (spec 491 T024's method); SC-006 click-to-first-frame through `/kiosk/` vs `:5174`, **measured twice**;
      `:5174/` and `:5175/` redirect to their bases and sign in. Record observed and not observed separately. Depends on T021, T023.
- [ ] **T027** (QA, phase 6) `/code-review` and `/security-review` (the hub's CORS policy is a trust-boundary change); dispose of every
      finding in `verification.md`. Depends on T026.

## Dependencies and parallelism

```text
493 merged → T001 → T002 ─┬─ T005 → T006 (backend hub CORS) ─────────────────────┐
                          ├─ T007 → T008 ─┬─ T009 → T010 → T017                 │
                          │               └─ (T020 needs T008)                   │
                          ├─ T011 → T012                                         │
                          └─ T003 → T004 → T013 → T014 ─┬─ T015 → T016 ←───────┘
                                                        └─ T018 → T019 → T020
T006, T010, T012, T016, T017, T019 → T021 → T022 → T025
T021 → T023 → T026 → T027;   T016, T019 → T024
```

- **Foundational, blocking:** T001, T002 (everything); T003 → T004 (every gateway test).
- **Fan-out after T002:** the backend hub track (T005–T006), the kiosk app track (T007–T010, T017), the shared preload (T011–T012) and the
  gateway track (T003–T004 → T013–T014 → T018–T019) own disjoint files. T005, T007, T009 and T011 are `[P]` with each other.
- **Joins:** T015–T016 need T006 (setting name) and T014 (the kiosk builders). T014 and its AppHost part are **one commit**.
- **Commits that stand alone** (ADR-0087, each builds on its own): T003 (characterisation), T004 (fixture-only), every red/green pair.
