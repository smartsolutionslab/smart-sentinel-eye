# Plan 316: A shell that loads what you hold

**Spec:** `spec.md`, clarified at commit `878e9fb3` (Q1 and Q2 both resolved to option A by the
user, 2026-10-08).
**ADRs:** 0168 (Module Federation, claims-driven nav), 0107 (shell plus remotes, incremental), 0074 (one
app = one package + one Aspire JS resource), 0075 (one store), 0077/0078 (Radix + Tailwind tokens),
0080 (`react-oidc-context`), 0106 (gateway, REST only; the edge stays deferred per Q2), 0139
(red vs characterisation), 0109 (`[P]` = disjoint files), 0091 (no shortcuts in names), 0036.
**Constitution:** §VI (AppHost wires the remote's URL), §VIII (endpoint scope checks stay the
authority; the nav is UX), §Testing (two obligations).
**No backend bounded context is touched.** No C# outside `src/AppHost` and its tests changes, and
there is no cross-context call. The Identity → EventIngestion typed-HttpClient precedent (#2628) does
not apply.
**Latency:** no §IV leg is affected. Spec 002 FR-013 (click → first frame ≤ 3 s p95) is designed
to be unaffected by construction (§5.3), and is measured before and after (T012).

---

## 1. Shape

```
apps/
  management-web/                  the shell (evolved in place, FR-001)
    vite.config.ts                 + @module-federation/vite host: name 'shell', remotes {}, shared (§3)
    src/app/
      store.ts                     store = createApiStore(apiSlices)      (refactor, §4.1)
      router.tsx                   'cameras/*' → <RemoteSurface remote="cameras"/>; gated routes; index → first visible
      ShellLayout.tsx              nav + palette from useVisibleEntries() instead of DESTINATIONS
      navigation/                  NEW
        shellEntries.ts            the six in-shell NavEntry values (FR-010 table)
        remotes.ts                 the remote registry: [{ name: 'cameras', baseUrl: VITE_CAMERAS_REMOTE_URL }]
        grantedScopes.ts           parse User.scope → ReadonlySet<string>
        visibleEntries.ts          pure: (entries, scopes) → ordered visible entries
        NavigationProvider.tsx     fetches + validates manifests once per session; exposes composition state
        RemoteSurface.tsx          registerRemotes/loadRemote, lazy element, typed load failure
        NotAvailable.tsx           "not available to your account" panel (FR-013)
      useAssignedFabs.ts           DELETED (moved, §4.2)
    src/features/cameras/          DELETED (moved, FR-002)
  management-cameras/              NEW workspace package = the cameras remote (FR-017)
    package.json                   @smart-sentinel-eye/management-cameras
    vite.config.ts                 @module-federation/vite remote: name 'cameras', exposes './CamerasSurface'
    tailwind.config.ts             theme: tailwindTheme; content: ./src only
    postcss.config.js, tsconfig.json, eslint.config.js, index.html? (only if the plugin needs one, T001)
    public/nav-manifest.json       the published manifest (FR-015)
    src/CamerasSurface.tsx         descendant <Routes>: index → CamerasPage, ':cameraIdentifier' → CameraDetailPage
    src/styles/remote.css          utilities-only Tailwind build for this remote (§5.2)
    src/features/cameras/*         moved verbatim (7 production + 15 test files)
    src/test/setup.ts              mirrors management-web's (ADR-0150 deadline), §6
  shared/src/
    store/createApiStore.ts        NEW: the RTK store contract (§4.1)
    hooks/useAssignedFabs.ts       MOVED from management-web (§4.2)
    navigation/navManifest.ts      NEW: NavEntry type + zod schema + parse (FR-015)
    navigation/remoteSurface.ts    NEW: the exposed-module contract type { default: ComponentType }
src/AppHost/AppHost.cs             + AddJavaScriptApp("management-cameras", "../../apps/management-cameras", "dev"), port 5176
scripts/wait-for-e2e-stack.sh      + probe :5176 before the gate opens
scripts/singleton-versions.mjs     NEW guard: shared-singleton versions identical across shell and remotes (§3)
```

**Names (ADR-0091, no shortcuts):**

| Thing | Value |
|---|---|
| Package directory | `apps/management-cameras` |
| Package name | `@smart-sentinel-eye/management-cameras` |
| Aspire resource | `management-cameras` |
| Federation remote name | `cameras` |
| Shell federation name | `shell` |
| Dev port | 5176 (the next after 5173/5174/5175; `AppHost.cs` uses no 5176 today) |
| Shell env | `VITE_CAMERAS_REMOTE_URL` |

It is `management-cameras` rather than `cameras` because the kiosk also renders cameras, and the
directory sits beside `management-web` and `kiosk-web` in a flat `apps/*` workspace, where a bare
`cameras` would read as either.

## 2. Contracts

### 2.1 The nav manifest (FR-015), `apps/shared/src/navigation/navManifest.ts`

```ts
export type NavEntry = {
  readonly path: string;                     // absolute, starts with '/'
  readonly label: string;
  readonly icon?: string;                    // accepted per ADR-0168 §4; not rendered (§2.4)
  readonly order: number;
  readonly requiredScopes: readonly [string, ...string[]];  // all-of, non-empty
};

export type NavManifest = {
  readonly schemaVersion: 1;
  readonly remote: string;
  readonly basePath: string;                 // '/cameras'
  readonly entries: readonly NavEntry[];     // each entry.path === basePath or starts with basePath + '/'
};

export const SCOPE_PATTERN = /^sse\.[a-z]+(\.[a-z]+)+$/;
export function parseNavManifest(input: unknown): Result-like { ok: true; manifest } | { ok: false; reason: string };
```

- zod 4.6.5 (already a dependency of `apps/shared`). `schemaVersion` is `z.literal(1)`, so an
  unknown version is a parse failure (fail closed). `requiredScopes` is
  `z.array(z.string().regex(SCOPE_PATTERN)).nonempty()`.
- `parseNavManifest` returns a discriminated union rather than throwing. The caller logs
  `reason` through `logResilienceEvent('navigation', 'manifest-rejected', { remote, reason })`.
  Whether `ResilienceSubsystem` gains a `'navigation'` member or reuses an existing one is
  decided by reading `resilienceLog.ts`'s union at T011. Adding one member is in scope.
- **Collision rule** (FR-015): the shell rejects a remote manifest whose `basePath` equals, or is a
  prefix-segment of, any in-shell entry's path or another accepted remote's `basePath`.

### 2.2 The exposed-module contract, `apps/shared/src/navigation/remoteSurface.ts`

```ts
export type RemoteSurfaceModule = { readonly default: ComponentType };
```

The cameras remote exposes `./CamerasSurface` whose default export renders descendant
`<Routes>`. The shell checks the loaded module's shape: a missing or non-function `default` is a
load failure (FR-007), not a crash deep inside React.

### 2.3 Where manifests and entries come from

| Source | Mechanism | Changes require |
|---|---|---|
| In-shell entries (six) | `shellEntries.ts`, typed `NavEntry[]` | a shell change (they are shell code) |
| Remote list | `remotes.ts`: name → `baseUrl` from `import.meta.env.VITE_CAMERAS_REMOTE_URL` | a shell change, only when a *new* remote is added |
| A remote's entries, labels, scopes | `GET {baseUrl}/nav-manifest.json`, unauthenticated static JSON | a remote rebuild only (US3) |
| A remote's code | `GET {baseUrl}/remoteEntry.js` (path per plugin; T001 confirms), **only after gating** | a remote rebuild only |

The manifest is public static data: labels and scope *names* are not secrets, because the scope
catalogue is in the realm file and in OpenAPI summaries. Gating decides what is *fetched as code*,
not what is *known*.

### 2.4 `icon`

ADR-0168 lists an icon in the manifest. Today's nav renders text only, and `apps/shared/src/ui`
has no icon set. The schema **accepts** an optional `icon` string so the published contract
matches the ADR, and the shell **does not render it**. Rendering icons needs an icon set
(ADR-0077), which is not this slice. The cameras manifest omits it.

## 3. Shared-dependency governance (ADR-0168's accepted risk, made explicit)

Host and remote declare the same `shared` block (one exported constant per app's
`vite.config.ts`, values read from each package's own `package.json`):

| Module | `singleton` | `strictVersion` | `requiredVersion` | Why its identity is load-bearing |
|---|---|---|---|---|
| `react`, `react-dom` | true | true | exact pin (19.3.0) | hooks and context |
| `react-router-dom` | true | true | 7.18.4 | the remote's `<Routes>`/`useParams` must see the shell's router context |
| `react-redux` | true | true | 9.3.0 | `<Provider>` context: the remote must reach the shell's store |
| `@reduxjs/toolkit` | true | true | 2.12.0 | the listener middleware's `addListener` action identity (spec 314) |
| `react-oidc-context` | true | true | 3.3.1 | `useAuth` in `CameraDetailPage` must see the shell's `AuthProvider` |
| `@smart-sentinel-eye/shared` (all subpaths) | true | true | `0.0.0` (workspace) | `gateway.ts` token provider state; API slice instances; `resetApiCaches` coverage |

- **`strictVersion: true`** turns a mismatch into a load-time error from the federation runtime.
  `RemoteSurface` catches it as a load failure (FR-008). It is never a silent second copy.
- **Build-time guard:** `scripts/singleton-versions.mjs` (plus `.test.mjs`, run by
  `pnpm test:guards`) reads every `apps/*/package.json` that declares a federation role (marker:
  the presence of `@module-federation/vite` in `devDependencies`) and fails if any singleton
  above has a different pinned version between them. pnpm resolves a single workspace lockfile,
  but each package pins its own version string. This catches the human edit that bumps one app
  and forgets the other, before it reaches runtime.
- **Subpath sharing is the one plugin-dependent detail.** `@smart-sentinel-eye/shared` is
  consumed through about 50 subpath exports (`./api/cameras.api`, `./ui/primitives/Button`, …). If
  the pinned plugin honours a trailing-slash prefix key (`'@smart-sentinel-eye/shared/'`), use it.
  Otherwise, generate the explicit key list from `apps/shared/package.json`'s `exports` in both
  configs (a tiny shared helper, `apps/shared/federation/sharedKeys.ts`, read at config time). T001
  decides which, by observation.
- **Federation plugin is off under Vitest.** Both `vite.config.ts` files also carry the Vitest
  config. The federation plugin is added only when `process.env.VITEST` is unset, so unit tests
  keep importing modules directly.
- `dts: false` on both sides. The plugin's type-generation server is not needed: the exposed
  contract is the shared `RemoteSurfaceModule` type (§2.2).

**Pinned version:** `@module-federation/vite@1.23.3` (latest on 2026-10-08; peer
`vite ^5 || ^6 || ^7 || ^8`, so it accepts the pinned Vite 8.3.1; depends on
`@module-federation/runtime@2.9.2`). It is added as an exact pin to both packages, and
`@module-federation/runtime` is added as an exact pin to the shell, which calls it directly (§4.4).

## 4. Shell changes

### 4.1 The store contract moves to `apps/shared` (refactor, characterised)

`apps/shared/src/store/createApiStore.ts`:

```ts
export function createApiStore(slices: readonly AnyApiSlice[]) {
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: Object.fromEntries(slices.map((s) => [s.reducerPath, s.reducer])),
    middleware: (getDefault) =>
      getDefault().prepend(listenerMiddleware.middleware).concat(slices.map((s) => s.middleware)),
  });
}
```

The shell's `store.ts` keeps `apiSlices` (8), `resetApiCaches`, `RootState` and `AppDispatch`,
and becomes `export const store = createApiStore(apiSlices)`. The remote's tests build
`createApiStore([camerasApi, streamsApi])`: **the same construction the shell uses**, so the
listener-middleware ordering (spec 314) cannot drift between test and production.
`store.test.ts`, `subjectChangeResetsCache.test.tsx` and `staleBearerRetry.test.tsx` must pass
unmodified. The FR-005 drift guard becomes true by construction, and it still runs.

*Why move it rather than give the remote a local test store:* ADR-0107's Implementation Notes
name "RTK store contract" as a shared package to extract, and a second hand-written store is
exactly the divergence the spec 314 comment in `store.ts` warns about.

The `RootState` type stays derived from the shell's store. The remote does not import it (no
production remote file reads `RootState`; checked: every `../../app/store.js` import in
`features/cameras` is in a test file).

### 4.2 `useAssignedFabs` moves to `apps/shared/src/hooks` (refactor, characterised)

The file moves unchanged and is exported from `hooks/index.ts` (no new `package.json` export:
`./hooks` already exists). Its three importers change one import line each:
`features/cameras/RegisterCameraDialog.tsx`, `features/rules/RuleDialog.tsx` and
`features/systemVariables/SystemVariableDialog.tsx`. `react-oidc-context` is added to
`apps/shared/package.json` dependencies at 3.3.1. The covering tests are the register, rule and
system-variable dialog suites that already exercise the fab picker. They must stay green
unmodified.

### 4.3 Navigation (US2)

- `grantedScopes(user)`: `new Set((user?.scope ?? '').split(' ').filter(Boolean))`. It reads only
  `User.scope`, never the access token (FR-009).
- `visibleEntries(entries, scopes)`: filter `entry.requiredScopes.every((s) => scopes.has(s))`,
  then sort by `order` with stable ties. It is pure and unit-tested over the four scope sets in the
  spec (§6).
- `NavigationProvider` (mounted in `RoutedApp`, i.e. after authentication):
  - On mount, fetch each registered remote's manifest with `fetch(url, { signal: AbortSignal.timeout(MANIFEST_TIMEOUT_MS) })`.
    `MANIFEST_TIMEOUT_MS = 5_000`, a bound rather than a tuning knob: a down remote must not
    hold the whole shell's navigation, and 5 s is the same order as the gateway client's own
    attempt timeout (10 s) halved, so the nav settles before a REST call would give up. One
    constant, no configuration.
  - Each manifest settles into `accepted` or `rejected(reason)`. A rejection is logged and
    contributes no entry.
  - Context value: `{ status: 'loading' | 'ready', entries: NavEntry[] (in-shell + accepted
    remote), remoteFor(path): RemoteName | undefined }`.
  - `useVisibleEntries()` = `visibleEntries(context.entries, grantedScopes(auth.user))`, memoised
    on `auth.user?.scope` and `auth.user?.profile.sub` (FR-014). A renewed token produces a new
    `User`, which `react-oidc-context` re-renders with.
- `ShellLayout`: `DESTINATIONS` is replaced by `useVisibleEntries()` for both the nav and the
  `CommandPalette` items (FR-011). While `status === 'loading'`, in-shell entries render
  immediately and remote entries appear when their manifest settles. The nav never blocks on a
  remote.
- **Routes** (`router.tsx`): each in-shell route element is wrapped in `<Gated path="/layouts">`,
  which renders `NotAvailable` when the path's entry is not visible. The remote route is
  `{ path: 'cameras/*', element: <Gated path="/cameras"><RemoteSurface remote="cameras" /></Gated> }`.
  `errorElement: <SurfaceCrash />` stays on every child route.
- **Index** (FR-012): `{ index: true, element: <FirstVisibleSurface /> }`. It renders the element
  for the first visible entry directly, with no `<Navigate>`, preserving `router.tsx`'s stated
  reason. While a manifest that could own an earlier-ordered entry is still loading, it renders
  the existing `Centered`-style "Loading…" state. With no visible entry it renders the
  empty-navigation message.
- The in-shell `order` values are 20, 30, … in today's `DESTINATIONS` order, and the cameras
  manifest declares `order: 10`, so a full-scope session sees today's exact order (spec US2
  happy-path scenario).

### 4.4 Loading the remote (US1)

- `RemoteSurface` calls `registerRemotes([{ name, entry: `${baseUrl}/remoteEntry.js` }])` from
  `@module-federation/runtime` **only when first rendered**. Because `<Gated>` wraps it, that only
  happens for an entitled session, so an unentitled session never registers and never fetches
  (FR-013, SC-4).
- `loadRemote<RemoteSurfaceModule>('cameras/CamerasSurface')` sits behind `React.lazy`, inside the
  route element's `<Suspense>`. A rejection or a shape failure throws a `RemoteLoadFailure`
  (name, cause).
- `SurfaceCrash` branches on `RemoteLoadFailure`: it renders "This surface could not be loaded"
  and logs `logResilienceEvent('crash', 'remote-load-failed', { remote, message })`. Every other
  error keeps today's crash panel (FR-007).
- **Retry must re-fetch.** `React.lazy` caches a rejected promise, and the federation runtime may
  cache a failed entry. `RemoteSurface` therefore keys its lazy component on a retry counter held
  in the shell, and retry forces a fresh registration (`registerRemotes(..., { force: true })`
  or the pinned runtime's equivalent, which T001 confirms). The spec's "remote is down" scenario
  is tested by stopping and restarting the resource (T012), not only with a mock.
- Prod-build guard (US3, FR-017): `remotes.ts` throws at module load when `import.meta.env.PROD`
  and `VITE_CAMERAS_REMOTE_URL` is empty, with a message naming the variable. This mirrors
  `auth.ts`'s `VITE_KEYCLOAK_URL` check line for line.

## 5. The remote

### 5.1 Package

`package.json` mirrors `management-web`'s scripts (`dev`, `build`, `preview`, `lint`,
`typecheck`, `test`) and pins the same versions for every dependency it shares with the shell. Its
dependency list is what `features/cameras` imports: react, react-dom, react-router-dom,
react-redux, @reduxjs/toolkit, react-hook-form, @hookform/resolvers, zod, react-oidc-context and
`@smart-sentinel-eye/shared: workspace:*`. Dev dependencies are the same toolchain set plus
`@module-federation/vite`. The root `./apps/**` filters and `pnpm-workspace.yaml`'s `apps/*` pick
it up with no edit (FR-017).

### 5.2 Styles: why the remote compiles its own utilities

The shell's Tailwind `content` is `./src/**` plus `../shared/src/**`. **Classes used only in
remote code would not be generated by the shell's build**, and adding the remote's path to the
shell's content would make a remote change require a shell rebuild, against US3. So:

- `apps/management-cameras/src/styles/remote.css` contains `@import 'tailwindcss/utilities';` and
  `@config '../../tailwind.config.ts';` (theme = shared `tailwindTheme`; content = `./src/**`
  only). It has no preflight, no tokens and no fonts, because those come from the shell's
  stylesheet, already in the page.
- `CamerasSurface.tsx` imports `remote.css`, so the plugin loads it with the exposed module.
- A utility the shell also generates appears twice with identical declarations. This is harmless,
  and it is the cost of independent builds.
- `DesignTokenLayerTests` and `MotionLanguageTests` assert token and theme discipline per app. The
  remote is added to their scanned sets (§7).

### 5.3 `CamerasSurface` and the click-to-first-frame budget

`CamerasSurface` imports `CamerasPage` and `CameraDetailPage` **statically**, with no nested
`lazy`. The whole remote arrives on the first navigation to `/cameras`. The measured interaction
in `click-to-first-frame.spec.ts` (`link.click()` at line 402, from the list to a detail page)
therefore happens **after** the remote is loaded, and incurs no fetch. The only new cost is on the
first navigation to `/cameras`, which is outside FR-013's clock. T012 confirms this by measurement.

### 5.4 Manifest

`public/nav-manifest.json`:

```json
{
  "schemaVersion": 1,
  "remote": "cameras",
  "basePath": "/cameras",
  "entries": [
    { "path": "/cameras", "label": "Cameras", "order": 10, "requiredScopes": ["sse.cameras.read"] }
  ]
}
```

A remote unit test parses this file with the shared `parseNavManifest`, so the remote cannot
publish a manifest its own CI would reject.

## 6. Tests

**Colours** (declared here, per CLAUDE.md "the architect declares at phase 3"):

| Group | Colour | Covering evidence |
|---|---|---|
| §4.1 store extraction, §4.2 hook move, the cameras move (FR-002) | **characterisation, observed green before** | `management-web` Vitest suite (whole), `store.test.ts`, the 15 cameras test files, e2e `cameras.spec.ts`, `camera-detail.spec.ts`, `click-to-first-frame.spec.ts` (+ figure), `command-palette.spec.ts`. Moved test files may change **import lines and harness only**. |
| Remote loading, load failure, retry, single instance (US1 new behaviour) | **red** | new Vitest + e2e below |
| Navigation gate (US2) | **red** | new Vitest + e2e below |
| AppHost resource, readiness probe, prod-build guard, version guard (US3) | **red** | new C# integration (model only), node guard tests, Vitest |

**New red tests:**

1. `apps/shared/src/navigation/navManifest.test.ts`: valid manifest accepted; unknown
   `schemaVersion`, empty `requiredScopes`, a non-`sse.` scope, a missing `basePath`, and an entry
   outside `basePath` are each rejected with a reason.
2. `apps/management-web/src/app/navigation/visibleEntries.test.ts`: the four spec scope sets
   (all 21; all minus `sse.cameras.read`; only `sse.audit.read`; empty) → exact ordered labels.
3. `apps/management-web/src/app/navigation/NavigationProvider.test.tsx`: a manifest fetch that
   404s, times out (fake `fetch` that never resolves plus a short injected timeout), or fails
   validation contributes no entry and logs once. A basePath collision is rejected.
4. `apps/management-web/src/app/ShellLayout.navigation.test.tsx`: with stubbed `User.scope` (the
   `staleBearerRetry.test.tsx` pattern, real `AuthProvider`), nav **and** palette show exactly the
   visible set. A scope change re-renders the nav without remount (FR-014).
5. `apps/management-web/src/app/router.gating.test.tsx`: an unentitled `/cameras/x` renders
   `NotAvailable` and **`registerRemotes`/`loadRemote` are never called** (module mock of
   `@module-federation/runtime`). `/` renders Audit for an audit-only session.
6. `apps/management-web/src/app/navigation/RemoteSurface.test.tsx`: a `loadRemote` rejection
   renders the "could not be loaded" panel inside the layout (nav still present) and logs
   `remote-load-failed`. Retry calls `loadRemote` again (it is not served from the lazy cache). A
   module without a function `default` is a load failure.
7. `apps/management-web/src/app/navigation/remotes.test.ts`: production mode with
   `VITE_CAMERAS_REMOTE_URL` unset throws, naming the variable (mirrors `auth.test.ts`).
8. `apps/management-cameras/src/navManifest.published.test.ts`: `public/nav-manifest.json`
   parses.
9. `scripts/singleton-versions.test.mjs`: fixture packages with matching pins pass; one bumped
   `react-redux` fails, naming the module and both packages.
10. `tests/Integration.Tests/AppHostCamerasRemoteTests.cs` (application model only, the
    `AppHostWebAppHostingTests` footing): `management-cameras` exists, runs `dev` from
    `apps/management-cameras`, has endpoint 5176 `isProxied: false`, is parented to the gateway,
    and `management-web` carries `VITE_CAMERAS_REMOTE_URL` (key presence only, per that file's
    "never resolve" rule). **It is a new class, so it needs a `ci-shards/shard-N.filter` entry**:
    without one, CI fails deterministically.
11. `scripts/wait-for-e2e-stack.test.mjs`: `composedResources` gains `management-cameras`, and a
    case asserts the gate waits for `:5176`.
12. `e2e/operator-shell-federation.spec.ts`:
    - (a) navigating to `/cameras` fetches `remoteEntry.js` from `:5176`, and the camera list
      renders under the shell's nav.
    - (b) **single instance**: `page.evaluate` reads the federation runtime's share scope
      (`globalThis.__FEDERATION__`) and asserts exactly one loaded version per §3 singleton.
    - (c) a narrowed session: `page.route` on Keycloak's token endpoint rewrites the response's
      `scope` to drop `sse.cameras.read`. Then neither nav nor palette offers Cameras, `/cameras/x`
      shows "not available", and no request reaches `:5176`. This narrows **only `User.scope`**
      (the access token is unchanged), which is exactly the input the shell decides on. API
      enforcement is unchanged and already covered by spec 200 (Q1 resolution).

**Counterfactuals, run at Phase 4 and quoted in the PR:**
- Set `singleton: false` on `@smart-sentinel-eye/shared` → test 12(b) fails, and `cameras.spec.ts`
  fails with 401 (second `gateway.ts`).
- Drop `.every` for `.some` in `visibleEntries` → test 2 fails.
- Remove the `<Gated>` wrapper on the remote route → test 5 fails.
- Delete the `shard-N.filter` line → the shard check fails.

**Existing tests whose fixtures change (not their assertions):** `ShellLayout.test.tsx`,
`ShellLayoutCrashPanel.test.tsx`, `router.test.tsx`, `App*.test.tsx` and
`command-palette.spec.ts`'s session render the shell. They need a full-scope `User.scope` and,
where they render routes, the cameras remote mocked or served. A fixture that needs an assertion
edit is a block (ADR-0139).

## 7. Boundaries, gates and contention

- **Gates that silently narrow if nothing is done.** `ConsoleTriadAlphaTests` (`ConsoleSrc =
  "apps/management-web/src"`), `DesignTokenLayerTests` (`scannedTrees`, plus per-app
  `index.css`/`tailwind.config.ts` constants), `InteractionStateTests` (`scannedTrees`) and
  `MotionLanguageTests` (`ManagementSrc`) scan hard-coded trees. Moving 22 files out of
  `apps/management-web/src` would remove them from four architecture tests with every test still
  green. **The commit that moves the files adds `apps/management-cameras/src` to each scanned set in
  the same commit.** That widens the gates, and ADR-0144 permits widening. `PaginatedConsumerTests`
  and `ExternalFontHostTests` already enumerate `apps/*` and need nothing.
- **No cross-package relative import** from `apps/management-cameras` into `apps/management-web`
  (FR-006). ESLint `no-restricted-imports` in the remote's `eslint.config.js` forbids
  `**/management-web/**`. It is a lint gate, not a convention.
- **`apps/kiosk-web` is untouched** (FR-018). No task lists a file under it.
- **Contention:** `apps/shared/package.json` is edited by T003 (new `./store` export) and T004
  (`react-oidc-context` dependency). Those two tasks are not `[P]` with each other.
  `src/AppHost/AppHost.cs` is owned by exactly one task (T008). `ShellLayout.tsx` and
  `router.tsx` are each edited by T009 (US1, the remote route) and T011 (US2, gating), in
  sequence.
- **Each commit builds on its own** (rebase-merge, CLAUDE.md). The move (T009) is one commit
  that deletes from the shell and adds to the remote. A half-moved tree is never committed.

## 8. Deferred, recorded (not silently dropped)

- **Gateway / static-edge serving of the remote**: deferred by user decision (Q2, 2026-10-08)
  until a production deployment exists. The PR body and the feature issue carry the sentence.
- **Per-role scope grants**: separate decision (Q1, 2026-10-08). The feature issue links #2487 and
  notes that its closure delivered no change.
- **Icon rendering** (§2.4); **publishing `@smart-sentinel-eye/shared` outside the workspace**; **the
  six other remotes**.

## 9. Verification (phase 5)

1. `aspire run`; dashboard shows `management-web` and `management-cameras` under `api-gateway`.
2. Sign in as `operator`. On `/cameras`, the network panel shows `remoteEntry.js` from `:5176`.
   Register, rename and retire a camera; open its detail; watch the first frame.
3. Stop `management-cameras` in the dashboard and reload: the six in-shell surfaces work, and
   `/cameras` shows the load-failure panel. Retry with the resource still stopped: panel again.
   Start the resource, then retry: Cameras renders, with no shell restart.
4. Independent build: hash `apps/management-web/dist` after `pnpm --filter management-web build`.
   Change a label in the remote, run `pnpm --filter management-cameras build`, and re-hash: the
   shell's hash is unchanged.
5. `click-to-first-frame.spec.ts` before (on `develop`) and after, **each run twice** (project
   memory: the first run after churn reads like a regression). Quote the p95s. ≤ 3 s required.
6. A-1 (`User.scope` carries the 21 scopes) was observed at T001 against the booted realm. Restate
   the observation here.
7. `git diff --stat develop -- apps/kiosk-web` is empty.
