# Tasks: Gateway and shell dev proxy edge cases

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md) | **Quickstart**:
[quickstart.md](./quickstart.md) | **Issue**: #3259 (the tracking issue; no per-task issues) | ADR-0192, spec 208

**Phase 4a colour: red first for every story** (all four are behaviour-changing, ADR-0139): each story's red test is run against the
unfixed code and its failure quoted verbatim in the PR. Two kinds of test are expected **green** before the fix and are not the evidence:
the guard cases that pin unchanged behaviour (written in the red task's commit), and T003, the owner-confirmed lookup-only change to spec
491's `devProxy.test.ts`, which is **characterisation** and is its own commit.

**Out of scope, never touched**: `@microsoft/signalr`, `AppHost`, the kiosk and wall (spec 494), Proxy-mode caching, hashed-asset caching.
**No Aspire, no Docker**: every test here is vitest or `Integration.Tests` `FixtureLogic` (in-process gateway). One vitest at a time.
Never `git stash` or `git checkout -- .`. No `Co-Authored-By` (ADR-0086). No new `Integration.Tests` class (both C# classes used are
already in `ci-shards/shard-2.filter`); if one is added after all, it needs a shard entry. `Ensure.That` is not available in
`src/ApiGateway` (no `Shared.Kernel` reference) and no new argument guard is needed: `UiRoot`s are built only from configuration that
`StaticRoots` already validates at start-up.

**§IV legs touched: none.**

**Format**: `[ID] [P?] [Story] Description`. `[P]` = owns files disjoint from every other task that may run at the same time.

## Phase 1: Setup

- [x] T001 Baseline on the clean branch, recorded in a new `specs/493-gateway-edge-cases/verification.md`: (a)
      `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/app/devProxy.test.ts` (files/tests); (b) the quickstart's
      `dotnet test tests/Integration.Tests --filter …` over the five gateway `FixtureLogic` classes (passed/failed); (c)
      `dotnet test tests/Architecture.Tests` (passed). `git status` clean.
- [x] T002 Admin: #3259 is on Project #13 (checked 2026-11-12 with `gh project item-list 13 --owner smartsolutionslab --limit 2000`
      by `content.url`; status Todo). Nothing to add.

**No foundational phase**: no shared contract, package, `AppHost` or Aspire resource changes, so nothing blocks the stories. After T001,
the **US1 chain (T003-T005)**, the **US4 chain (T006-T007)** and the **US2→US3 chain (T008-T011)** can run in parallel; within a chain the
order is strict.

## Phase 2: User Story 1 - The shell's dev server forwards exactly what the gateway forwards (Priority: P1)

**Goal**: FR-001 to FR-003. **Independent test**: `devProxy.test.ts`; quickstart step 2 on a real Vite server when 5173 is free.

- [x] T003 [P] [US1] **Characterisation, own commit.** In `apps/management-web/src/app/devProxy.test.ts`, add a test-side helper
      `entryFor(table, url)` that mirrors Vite 8.3.6's `createProxyContextMatcher` (a key starting with `^` is a regular expression tested
      against the full URL; any other key is `url.startsWith(key)`; the first matching entry in insertion order wins), and replace every
      key-spelling lookup (`table['/api']`, `table['/remotes/cameras']`, `table['/remotes/event-ingestion']`,
      ``table[`^/${context}(/|$)`]``, `['^/system-variables(/|$)']`) with `entryFor(table, '<a URL that entry serves>')`. Every
      assertion on the entry found stays as written; the `-other` negatives become "`entryFor` finds nothing". Run against the **unfixed**
      `devProxy.ts`: **green**, same test count as T001(a). Record in `verification.md`. Commit alone (owner-confirmed deviation, plan.md).
- [x] T004 [US1] **Red.** In the same file add: (a) for each of the nine `REST_CONTEXTS`, `/<context>?limit=5` finds the gateway entry
      (today: nothing); (b) `/api-tokens`, `/apiary`, `/remotes/cameras-x`, `/remotes/event-ingestion-x` find nothing (today: the plain keys
      take them). Guards, expected green now: `/api`, `/api?x=1`, `/api/system-variables`, `/remotes/cameras/remoteEntry.js`,
      `/remotes/event-ingestion/x?y`, `/<context>/x?y` find their entries; the entry for `/system-variables?tab=x` hands a
      `Sec-Fetch-Dest: document` request back to Vite (`bypass` returns the URL). Run; quote the failures in `verification.md`. Depends on T003.
- [x] T005 [US1] Implement plan D1 in `apps/management-web/src/app/devProxy.ts`: one function builds `^<escaped prefix>(/|\?|$)`; all
      twelve keys use it; the bare contexts keep `proxyUnlessNavigation`. Trim the header comment where it describes the old keys. T003 and
      T004 green; `eslint`, `prettier --check`, `tsc --noEmit` clean for `management-web`. Depends on T004.

## Phase 3: User Story 4 - The authorize hook is unreachable through the gateway for every method (Priority: P3)

**Goal**: FR-006. **Independent test**: `GatewayApiPrefixTests` (in-process gateway + `StubDestination`).

- [x] T006 [P] [US4] **Red.** In `tests/Integration.Tests/ApiGateway/GatewayApiPrefixTests.cs` add a theory over GET, PUT, PATCH, DELETE,
      HEAD × `/stream-distribution/streams/authorize`, `/api/stream-distribution/streams/authorize`: 404 and
      `destination.Requests.ShouldBeEmpty()` (today: forwarded, the stub answers). Guard, expected green: GET
      `/stream-distribution/streams/abc` is still forwarded. Run with the quickstart filter; quote the failures in `verification.md`.
      Depends on T001.
- [x] T007 [US4] Implement plan D5 in `src/ApiGateway/GatewayHost.cs` `MapStreamAuthorizeCarveOut`: `app.Map` instead of `app.MapPost` on
      both paths, still `Results.NotFound()`. Trim only the comment lines touched. T006 green; the two existing POST carve-out tests
      (`GatewayHostCharacterisationTests`, `GatewayApiPrefixTests`) green **unedited**. Depends on T006.

## Phase 4: User Story 2 - A reload of any console page gets the console in Static mode (Priority: P2)

**Goal**: FR-004. **Independent test**: `GatewayUiStaticTests` (in-process `StaticUiStack`).

- [x] T008 [P] [US2] **Red.** In `tests/Integration.Tests/ApiGateway/GatewayUiStaticTests.cs` add: a GET with `Sec-Fetch-Dest: document`
      to `/cameras/cam.01` answers 200 with the shell index, `nosniff`, `text/html` (today: 404). Guards, expected green: a GET **without**
      the header to `/cameras/cam.01` is 404 (owner-confirmed); navigations to `/remotes/cameras/x.js` and `/api/x.y` are 404 and not the
      shell; a POST to `/cameras/cam.01` is 404. Run; quote the failure in `verification.md`. Depends on T001.
- [x] T009 [US2] Implement plans D2 and D3: new `src/ApiGateway/UiRoot.cs` (name owner-confirmed) holding the `UiRoot(RequestPath,
    Directory, EntryFile)` record and its two operations, _serve_ (static files with `nosniff`) and _map fallback_ (pattern
      `{prefix}/{**path}` with no `nonfile` constraint; GET/HEAD only; a page navigation, `Sec-Fetch-Dest` containing `document`
      case-insensitively, always gets the entry file; a non-navigation gets it only for an extensionless last segment; otherwise 404). In
      `src/ApiGateway/UiHosting.cs`, `StaticRoots` yields three `UiRoot`s (shell: `""`, `index.html`, both operations; each remote:
      `/remotes/<name>`, `remoteEntry.js`, _serve_ only) and `MapShellFallback`/`StaticFiles` are removed. Nothing in `UiRoot.cs` names the
      shell (spec 494 adds `/kiosk` and `/wall` as roots). T008 green; every pre-existing `GatewayUiStaticTests` and `GatewayUiProxyTests`
      case green **unedited**. Depends on T008.

## Phase 5: User Story 3 - A redeploy never leaves a browser running yesterday's entry points (Priority: P2)

**Goal**: FR-005. **Independent test**: `GatewayUiStaticTests`. Sequential after US2: same production file and test class.

- [ ] T010 [US3] **Red.** In `GatewayUiStaticTests.cs` add: `Cache-Control` contains `no-cache` on `/`, `/layouts/123`, `/index.html`, a
      navigation to `/cameras/cam.01`, `/remotes/cameras/remoteEntry.js`, `/remotes/event-ingestion/remoteEntry.js` (today: absent).
      Guards, expected green: `/assets/app.js` and `/remotes/cameras/assets/chunk.js` carry no `no-cache`; `nosniff` on all eight. Run;
      quote the failures in `verification.md`. Depends on T009.
- [ ] T011 [US3] Implement plan D4 in `src/ApiGateway/UiRoot.cs`: _serve_ sets `Cache-Control: no-cache` when the request path equals
      `<RequestPath>/<EntryFile>` (ordinal, case-insensitive); _map fallback_ sets it on every response it serves. T010 green; all earlier
      gateway tests green unedited. Depends on T010.

## Phase 6: Polish, verify, review, PR

- [ ] T012 Full local gate after all chains land: the quickstart vitest and `dotnet test` filter; `dotnet test tests/Architecture.Tests`
      (BoundaryTests unedited; frontend changes are scanned there too); `dotnet build -c Release` (analyzers as errors);
      `management-web` `eslint`, `prettier --check`, `tsc --noEmit`, full `vitest run`. Compare counts with T001 in `verification.md`.
      Depends on T005, T007, T011.
- [ ] T013 Phase 5 (`/verify`): quickstart step 2 on a real Vite dev server **if port 5173 is free**, before-and-after table recorded; if
      5173 is held, record the gap and do not stop anything (owner-confirmed). The `FixtureLogic` runs are the gateway's observation.
      Write the verification note in `verification.md`. Depends on T012.
- [ ] T014 Phase 6: `/code-review` on the diff, plus a security review pass (Stories 2 and 4 change what the gateway serves and forwards at
      the trust boundary). Record each finding and its disposition in `verification.md`. Depends on T013.
- [ ] T015 Phase 7: PR to `develop` (`gh pr create --base develop`) with `Closes #3259`, every red failure from T004, T006, T008, T010
      quoted verbatim, T003's green-before run quoted, §IV "legs touched: none". After merge, check #3259 actually closed. Depends on T014.

## Dependencies

```text
T001 ─┬─ T003 → T004 → T005 ───────────────┐
      ├─ T006 → T007 ──────────────────────┼─ T012 → T013 → T014 → T015
      └─ T008 → T009 → T010 → T011 ────────┘
```

Parallel fan-out after T001: three agents may take the three chains at once (disjoint files:
`apps/management-web/src/app/devProxy*.ts`; `src/ApiGateway/GatewayHost.cs` + `GatewayApiPrefixTests.cs`; `src/ApiGateway/UiRoot.cs` +
`UiHosting.cs` + `GatewayUiStaticTests.cs`). `verification.md` is written by all three, so the orchestrator merges their entries, or each
chain reports its quoted output for one writer. The two C# chains share the `Integration.Tests` build, so run their tests one at a time.
