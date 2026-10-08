# Tasks 316: A shell that loads what you hold

**Spec:** `spec.md` (clarified, `878e9fb3`) · **Plan:** `plan.md`
**Issue:** a feature-level issue that supersedes #1008. It is created at the Phase 3 gate and added to
Project #13 (T013). Per the post-spec-028 convention there are no per-task issues.
**Engineers:** `frontend-engineer` (T003, T004, T007, T009, T011), `infra-engineer` (T001 shared
with frontend, T008), `test-writer` (T002, T005, T006, T010), orchestrator (T012 verify, T013 tracking).

**Phase 4a colour, per task** (plan §6). Ambiguity resolves to red.

| Task | Colour |
|---|---|
| T003, T004, and the move in T009 | **characterisation**: T002's baseline must stay green, **unmodified** apart from import lines and harness |
| T005, T006, T010 | **red**: each new test observed failing for its stated reason before its implementing task |
| T001 | investigation; no merged code |

**Story order and shippability:** foundational (T001–T004) → US1 + US3 (T005–T009, which together
make the federated cameras surface runnable) → US2 (T010–T011) → verify (T012). US1 is shippable
without US2: until T011 the Cameras nav entry stays in today's static list. US2 cannot ship without
US1, because it gates a remote that must exist.

**Parallelism (ADR-0109, disjoint files):**
- T003 and T004 both edit `apps/shared/package.json`, so they run **sequentially**.
- T005 ∥ T006: test-writers, disjoint files. They may run beside T003/T004, since they only add test files.
- T007 ∥ T008: the remote package vs AppHost/scripts, disjoint.
- T009 is the **join point**. It depends on T003, T004, T005, T006, T007 and T008.
- T010 → T011 → T012 → T013 are sequential.
- **Blocking:** T001 blocks T007, T008 and T009, because it fixes the plugin facts they build on.
  T002 must be captured before T003 touches anything.

---

## Foundational

### [T001] Verify the plugin and realm facts the plan depends on (frontend-engineer + infra-engineer), blocking

On a throwaway branch (nothing from it merges), with `@module-federation/vite@1.23.3` + Vite
8.3.1 in a scratch host and remote:

1. Does a **remote under `vite dev`** serve a loadable entry? Record the entry path
   (`remoteEntry.js` or other). If dev is unsupported, record that the remote's Aspire resource
   must run `build && preview` instead (plan A-2).
2. Does a trailing-slash shared key (`'@smart-sentinel-eye/shared/'`) share **subpath** imports as
   one instance? If not, confirm the explicit-key fallback (plan §3).
3. With `strictVersion: true` and a deliberate version mismatch, what does `loadRemote` reject
   with? It must be catchable, not a page crash.
4. After a failed entry fetch, does a second `loadRemote` re-fetch, or return the cached failure?
   What forces a re-fetch in runtime 2.9.2 (plan §4.4)?
5. Does Vite's default `server.cors` let `localhost:5173` fetch modules from `localhost:5176`?
6. **A-1:** against the booted realm, sign in as `operator` through `management-web` and record
   `User.scope`. It must contain the 21 `sse.*` scopes.

**Done when:** each answer is appended to `plan.md` as a dated "T001 findings" section, quoting
what was observed (commands and output). If any answer contradicts the plan, **stop and flag
it**. Do not adapt the plan silently.

### [T002] Characterisation baseline (test-writer), before any source change

Run and capture verbatim, on this branch before T003:
- `pnpm --filter @smart-sentinel-eye/management-web test` (whole suite) and `tsc --noEmit`
- `pnpm --filter @smart-sentinel-eye/shared test`
- Playwright against `aspire run`: `cameras.spec.ts`, `camera-detail.spec.ts`,
  `command-palette.spec.ts`, and `click-to-first-frame.spec.ts` **twice**, recording both p95 figures.

**Done when:** all green, with output saved for the PR body. A red baseline stops the slice:
something else is broken.

### [T003] [P with T005/T006 only] Move the store contract to `apps/shared` (frontend-engineer), characterisation, depends on T002

- New `apps/shared/src/store/createApiStore.ts` (plan §4.1), plus a `./store` export in
  `apps/shared/package.json`.
- `apps/management-web/src/app/store.ts` becomes `createApiStore(apiSlices)`. Keep `apiSlices`,
  `resetApiCaches`, `RootState` and `AppDispatch`.

**Done when:** `store.test.ts`, `subjectChangeResetsCache.test.tsx`, `staleBearerRetry.test.tsx`
and the whole management-web suite are green with **no test file changed**. Lint, typecheck and
`prettier --check` are clean.

### [T004] Move `useAssignedFabs` to `apps/shared/src/hooks` (frontend-engineer), characterisation, depends on T003

- `git mv` to `apps/shared/src/hooks/useAssignedFabs.ts`, export it from `hooks/index.ts`, and add
  `react-oidc-context` 3.3.1 to `apps/shared/package.json`.
- Change one import line in each of `RegisterCameraDialog.tsx`, `RuleDialog.tsx` and
  `SystemVariableDialog.tsx`.

**Done when:** the register, rule and system-variable dialog suites are green with no test file
changed. Lint, typecheck and format are clean.

---

## US1 + US3: the federated cameras remote, with its own lifecycle

### [T005] [P] [US1] Red: remote loading (test-writer), depends on T002

Write plan §6 tests **6** (`RemoteSurface.test.tsx`: load failure, retry re-fetches, bad module
shape) and **12(a)(b)** (`e2e/operator-shell-federation.spec.ts`: entry fetched from `:5176`;
exactly one instance per singleton via `__FEDERATION__`). Run them and also run
`tsc --noEmit`, since a missing named import binds to `undefined` under Vitest.

**Expected red:** module not found / remote not served / no share scope.
**Done when:** verbatim red output is captured for the PR.

### [T006] [P] [US3] Red: lifecycle guards (test-writer), depends on T002

Write plan §6 tests:
- **7**: `remotes.test.ts`, the prod-build guard.
- **9**: `scripts/singleton-versions.test.mjs`.
- **10**: `tests/Integration.Tests/AppHostCamerasRemoteTests.cs`. **Add its
  `ci-shards/shard-N.filter` entry in this task**: a missing entry fails CI deterministically.
- **11**: `scripts/wait-for-e2e-stack.test.mjs`, adding `management-cameras` to `composedResources`
  plus the `:5176` wait case.

**Expected red:** module, script and resource absent.
**Done when:** verbatim red output is captured, covering `dotnet test --filter` for test 10 and
`pnpm test:guards` for 9 and 11.

### [T007] [P] [US3] Scaffold the remote package (frontend-engineer), depends on T001

`apps/management-cameras/`:
- `package.json`: pins identical to the shell, plus `@module-federation/vite@1.23.3`.
- `vite.config.ts`: remote `cameras`, exposes `./CamerasSurface`, the plan §3 `shared` block,
  federation off under `VITEST`, `dts: false`, port 5176 `strictPort`.
- `tailwind.config.ts`, `postcss.config.js`, `tsconfig.json`.
- `eslint.config.js` with `no-restricted-imports` on `**/management-web/**`.
- `src/test/setup.ts`, mirroring the shell's.
- `src/styles/remote.css` (utilities only, plan §5.2).
- `public/nav-manifest.json` (plan §5.4).
- A placeholder `src/CamerasSurface.tsx` rendering nothing yet.

Also add `scripts/singleton-versions.mjs` (plan §3).

**Done when:** `pnpm install` is clean. `pnpm --filter @smart-sentinel-eye/management-cameras build|lint|typecheck|test`
pass. T006 test 9 is green. The root `pnpm build/lint/typecheck/test` pick the package up without
edits.

### [T008] [P] [US3] AppHost resource and e2e readiness (infra-engineer), depends on T001

- `src/AppHost/AppHost.cs`: `AddJavaScriptApp("management-cameras", "../../apps/management-cameras", "dev")`
  (or T001's script), plus `WithNpm(install: false)`, `WithHttpEndpoint(env: "PORT", port: 5176,
  isProxied: false)`, `WithExternalHttpEndpoints()` and `WithParentRelationship(apiGateway)`. It sits
  in the same `isRunMode && !isE2ETests` block. `management-web` gains
  `.WithEnvironment("VITE_CAMERAS_REMOTE_URL", camerasRemote.GetEndpoint("http"))`.
- `scripts/wait-for-e2e-stack.sh`: wait for `:5176` like the kiosk probes.

**Done when:** T006 tests 10 and 11 are green, the full `AppHostWebAppHostingTests` is green
**unmodified**, and the `dotnet build` Release build is clean. Stop the stack before building
(project memory).

### [T009] [US1] Move cameras into the remote and federate it (frontend-engineer), depends on T003–T008

**One commit:**
- `git mv apps/management-web/src/features/cameras apps/management-cameras/src/features/cameras`.
- In the moved tests, change **import lines and harness only**: the store comes from
  `createApiStore([camerasApi, streamsApi])`.
- Real `CamerasSurface.tsx`: static imports, descendant `<Routes>` (plan §5.3).
- Shell:
  - federation host config (`shell`, `remotes: {}`, the `shared` block);
  - `navigation/remotes.ts` with the prod guard;
  - `navigation/RemoteSurface.tsx`;
  - `@module-federation/runtime` pin;
  - `router.tsx`: `cameras/*` and the index both render `RemoteSurface` for now;
  - `SurfaceCrash` branch for `RemoteLoadFailure`.
- **The same commit** adds `apps/management-cameras/src` to the scanned sets of
  `ConsoleTriadAlphaTests`, `DesignTokenLayerTests` (including the remote's `tailwind.config.ts`),
  `InteractionStateTests` and `MotionLanguageTests` (plan §7). Without it, four architecture gates
  narrow silently.

**Done when:**
- T005 tests 6 and 12(a)(b) and T006 test 7 are green with their files unchanged.
- T002's baseline is green, unmodified apart from import lines and harness, including the four e2e
  specs.
- Architecture tests are green.
- Plan §6's singleton counterfactual has been run and quoted: `singleton: false` on shared makes
  12(b) and `cameras.spec.ts` fail.
- `git diff --stat develop -- apps/kiosk-web` is empty.

---

## US2: navigation shows only what the token holds

### [T010] [US2] Red: the navigation gate (test-writer), depends on T009

Write plan §6 tests:
- **1**: `navManifest.test.ts`.
- **2**: `visibleEntries.test.ts`, covering the four scope sets.
- **3**: `NavigationProvider.test.tsx`: 404, timeout, invalid manifest, collision.
- **4**: `ShellLayout.navigation.test.tsx`: nav **and** palette; recompute on scope change.
- **5**: `router.gating.test.tsx`: `NotAvailable`, no `registerRemotes`/`loadRemote` call,
  audit-only `/`.
- **8**: `apps/management-cameras/src/navManifest.published.test.ts`.
- **12(c)**: a narrowed `User.scope` through token-response interception.

The existing shell tests listed in plan §6 get a full-scope `User.scope` **fixture** only.

**Expected red:** modules absent, and the nav is still unconditional.
**Done when:** verbatim red output is captured.

### [T011] [US2] Implement the navigation gate (frontend-engineer), depends on T010

- `apps/shared/src/navigation/navManifest.ts` and `remoteSurface.ts`.
- `ResilienceSubsystem` member, if needed (plan §2.1).
- Shell `navigation/`: `shellEntries.ts`, `grantedScopes.ts`, `visibleEntries.ts`,
  `NavigationProvider.tsx`, `NotAvailable.tsx`.
- `ShellLayout` from `useVisibleEntries()` (nav **and** palette).
- `router.tsx`: `<Gated>` on every child route, and `<FirstVisibleSurface>` at the index.
- Run plan §6's two remaining counterfactuals (`.some` instead of `.every`; no `<Gated>` on the
  remote route) and quote the failures.

**Done when:**
- All T010 tests are green with their files unchanged since T010.
- T002's baseline is still green, with `command-palette.spec.ts` unmodified.
- Lint, typecheck, format and `pnpm test:guards` are clean.

---

## Verify and track

### [T012] Phase 5 verification (orchestrator), depends on T011

Follow plan §9 steps 1–7 in full and write the verification note on the PR:
- stopping the remote, retrying while it is down, then restarting it;
- shell `dist` hashes unchanged across a remote-only rebuild;
- click-to-first-frame before and after, twice each, ≤ 3 s p95;
- the T001 A-1 observation restated;
- kiosk diff empty.

Latency: no §IV leg; spec 002 FR-013 is cited with figures.

### [T013] Tracking (orchestrator)

- Create the feature issue, add it to Project #13 (`gh project item-add 13 --owner
  smartsolutionslab --url <issue-url>`), and verify with `--limit 2000`.
- Comment on #1008 that it is superseded by the new issue and spec 316, then close it.
- Note on the feature issue that #2487 was closed with no change delivered (Q1).
- The PR body states both deferrals (plan §8): gateway/edge serving and per-role scopes.
