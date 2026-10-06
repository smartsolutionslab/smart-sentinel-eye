# Plan 303 — A new operator starts with an empty cache

**Spec:** `spec.md` · **Issue:** #2524 · **App:** `apps/management-web` only

## 1. Shape

There are three pieces, all in `apps/management-web/src/app/`, and no change to
`apps/shared`.

| File | Kind | Role |
|---|---|---|
| `store.ts` | edit | Declare `apiSlices` once (the eight slices). Build `middleware` from it and export `resetApiCaches(dispatch)`, which dispatches `slice.util.resetApiState()` for each. The `reducer` object stays explicit, because the `RootState` type depends on it. |
| `subjectWatcher.ts` | new | Pure: `createSubjectWatcher(onChange: () => void): (subject: string \| undefined) => void`. It remembers the last *defined* subject. It calls `onChange` only when a defined subject differs from the last one. `undefined` is ignored and does not clear the memory. The first defined subject is recorded without firing. |
| `useResetApiCachesOnSubjectChange.ts` | new | Hook. `const auth = useAuth(); const dispatch = useDispatch();`. Holds a watcher in a `useRef` with `onChange = () => resetApiCaches(dispatch)`. **Event:** in `useEffect`, call `auth.events.addUserLoaded(u => observe(u.profile.sub))` and return the matching `removeUserLoaded`. That return keeps StrictMode's double mount from subscribing twice. **Seed:** a second `useEffect` keyed on `auth.user?.profile.sub` calls `observe(...)`. |
| `App.tsx` | edit | `AuthGate` calls `useResetApiCachesOnSubjectChange()` as its first hook after `useAuth()`. One line. |

**Why the seed effect exists.** `AuthProvider` loads the existing user on mount
through `userManager.getUser()`, and oidc-client-ts 3.5.0 defaults
`getUser(raiseEvent = false)` (`dist/esm/oidc-client-ts.js:2955`). The first
subject of a page load never raises `userLoaded`. Without the seed, the first
renewal would be treated as the first sighting and would never detect a change.
The seed cannot double-fire: the watcher is idempotent for an unchanged subject.
If the event handler has already observed B, the effect's later observation of B
does nothing.

**Why the event, not the seed, does the detection (decision on #2524).**
`signinSilent` runs `events.load(user)` synchronously before its promise resolves
(`:3163`, `:3331`, `:3507`). That is the same ordering `staleBearerRetry.test.tsx`
pins for spec 205. A reset dispatched from the handler therefore lands before
`gateway.ts` sends its retry. A render effect runs after React commits the new
user, which is after the retry, so the old record could flash.

**Why `auth.events` and not a `UserManager` owned in `auth.ts`.** `App.tsx`
spreads `oidcConfig` into `AuthProvider`, so the provider owns the
`UserManager`. Moving its construction into `auth.ts` would change the shape of
`oidcConfig` that `App.test.tsx:262` and `auth.test.ts` assert. That is a
refactor the decision did not ask for. `useAuth().events` is
`userManager.events` (react-oidc-context 3.3.1, `dist/esm:158`). It is the same
handler and is stable across renders.

## 2. RTK behaviour this relies on (RTK 2.12.0)

- `resetApiState` returns the slice to its initial state. A fulfilled action
  from a request that was in flight before the reset finds no substate with a
  matching `requestId` and is dropped. A response fetched under A's token cannot
  repopulate the cache after the reset.
- Mounted `useQuery` hooks notice that their subscription is gone and subscribe
  again, which issues a fresh request under the current bearer. The new
  subject's first result is therefore its own answer: data, or the refusal that
  spec 030 FR-008 renders.

**Phase 4a must observe both points, not assume them.** That is the purpose of
US1 scenario 2 in the integration test.

## 3. Boundaries and contention

- No cross-workspace change. `apps/shared` stays app-agnostic and the gateway's
  module singletons are untouched.
- `store.ts` and `App.tsx` are shared app files, but no other in-flight slice
  edits them (ADR-0109). Single engineer, no `[P]`.
- The existing tests that mock `react-oidc-context` and render `AuthGate` are
  `App.test.tsx` and `App.affordance.test.tsx`. Their `useAuth()` stubs have no
  `events`, so the hook would throw there. The test-writer adds
  `events: { addUserLoaded: vi.fn(), removeUserLoaded: vi.fn() }`. That is
  fixture plumbing, not an assertion change. No other test file mocks
  `react-oidc-context` while rendering `AuthGate`.

## 4. Tests (behaviour-changing → red first)

| # | File (new unless noted) | What it proves | Expected red |
|---|---|---|---|
| 1 | `src/app/subjectWatcher.test.ts` | First subject is silent. Same subject is silent. Different subject fires once. `undefined` is silent and A→undefined→B fires. A→B→B fires once. | Module missing → suite fails to import. Quote it. Acceptable for a pure helper; tests 3–4 carry the behavioural red. |
| 2 | `src/app/store.test.ts` | (a) `Object.keys(store.getState())` sorted equals `apiSlices.map(s => s.reducerPath)` sorted (FR-005 drift guard). (b) Seed one entry per slice with `slice.util.upsertQueryData`, or a fulfilled `initiate` against a stubbed `fetch`. Call `resetApiCaches(store.dispatch)`. Every slice's `queries` is `{}`. | `apiSlices` / `resetApiCaches` not exported. |
| 3 | `src/app/subjectChangeResetsCache.test.tsx` | Real `AuthProvider` + `UserManager` (`automaticSilentRenew: false`, `WebStorageStateStore(sessionStorage)`) + real `store` in `<Provider>`. Probe: a `Gate` that mirrors `AuthGate`'s three setters and the new hook, plus a child using `camerasApi.useGetCameraQuery(C)` that renders `data?.name ?? 'none'` and an error marker. Do not mock `react-oidc-context`. | Scenario 2 red: A's name still rendered beside the error (RTK keeps `data` on error). |
| 4 | same file | **Same-subject renewal** (`signinSilent` loads sub A with a new token): A's name stays and `fetch` count shows no reset-driven refetch. | Should pass before the fix. It is a guard against over-resetting, recorded as green-before. |
| 5 | same file | **Different subject via the 401 path.** `fetch` returns 200 {C as A} → (trigger `refetch`) 401 → `signinSilent` stub `storeUser`s + `events.load`s user sub B → retry 404 (and any resubscribe fetch 404). Assert, with `findBy…`/`waitFor` and never a fixed tick count (ADR-0150): C's name absent, error marker present, the last request's `Authorization` is `Bearer B-TOKEN`, every slice's `queries` holds no entry fulfilled under A. | Red: name still present. |
| 6 | same file | **Unload then load B.** `manager.removeUser()` → assert nothing reset (A still cached) → `events.load(userB)` → reset. | Red on the second half. |

Subject simulation follows `staleBearerRetry.test.tsx:120-131` exactly.
Replace `manager.signinSilent` with a stub that runs `await manager.storeUser(u);
await manager.events.load(u); return u;`, the two statements oidc-client-ts
performs on its real renewal paths. `userWith(token, sub)` extends that file's
helper with a `sub` parameter.

Reset the singletons in `afterEach`, as `staleBearerRetry.test.tsx` does:
gateway setters, `resetApiCaches(store.dispatch)` and `sessionStorage.clear()`.

## 5. Verification (phase 5)

Use the manual procedure in spec §5.2 against the Aspire stack. Quote the
resilience log line `session renew-success`, then the network panel showing the
refetch under the new bearer and the refusal on screen. Latency: N/A.

**Assumption, marked:** a real Keycloak may refuse to switch subjects at all.
oidc-client-ts renews with the refresh token when it holds one, and that token
belongs to the old SSO session. Ending that session should make the renewal
*fail*, which leads to "Session expired". It should not return a new `sub`. If
phase 5 sees that, record it as observed. In that case the subject-switch path
is reachable only through the iframe (`prompt=none`) renewal, and test 5 is the
end-to-end evidence. Do not stage a switch by editing the realm.
