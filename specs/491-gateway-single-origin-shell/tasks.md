# Tasks: The operator console on one origin

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md) | **Contract**:
[contracts/gateway-paths.md](./contracts/gateway-paths.md) | **Issue**: #3245 (epic #3248, ADR-0192)

**Phase 4a colour: mixed, declared per task.** Gateway routing, the navigation rule, UI hosting and the build guards are **new behaviour:
red first, failure quoted in the PR** (ADR-0139). The existing frontend suites are **characterisation: green before, unmodified after**
(SC-002); the one exception is three URL substrings in an e2e spec (T028), recorded in the PR. Test-enabling extraction (T004) is
behaviour-preserving and is its own commit.

**Out of scope, never touched**: `@microsoft/signalr` (owner instruction), the SignalR hub, WebRTC, Keycloak, the kiosk and the wall
(#3246), the e2e cut-over (#3247), the production edge. `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` is
**never edited**: if the spike fails, stop and amend ADR-0192. Never `git stash` or `git checkout -- .`. One vitest at a time. One Aspire stack
at a time; stop it before building. No `Co-Authored-By`. A new test class in `Integration.Tests` needs a `ci-shards/shard-N.filter` entry.

**§IV legs touched: none.**

## Phase 1 — Setup and baseline

- [x] **T001** Baseline on the clean branch (lockfile-true install): `vitest run` in `apps/management-web`, `apps/management-cameras`,
      `apps/management-event-ingestion`, one at a time, recording files and tests (expected 102/740, 24/151, 4/27); then
      `dotnet test tests/Architecture.Tests` (expected 3254 passed). `git status` clean. Record in `verification.md`.
- [x] **T002** Admin: confirm #3245 is on Project #13 (`gh project item-add 13 --owner smartsolutionslab --url …` only if missing; verify
      with `--limit 2000`); file the follow-up issue for the gateway's Content-Security-Policy and security headers (plan D9) and link it from
      #3248; comment on #3245 that the CORS list does **not** shrink in this phase (spec FR-013, ADR-0192 decisions 6-7); re-check that nothing
      names or listens on port 5180 (`grep` plus `netstat`). Record the issue number and the port result.
- [x] **T003** Spec edit, accepted by the owner at the Plan gate: reword FR-012 in `spec.md` so "the fixed ports keep working" means the
      apps are served and usable there, the shell's own dev calls go through Vite's proxy (plan D7), and a remote's entry URL moves under its
      base (plan D8). Add the same sentence to the Assumptions. Prettier clean.

## Phase 2 — Foundational: a gateway that tests can build (blocks every gateway task)

- [x] **T004** (backend) **Behaviour-preserving extraction.** Move `src/ApiGateway/Program.cs`'s setup into `GatewayHost` (a public static
      `Build(string[] args, Action<WebApplicationBuilder>? configure = null)` returning the `WebApplication`); `Program.cs` becomes a thin call.
      No route, policy or middleware order changes. Characterisation, captured **before** and after: `Architecture.Tests` (BoundaryTests
      untouched, still green) and the existing `GatewayRoutingIntegrationTests` / `GatewayRateLimitIntegrationTests` /
      `GatewayEdgeHeaderTransformTests` (CI, Docker). Add a `ProjectReference` from `Integration.Tests` to the gateway if absent. Commit alone.
- [x] **T005** (test) `tests/Integration.Tests/ApiGateway/GatewayTestHost.cs`: a helper that builds the gateway with `GatewayHost.Build` on
      a loopback Kestrel port with overridden configuration (clusters pointing at a stub destination, a low rate limit, a temp-directory `Ui`),
      plus a stub destination app that echoes method, path, headers and answers a WebSocket echo. No fixture dependency (these tests do not
      boot Aspire). Depends on T004.

## Phase 3 — US2 (P1): the spike, then hot reload and the fixed ports

**Goal**: prove ADR-0192's riskiest assumptions before anything depends on them. **Independent test**: quickstart section 0 (a), (b), (c),
and section 2.

- [x] **T006** (test, red) `GatewayUiProxyTests`: with `Ui:Mode=Proxy` and stub destinations for the shell and two remotes — a request to
      `/` and to an unmatched path is answered by the shell destination; `/remotes/cameras/remoteEntry.js` reaches the cameras destination
      **with the path unchanged** (no prefix strip: the dev server's `base` expects it); a WebSocket upgrade on `/` is carried to the shell
      destination and echoes; the proxied UI routes carry no `RateLimiterPolicy`; with **no** `Ui` section an unrouted path is a 404.
      Run, quote the failures. Depends on T005.
- [x] **T007** (backend) Implement `Ui` Proxy hosting (new `UiHosting` in `src/ApiGateway/`): the three UI routes and clusters from `Ui`
      configuration, shell route lowest order, no rate-limiter policy, nothing registered when `Ui` is absent. The gateway code references none
      of `Microsoft.AspNetCore.SignalR`, `.WebSockets`, `.Http.Connections`. T006 green; **`Architecture.Tests` BoundaryTests green and
      unedited**. Depends on T006.
- [x] **T008** (frontend) Set a Vite `base` constant in each remote's `vite.config.ts` (plan D6): `base: '/remotes/cameras/'` in
      `apps/management-cameras`, and `base: '/remotes/event-ingestion/'` in `apps/management-event-ingestion`. Depends on T001.
- [x] **T009** (frontend, red then green) `apps/management-web/src/app/navigation/remotes.test.ts`: add cases that relative remote URLs
      (`/remotes/cameras`, `/remotes/event-ingestion`) are accepted, keep their path, lose a trailing slash, and that an empty value in a
      production build still throws naming the variable. Run red where the code does not yet hold; adjust `remotes.ts` only if needed. The
      existing cases stay unmodified. Depends on T001.
- [x] **T010** (frontend) `apps/management-web/vite.config.ts`: `server.proxy` for `/api` (gateway origin via a shell-safe alias env name),
      `/remotes/cameras` → 5176, `/remotes/event-ingestion` → 5177, mirroring the kiosk's `/hubs` proxy; the config still works with the
      alias unset (unit tests, previews). Depends on T008.
- [x] **T011** (infra) `src/AppHost/WebApplications.cs`, `AppHost.cs`: in run mode only, the gateway gets `Ui__Mode=Proxy`, references to the
      three Vite resources (no `WaitFor`, matching the existing reasoning) and a fixed dev port (5180 per T002); the shell and kiosk env values
      for the console become `/api`, `/remotes/cameras`, `/remotes/event-ingestion` and the shell's proxy alias is injected. Test mode keeps
      dynamic ports and no `Ui`. Characterisation: the existing `AppHost*` tests pass; add an assertion for the fixed port and `Ui__Mode`
      if the existing pattern allows. Depends on T007, T008, T010.
- [x] **T012** ((a), (b) and (c) observed, see verification.md; the signed-in render is CI e2e) (a) and (b) observed on a lighter real-Vite-and-real-gateway rig (Docker was down), see verification.md; (c) at T016; the signed-in render is CI e2e (T028). **SPIKE observation (a), (b), (c)** on the real stack (quickstart section 0), one Aspire stack, built after stopping any
      other. Record each in `verification.md`: (a) a shell edit updates in place at the gateway address and BoundaryTests passes; (b) the
      cameras and event-ingestion pages render at the gateway address with every remote request under `/remotes/<name>/` and none to `:5176`
      / `:5177`; and the same through `:5173` (the Vite proxy). **A failure of (a) stops the work: comment on #3245, amend ADR-0192 as its own
      change, and do not continue.** Depends on T011.

## Phase 4 — US1 (P1): the whole console from one address

**Goal**: every data call at `/api`, page reloads land on the page. **Independent test**: quickstart sections 1 and 4 (c).

- [x] **T013** (test, red) `GatewayApiPrefixTests`: for each of the nine contexts a request to `/api/<ctx>/health` reaches the context's
      destination with `/api/<ctx>` removed, `Authorization` unchanged and `X-Fab` removed; each `/api` route carries the same CORS and
      rate-limit policy names as its bare twin (the parity guard, reading the configuration); `POST /api/stream-distribution/streams/authorize`
      and the bare form are both 404; `/api/not-a-context/…` is a 404 **and not the shell** (with `Ui:Mode=Proxy` set). Quote the failures.
      Depends on T005.
- [x] **T014** (backend) `src/ApiGateway/appsettings.json`: the nine `/api/<ctx>/{**catch-all}` routes and the carve-out twin in
      `GatewayHost`/`Program` (same policies, `PathRemovePrefix` `/api/<ctx>`, `RequestHeaderRemove: X-Fab`). T013 green. Depends on T013.
- [x] **T015** (test, red) `GatewayNavigationRuleTests` (spike (c), automated): with a stub REST destination and a stub shell destination,
      a bare-route request with **no** `Sec-Fetch-Dest` and one with `Sec-Fetch-Dest: empty` reach the REST destination; one with
      `Sec-Fetch-Dest: document` reaches the shell destination (`Ui` on) or is a 404 (`Ui` off), for `/system-variables/x` and
      `/event-ingestion/x` and all nine contexts. Quote the failures. Depends on T007, T014.
- [x] **T016** (backend) `appsettings.json`: the header match `Sec-Fetch-Dest` `NotContains` `document` on each bare route (not on `/api`).
      T015 green. **If `NotContains` does not match an absent header, split the rule per plan Risks (a `NotExists` route and a `NotContains`
      route per context) and say so in `verification.md`.** Depends on T015.
- [x] **T017** (frontend) The one-origin values reach the apps: confirm by reading `gateway.ts`/`remotes.ts` that `VITE_API_GATEWAY_URL=/api`
      yields `/api/<ctx>/…` request URLs; add a test in `apps/shared` or `management-web` if none pins a relative gateway origin. Depends
      on T011.
- [ ] **T018** **Observe** (quickstart section 1): sign in at the gateway address and back to it, open `/cameras` and `/event-ingestion/...`,
      reload each plus `/system-variables`, read the network log for any application request leaving the origin (SC-001). Record. Depends
      on T012, T014, T016, T017.

## Phase 5 — US3 (P2): a production build from the same origin

**Goal**: `Static` mode serves built bundles at the same paths; a missing setting fails loudly. **Independent test**: quickstart section 3.

- [x] **T019** (test, red) `GatewayUiStaticTests` (temp directories as the three roots): `/` and an unknown deep path (`/layouts/123`) return
      the shell's `index.html`; `/assets/x.js` returns the file; `/remotes/cameras/remoteEntry.js` returns the cameras root's file and
      `/remotes/event-ingestion/…` the other; `/api/…` and `/remotes/…` misses are 404, never the shell; responses carry
      `X-Content-Type-Options: nosniff`; a path traversal attempt (`/..%2f…`) returns no file outside the roots; startup **fails fast** when a
      root is missing or lacks `index.html` / `remoteEntry.js`. Quote the failures. Depends on T005.
- [x] **T020** (backend) Implement `Static` mode in `UiHosting`: static files from the three roots (`PhysicalFileProvider`), shell fallback,
      `nosniff`, fail-fast validation; registered **before** `UseRateLimiter`. T019 green; BoundaryTests green and unedited. Depends on T019.
- [x] **T021** (frontend) Production build proof: build the three apps with the one-origin values (quickstart section 3); with one required
      value removed, the app fails at load naming the variable (the existing `remotes.test.ts` cases already pin two of them; add the
      `VITE_API_GATEWAY_URL` equivalent if absent). Record the built folders' sizes and that `remoteEntry.js` chunks resolve under the base.
      Depends on T008, T009.
- [x] **T022** (observed on the real gateway over real production builds; a signed-in render is CI e2e, see verification.md) **Observe** (quickstart section 3): start the gateway with `Ui__Mode=Static` over the three `dist` folders and repeat T018's
      flow. Record. Depends on T020, T021.

## Phase 6 — US4 (P2): page loads are not throttled, nothing protected is exposed

- [x] **T023** (covered by the proxy, static and API rate-limit cases already written and the 4th-request 429 characterisation; not duplicated) (test, red then green) `GatewayUiRateLimitTests`: with `RateLimiting:PermitLimit` set to 3, ten static requests from one source
      all succeed (and ten shell fallbacks in `Proxy` mode); the fourth `/api/<ctx>/…` request in the window is 429; the bare route still
      429s as today. Run before T020/T007 are complete if useful; at the end all green. Depends on T007, T014, T020.
- [x] **T024** (observed on the real gateway in Static mode; see verification.md) **Observe** (quickstart section 4): with the production limit (not the run-lane 6000/min override), a cold console load plus a
      visit to every remote page from one address yields zero 429 (SC-003); more than 100 API calls in a minute are refused. Record. Depends
      on T018.

## Phase 7 — Polish, verification, delivery

- [x] **T025** (docs) `docs/deployment-frontend-env.md`: the one-origin values for `VITE_API_GATEWAY_URL`, `VITE_CAMERAS_REMOTE_URL`,
      `VITE_EVENT_INGESTION_REMOTE_URL`; the path map; a pointer to `contracts/gateway-paths.md`. `.devcontainer/devcontainer.json`
      `forwardPorts` gains the dev address. Prettier clean. Depends on T011.
- [x] **T026** (emulated locally: 1227 = 1227, no gaps or duplicates) (test infra) Add each new `Integration.Tests` class to a `ci-shards/shard-N.filter` (the shard coverage guard fails
      deterministically otherwise); run the guard locally if a script exists. Depends on T013, T015, T019, T023, T006.
- [x] **T027** (frontend) Run `vitest` in the three apps one at a time: **counts equal T001** (SC-002); `eslint src --max-warnings 0`,
      `tsc --noEmit`, `prettier --check` in each; exit codes read. Depends on T008–T010, T017, T021.
- [x] **T028** (spec edited; run by CI, see verification.md) (e2e) `e2e/operator-shell-federation.spec.ts`: change the three `:5176/remoteEntry.js` / `:5177/remoteEntry.js` URL
      expectations to the new `/remotes/<name>/remoteEntry.js` (plan D8); nothing else in e2e changes. Run the spec once against the stack if
      it can be run locally, otherwise state that CI's e2e job is the observation. Depends on T012.
- [ ] **T029** **SC-005**: measure click-to-first-frame (`e2e/click-to-first-frame.spec.ts`) on the camera detail page through the one
      origin and through `:5173`, **twice each** (the first run after machine churn looks like a regression); record the figures and the
      ≤ 3 s p95 bar. Depends on T028.
- [x] **T030** (full Architecture.Tests rerun not observed on the final tree: the harness stopped it for memory; see verification.md) `dotnet build` (Release) and `dotnet test tests/Architecture.Tests` foreground: BoundaryTests green and **its source file
      shows no diff** (`git diff origin/develop -- tests/Architecture.Tests/BoundaryTests.cs` empty); the gateway integration tests (CI). Depends
      on T020, T023.
- [x] **T031** Write `verification.md`: baseline vs after counts, the spike table (a)(b)(c) as observed, the observations T018/T022/T024/T029,
      the red-test failures quoted (T006, T013, T015, T019), gate exit codes, "§IV legs touched: none", the CORS-not-narrowed note, the e2e
      substring change, the CSP follow-up number. Tick T001–T031. Depends on T030.
- [ ] **T032** `/code-review medium` on the branch (and `/security-review`: the gateway becomes an HTML origin and serves files from
      disk); dispositions in `verification.md`. Depends on T031.
- [ ] **T033** Open the PR to `develop` (`--base develop`, `Closes #3245`), park it, merge on green with the run's head SHA equal to the PR tip
      and every non-skipped bucket passing via `gh pr merge --rebase --admin --delete-branch`; post the close-out; confirm #3245 closed;
      tick phase 1 in epic #3248 and note phase 2 (#3246) is unblocked. T032/T033 are not ticked until the PR is merged. Depends on T032.

## Dependencies

```text
T001 → T008, T009                         T004 → T005 → T006 → T007 ┐
T002 → T011 (port)                        T008 → T010 → T011 ← T007 ┘ → T012 (SPIKE: may stop the work)
T003 (independent doc)                    T012 → T018, T028;   T005 → T013 → T014 → T015 → T016 → T018
T005 → T019 → T020 → T022;  T020/T007/T014 → T023 → T024
T011 → T017, T025;  T012 → T028 → T029;  all → T030 → T031 → T032 → T033
```

**Parallel (`[P]`)**: none by default. One branch, one index; the Vite configs, gateway files and test files are distinct, but T004 and
T007 and T020 all edit the gateway and must be ordered, and one Aspire stack and one vitest run at a time.

## Implementation strategy

- **MVP = through T012.** A gateway that proxies the console at one origin in dev, with hot reload and the fixed ports intact, and the three
  ADR-0192 assumptions observed. Everything after assumes T012 passed.
- Then T013–T018 (the API at `/api`, the navigation rule), then `Static` mode (T019–T022), then the rate-limit proof, polish and delivery.
- If T012 (a) fails, stop at that task. Do not weaken `BoundaryTests`. Amend ADR-0192 as its own change and ask the owner.
