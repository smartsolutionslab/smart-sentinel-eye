# Tasks: Security headers and a Content-Security-Policy for the gateway's HTML origin

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) (the policy contract is normative there) | **Issue**: #3252 (epic #3248,
ADR-0192). Feature-level issue only; no per-task issues. Before phase 4 the orchestrator verifies #3252 is on Project #13 by content URL with
`--limit 2000`.

**Lane**: autonomous (ADR-0144). **Engineer**: infra-engineer for every implementation task; test-writer for every test task. **Phase 4a
colour: red** for all new behaviour (FR-001–FR-010); **characterisation** for the existing suites, with one declared fixture-only edit
(T003). A red test that arrives green is a phase-4 failure. **Security-sensitive**: phase 6 runs `/security-review` as well as
`/code-review`.

**Never touched**: `BoundaryTests.cs`; `@microsoft/signalr`; the hub, Keycloak realm and WHEP code; `apps/*/src` (if T001 finds an app must
change, stop and add a frontend task rather than editing it here); any existing assertion, test method or `[InlineData]` row. One Aspire
stack per machine, stopped before building. Every new `Integration.Tests` class carries `[Trait("Category", "FixtureLogic")]` and an entry in
`tests/Integration.Tests/ci-shards/shard-2.filter`. Release build before pushing (analyzers are errors). No `Co-Authored-By`.

Format: `[ID] [P?] [Story] Description`. `[P]` = disjoint files from every other `[P]` task in the same phase.
Stories: US1 headers on every page, US2 missing origin fails a test, US3 e2e under the policy.

## Phase 1 — Setup (blocks everything)

- [ ] **T001** [Setup] (infra-engineer, **observation**) Prove the plan's browser and bundle premises before code depends on them; record
      each as observed in `specs/499-gateway-security-headers/verification.md`:
      (a) `pnpm --filter ./apps/management-web build`, `--filter ./apps/kiosk-web build`, both remotes: grep each `dist/index.html` for a
      `<script>` without `src` (premise P7; expect none). Delete the `dist` folders afterwards only if they were absent before.
      (b) Boot the dev stack; in Chromium at `:5180/`, `/cameras`, `/kiosk/`, `/wall/`, served with the dev policy of plan.md by a local,
      uncommitted gateway patch (stashed by name and dropped afterwards is not allowed: keep it in a separate worktree or discard by hand);
      observe: HMR socket connects
      under `'self'` (P9), no violation from Vite/MF inline scripts with `'unsafe-inline'` (P8), Radix dialog opens (P6), a still captures
      (P10), sign-in and silent renew run without framing Keycloak (P11).
      (c) Confirm YARP-proxied HTML has `Content-Type` set when `OnStarting` fires.
      Any premise that fails changes the plan (record the revision) before T004. Depends on nothing.
- [x] **T002** [Setup] (test-writer) Baseline, green: `dotnet test tests/Integration.Tests --filter "Category=FixtureLogic&FullyQualifiedName~ApiGateway"`,
      the `AppHost*OneOrigin*` and `AppHostKioskOneOriginTests` classes, `tests/Architecture.Tests`. Record counts in `verification.md`. Note
      the latest green CI e2e run on `develop` as the e2e baseline.

## Phase 2 — Foundational (blocks US1 and US2)

- [x] **T003** (test-writer, **characterisation, fixture-only**) Add `Ui:ContentSecurityPolicy:IdentityProvider` = `http://identity.test`,
      `:LayoutHub` = `http://hub.test:5000`, `:Media` = `http://media.test:8889` to the overrides in
      `tests/Integration.Tests/ApiGateway/UiProxyStack.cs` and in the `StaticUiStack` nested in
      `tests/Integration.Tests/ApiGateway/GatewayUiStaticTests.cs`. **No assertion, test method or `[InlineData]` changes.** Suites stay green
      (the gateway ignores the keys until T006). Commit alone; message says it is a fixture-only edit declared in spec A3. Depends on T002.

## Phase 3 — US1 (P1): every page carries the policy

**Independent test**: spec US1.

- [x] **T004** [P] [US1] (test-writer, **red**) New `tests/Integration.Tests/ApiGateway/GatewaySecurityHeadersStaticTests.cs`, using
      `GatewayUiStaticTests.StaticUiStack`: for `/`, `/layouts/123`, `/kiosk/`, `/wall/`, `/remotes/cameras/index.html` (add the file through
      the stack's existing file parameters if needed, fixture code in the new file only) assert the exact production policy string of
      plan.md with the three test origins and `ws://hub.test:5000`, and the four companions; assert `HEAD /` carries them; assert
      `/assets/app.js` and `/api/<ctx>/x` carry **no** `Content-Security-Policy` and no `Strict-Transport-Security` anywhere. Theory rows for
      start failure: each of the three keys absent, empty, `relative/path`, `ftp://x`, `http://a.test;script-src *`, `http://a.test 'x'` →
      `InvalidOperationException` whose message names the key. A row with a path (`http://hub.test:5000/hubs/layouts/`) reduces to the
      origin. Observe red (no header today; no validation today). Quote the failure. Depends on T003.
- [x] **T005** [P] [US1] (test-writer, **red**) New `tests/Integration.Tests/ApiGateway/GatewaySecurityHeadersProxyTests.cs`, with its
      own small HTML stub destination defined in the file (answers `text/html` and sets an upstream
      `Content-Security-Policy: default-src *`), started behind a gateway in `Ui:Mode=Proxy` using `UiProxyStack`'s configuration shape: the
      shell page's response carries the dev policy (`script-src 'self' 'unsafe-inline'`), **replacing** the upstream one (exactly one header
      value); a JSON response through `/api/...` from the existing `StubDestination` carries none; with `Ui:Mode` unset (plain
      `GatewayTestHost`) no header of this feature appears on any response. Observe red. Depends on T003.
- [x] **T006** [US1] (infra-engineer) New `src/ApiGateway/ContentSecurityPolicy.cs` (origins record with `Read(IConfiguration)`
      validation per plan D3/D4; `For(mode, origins)` header builder per the contract) and `src/ApiGateway/SecurityHeaders.cs`
      (`UseGatewaySecurityHeaders`, plan D1). Edit `UiHosting.cs`: validate origins in `AddGatewayUi` for both modes; edit `GatewayHost.cs`:
      call `app.UseGatewaySecurityHeaders()` before `app.UseGatewayUiStatic()`. `Ensure.That` for argument guards; no new suppression.
      T004 and T005 green; T002's suites green unmodified; `BoundaryTests` green. Depends on T001, T004, T005.

## Phase 4 — US2 (P1): a missing origin fails a test

**Independent test**: spec US2 (counterfactuals).

- [x] **T007** [P] [US2] (test-writer, **red**) New `tests/Integration.Tests/AppHostContentSecurityPolicyTests.cs` per plan D7 (helpers
      copied from `AppHostKioskOneOriginTests`, not shared): (1) every `(resource, endpoint)` referenced from a `VITE_*` entry of any
      `JavaScriptAppResource`, minus `api-gateway`, plus `stream-distribution`'s `services__mediamtx__whep__0`, appears among the gateway's
      `Ui__ContentSecurityPolicy__*` endpoint references; failure message lists the missing pairs; (2) the three keys map to
      `keycloak/http`, `layout-composition/http`, `mediamtx/whep` respectively. Observe red (no such keys today). Depends on T002 (can run in
      parallel with Phase 3).
- [x] **T008** [US2] (infra-engineer) `src/AppHost/WebApplications.cs`: `AddWebApplications` takes the `mediamtx` container resource;
      `WithOneOriginUi` adds the three `Ui__ContentSecurityPolicy__*` environment entries from endpoint references (plan D6).
      `src/AppHost/AppHost.cs`: pass `mediamtx` at the call site. T007 green; the existing AppHost model tests green unmodified. Depends on
      T006, T007.
- [x] **T009** [US2] (test-writer, **counterfactual proof**) Record in `verification.md`, verbatim: with T008's `__Media` line removed
      locally, T007 fails naming `mediamtx/whep`; with a dummy `.WithEnvironment("VITE_PROBE", mediamtx.GetEndpoint("api"))` added to
      `kiosk-web`, T007 fails naming `mediamtx/api`. Revert both; do not commit them. Depends on T008.
- [x] **T010** [P] [US2] Add `AppHostContentSecurityPolicyTests`, `GatewaySecurityHeadersStaticTests` and
      `GatewaySecurityHeadersProxyTests` to `tests/Integration.Tests/ci-shards/shard-2.filter`. Depends on T004, T005, T007.

## Phase 5 — US3 (P2): the e2e suite under the policy

**Independent test**: spec US3.

- [ ] **T011** [US3] (test-writer, **red**) New `e2e/content-security-policy.spec.ts` (plan D8), in the `chromium` project, using the
      existing sign-in helpers and `e2e/support/origins.ts`; an init script records `securitypolicyviolation` events
      (`violatedDirective`, `blockedURI`); for the console root, `/cameras`, `/kiosk/` on the seeded live-video layout until a decoded frame
      and one hub push, and `/wall/`: assert the document response's `content-security-policy` header is present and the violation list is
      empty. Run against a stack **without** T006/T008 and observe red on the missing header. Quote it. Check `playwright.config.ts`'s
      `testMatch`/`testIgnore` anchors place it in the intended project (spec 497 P9). Depends on T008.
- [ ] **T012** [US3] (infra-engineer) Run the full e2e suite on the dev stack with T006/T008 in place. Green with no spec edited; record the
      run in `verification.md`. Any red spec caused by the policy follows plan.md's risk table (route the page-context call through
      Playwright's `request`, never widen the policy); if that would need an assertion edit, block. Depends on T011.

## Phase 6 — Polish

- [x] **T013** [P] (infra-engineer) `docs/deployment-frontend-env.md`: a "Gateway security headers" subsection under "One origin" listing
      `Ui__ContentSecurityPolicy__IdentityProvider`/`__LayoutHub`/`__Media`, that each must equal the origin of `VITE_KEYCLOAK_URL`,
      `VITE_LAYOUT_HUB_URL` and the WHEP base respectively, that the gateway refuses to start without them, and that HSTS waits for TLS.
      Depends on T006.
- [ ] **T014** [P] (orchestrator) File a follow-up issue on Project #13, linked from #3252 and epic #3248: "Separate origin for the kiosk and
      the wall display (needs an ADR amending ADR-0192 decision 1)", quoting the reviewer's comment and spec §"What a CSP does and does not
      fix". Do not label it `agent:ready` (the lane may not write ADRs). Depends on nothing.
- [ ] **T015** Phase 5 `/verify`: at `:5180`, read the headers on `/`, `/kiosk/`, `/wall/`, an asset and an `/api` call; confirm HMR still
      reloads an edited component; record in `verification.md` with the PR's counterfactuals and the known gap (spec A1). Depends on T012,
      T013.

## Dependencies and parallelism

```text
T001 ─┐
T002 ─┴─ T003 ─┬─ T004 [P] ─┐
               ├─ T005 [P] ─┼─ T006 ─┬─ T008 ─ T009
T002 ──────────┴─ T007 [P] ─┘        │     └─ T011 ─ T012 ─ T015
                                     └─ T013 [P]
T010 [P] after T004, T005, T007        T014 [P] any time
```

**Foundational blockers**: T001 (premises) and T003 (fixture keys) block every gateway test; T006 blocks the AppHost wiring because a
gateway given the keys must accept them. **Fan-out**: T004, T005 and T007 own disjoint new files and can be written in parallel by
test-writer; T013 and T014 are independent of the code path. All implementation (T006, T008) touches the gateway and AppHost and is
sequential for one infra-engineer.
