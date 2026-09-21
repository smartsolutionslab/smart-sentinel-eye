# Spec 205 — The bearer a retry carries

**Issue:** #2301 — *The one 401 retry re-sends the bearer that just failed, and no test can see which token a request carries*
**Branch:** `2301-stale-bearer-retry` · **Base:** `origin/develop` @ `1b4b9956`
**Phase-4a colour:** **RED** (behaviour-changing) — a test that arrives green is a phase-4 failure (ADR-0139, CLAUDE.md §*Phase 4a has two colours*).
**ADRs:** **ADR-0080** (browser auth — `react-oidc-context` + the custom kiosk flow; the decision this repairs), **ADR-0143** (`POST`/`PATCH` not retried by default — the retry-safety frame this 401 retry sits inside), **ADR-0106** (the single API gateway all REST goes through), ADR-0074/0075 (two React apps, RTK Query), ADR-0131 (the kiosk's long-lived grant and `automaticSilentRenew`), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0109 (parallel markers), ADR-0037 (phases), ADR-0150 (wait for a condition, never a count).
**Constitution:** §Testing (RED for new behaviour; a test waits for a condition, not a count), §IV (latency budget — **N/A, see §*Latency budget*).

**No new ADR is required.** ADR-0080 already locks `react-oidc-context` as the browser auth mechanism and spec 011 FR-011/012 already locks "one silent renewal and one retry before the session counts as expired" (`apps/shared/src/api/gateway.ts:52-56`). This spec makes the retry actually *use* the renewal it just performed. Nothing about the auth library, the gateway, the scope model, or any context boundary changes.

**Severity: every operator and every wall screen loses its session on a renewal that succeeded.** Not an error page on one request — `onSessionExpired()` tears the session down: management-web renders the "Session expired" screen (`apps/management-web/src/App.tsx:35-49`) and the kiosk enters `beginReauthentication` (`apps/kiosk-web/src/app/useSessionExpiry.ts:194`). **management-web is the exposed app**: its `oidcConfig` sets no `automaticSilentRenew` (`apps/management-web/src/app/auth.ts:22-46`), so the 401-triggered renewal is the *only* renewal path it has — and it is the broken one.

---

## Problem

Every line number below was re-read in the working tree at `1b4b9956`, not copied
from the issue. Every behavioural claim below was **reproduced locally**
(transcripts in §*Observed*), not inferred.

### The filed defect

`apps/shared/src/api/gateway.ts:88-117`:

```ts
export const gatewayBaseQuery = (route: string): ReturnType<typeof fetchBaseQuery> => {
  const baseQuery = fetchBaseQuery({
    baseUrl: gatewayApiUrl(route),
    prepareHeaders: (headers) => {
      const token = accessTokenProvider();          // :92
      if (token !== undefined && token !== '') {
        headers.set('Authorization', `Bearer ${token}`);
      }
      return headers;
    },
  });

  return async (args, queryApi, extraOptions) => {
    let result = await baseQuery(args, queryApi, extraOptions);
    if (result.error === undefined || result.error.status !== 401) {
      return result;
    }

    if (await renewSessionOnce()) {                 // :106 — resolves `boolean`
      result = await baseQuery(args, queryApi, extraOptions);   // :107 — same closure
      ...
    }

    logResilienceEvent('session', 'expired');
    onSessionExpired();                            // :114
    return result;
  };
};
```

`renewSessionOnce()` resolves a **boolean**. It throws away the one thing the
retry needs — the token the renewal just minted — and `:107` re-enters a
`prepareHeaders` that reads `accessTokenProvider()`, a module-level slot written
during React render:

- `apps/management-web/src/App.tsx:26` — `setAccessTokenProvider(() => auth.user?.access_token);`
- `apps/kiosk-web/src/App.tsx:29` — identical.

`auth` is the memoised context value from the **last committed render**. The
renewal's new user reaches that slot only when React re-renders.

### Why the re-render is always too late

`react-oidc-context@3.3.1` publishes a renewed user through a `useReducer`
`dispatch` (`dist/umd/react-oidc-context.js:275-279`):

```js
const handleUserLoaded = (user) => { dispatch({ type: "USER_LOADED", user }); };
userManager.events.addUserLoaded(handleUserLoaded);
```

`oidc-client-ts@3.5.0` raises that event **before `signinSilent()` resolves**, on
both of its two paths:

- refresh-token path, `dist/umd/oidc-client-ts.js:3208-3211` —
  `await this.storeUser(user); await this._events.load(user); return user;`
- iframe path, `:3376-3378` — the same two statements inside `_signin`.

So by the time `sessionRenewer()`'s promise resolves, `dispatch` has already been
called — and React 19 schedules a non-discrete `useReducer` update on
`DefaultLane`, i.e. through the Scheduler's `MessageChannel`, a **macrotask**.
The continuation at `gateway.ts:107` is a **microtask**. The microtask always
runs first. This is not a probabilistic race the operator is unlucky to hit; it
is the ordering guarantee of the two schedulers.

### The correction the issue needs, part 1 — the "ref" fix does not work

#2301's first suggested remedy is *"read the token from a ref"*, citing
`CellPage.tsx:48-54` as the pattern to mirror. **That pattern is written during
render:**

```ts
const accessTokenRef = useRef(auth.user?.access_token);
// eslint-disable-next-line react-hooks/refs -- see above
accessTokenRef.current = auth.user?.access_token;      // apps/kiosk-web/src/features/cell/CellPage.tsx:48-53
```

A ref written during render becomes visible at exactly the same moment as a
module-level provider re-registered during render — which is what
`gateway.ts` already has. The ref there solves effect-dependency churn
(issues 1888, 1889), not renewal-vs-retry ordering. **Proved by counterfactual
below: a ref-based gate carries the same stale bearer and still tears the session
down.** The issue's *second* suggestion — re-resolving from the user manager — is
directionally right but not reachable as written; see §*Alternatives* in
`plan.md`.

### The correction the issue needs, part 2 — the test-blindness claim is too broad

#2301 states: *"No assertion in the repository inspects a request's
`Authorization` header."* **That is false.** `apps/shared/src/streaming/WhepClient.test.ts`
asserts it three times, including the exact analogous requirement:

```ts
// :344  it('close() DELETEs with the token current at close time, not the connect-time one (FR-015)', …)
// :365  expect(headers.Authorization).toBe('Bearer token-after-renewal');
```

The true claim is narrower and still damning: **nothing on the gateway / RTK Query
path asserts it.** The WHEP client already re-resolves `getToken()` at request
time and is tested on exactly that property (spec 142 FR-015); the gateway does
neither. The precedent this spec follows is therefore *in the repository*, one
directory away — not a new pattern.

---

## Observed

Run at `1b4b9956` in `apps/kiosk-web` (the only workspace holding both
`react-oidc-context` and `oidc-client-ts`), against the **real** `AuthProvider`,
the **real** `UserManager` store and event bus, the **real** `gateway.ts`, and
real React 19 scheduling. The only stub is a `signinSilent` that performs the
two statements the library's own `_useRefreshToken` performs
(`storeUser` then `events.load`) without a network round trip.

### 1. The race — observed, not inferred

```
CALL COUNT 2
FIRST  AUTH Bearer OLD-TOKEN
RETRY  AUTH Bearer OLD-TOKEN      <-- the retry re-sends the bearer that just failed
RENDERED AFTER NEW-TOKEN          <-- React did re-render, after the retry had gone
AssertionError: expected 'Bearer OLD-TOKEN' to be 'Bearer NEW-TOKEN'
```

**This closes the gap #2301 declared open.** The issue labelled its own runtime
claim *"inferred from the installed dist, not observed"*. It is now observed —
deterministically, in jsdom, with no Keycloak required, because the mechanism is
scheduler ordering rather than network timing. What remains unobserved is the
same sequence against a live Keycloak; §*Residual uncertainty* says why that is
not a gap worth a spec of its own.

### 2. The user-visible harm — observed

With a realistic second response (the stale bearer is rejected again):

```
REF-GATE onSessionExpired calls 1
REF-GATE rendered token after NEW-TOKEN
```

A session torn down while a valid, freshly minted token sits in the store.

### 3. The issue's "ref" remedy — disproved by counterfactual

Same harness, `accessTokenProvider` reading a `useRef` written during render,
mirroring `CellPage.tsx:48-53` exactly:

```
REF-GATE RETRY AUTH Bearer OLD-TOKEN
```

Unchanged. **The ref is a no-op for this defect.**

### 4. The chosen remedy — prototyped green

A prototype in which the renewer resolves the **token it minted** and the retry is
built against that token, same harness:

```
 Test Files  1 passed (1)
      Tests  1 passed | 2 skipped (3)
```

The fix direction in `plan.md` is therefore validated, not proposed.

---

## User stories

### US1 (P1) — A renewal that succeeds keeps the session

**As an operator** whose access token expires mid-session, **I want** the one
retry the gateway performs after a silent renewal to carry the token that
renewal produced, **so that** a renewal that worked does not end my session.

Independently shippable: it is a change to `apps/shared/src/api/gateway.ts` and
its two registration sites, verifiable end to end by the acceptance scenarios
below. It is also **not splittable** — `SessionRenewer`'s type is what carries the
token, so both call sites must change in the same commit or the workspace does
not typecheck.

### US2 (P2) — The race stays fixed, proven against the real provider

**As the next engineer to touch the gateway's auth path**, **I want** a test that
drives the *actual* `react-oidc-context` dispatch-vs-microtask ordering, **so
that** a future change cannot silently reintroduce a stale bearer while every
unit test still passes.

US1's tests prove the *contract* (the renewer's token reaches the retry). US2
proves the *mechanism* (React's re-render is too late, and the fix does not depend
on it). Both are needed because the contract test would pass even against a fix
that merely re-reads a render-written slot — which §*Observed* item 3 shows is not
a fix at all.

---

## Acceptance scenarios (Gherkin)

### US1 — happy path

```gherkin
Scenario: The retry carries the token the renewal minted
  Given the gateway holds the bearer "old-token"
  And the registered session renewer, when called, mints "new-token"
  When a gateway request returns 401
  Then exactly one renewal is performed
  And the retried request carries the Authorization header "Bearer new-token"
  And the caller receives the retried request's successful result
  And onSessionExpired is not called
```

### US1 — concurrency / conflict

```gherkin
Scenario: Concurrent 401s share one renewal and all retry with the same new token
  Given the gateway holds the bearer "old-token"
  And two requests are in flight
  When both return 401 before the renewal completes
  Then the session renewer is called exactly once
  And both retried requests carry the Authorization header "Bearer new-token"
  And onSessionExpired is not called
```

### US1 — degenerate input (the renewal "succeeds" with nothing to show)

```gherkin
Scenario: A renewal that yields no access token is a failed renewal
  Given a gateway request returns 401
  And the registered session renewer resolves with no token
  Then no retry is attempted
  And logResilienceEvent records "renew-failure"
  And onSessionExpired is called exactly once
  And the caller receives the original 401
```

```gherkin
Scenario: A renewal that rejects is a failed renewal
  Given a gateway request returns 401
  And the registered session renewer rejects
  Then no retry is attempted
  And onSessionExpired is called exactly once
```

### US1 — auth (a genuine authorization failure must still expire the session)

```gherkin
Scenario: A retry with the new token that is still refused expires the session
  Given a gateway request returns 401
  And the renewer mints "new-token"
  When the retry carrying "Bearer new-token" also returns 401
  Then onSessionExpired is called exactly once
  And the caller receives the 401
```

```gherkin
Scenario: A non-401 error is never renewed against
  Given a gateway request returns 500
  Then the session renewer is not called
  And onSessionExpired is not called
```

```gherkin
Scenario: An unauthenticated app sends no bearer at all
  Given no access token has been registered
  When a gateway request is made
  Then the request carries no Authorization header
```

### US2 — the mechanism, against the real provider

```gherkin
Scenario: A renewal racing a retry does not end the session
  Given a management-web AuthGate rendered inside the real react-oidc-context AuthProvider
  And the provider's user manager holds a user whose access token is "OLD-TOKEN"
  And signinSilent stores a user whose access token is "NEW-TOKEN" and raises the
      library's userLoaded event before it resolves, as oidc-client-ts does
  When a gateway request returns 401 and the gateway renews and retries
  Then the retried request carries the Authorization header "Bearer NEW-TOKEN"
  And onSessionExpired is not called
  And this holds even though React has not yet re-rendered at the moment the retry is issued
```

---

## Independent end-to-end test procedure

A reviewer who trusts none of the above can reproduce the defect and the fix
without reading a test file.

1. `git checkout 1b4b9956` (or `origin/develop`), `pnpm install`.
2. In `apps/shared/src/api/gateway.ts:92`, replace
   `const token = accessTokenProvider();` with
   `const token = 'STALE-TOKEN-FROM-BEFORE-RENEWAL';`.
3. `cd apps/shared && npx vitest run` → **169/169 pass**, `gateway.test.ts` 6/6.
   *Nothing in the suite can see which token a request carries.* (This is #2301's
   own counterfactual, re-run at `1b4b9956`.)
4. Revert step 2. Check out this spec's branch and run `pnpm -r --filter "./apps/**" test`.
   The same injection now fails `gateway.test.ts`'s new header assertions — the
   blindness is closed.
5. With the AppHost running (`dotnet run --project src/AppHost`), open
   management-web, sign in, and leave the console idle past the realm's access-token
   lifespan; then trigger any list query. Before the fix the console shows
   **Session expired**; after it, the query succeeds silently. Phase 5 records the
   observation; the jsdom evidence above stands on its own because the mechanism is
   scheduler ordering, which a live Keycloak does not change.

---

## Latency budget — **N/A**

No leg of §IV is touched. `gateway.ts:14-15` states the boundary explicitly:

> Realtime (SignalR, ADR-0152) and WebRTC media do NOT go through here — they
> stay direct, off the gateway and off the §IV latency budget.

This change is confined to the REST gateway path. It adds no per-request work:
`prepareHeaders` still reads one value; the second `fetchBaseQuery` factory call
is constructed only on the 401 path, which already performs a network round trip
and an identity-provider round trip. Constitution §VII's dashboard rule is not
engaged because no implemented leg changes.

---

## Locked tech choices

| Concern | Choice | Source |
|---|---|---|
| Browser auth | `react-oidc-context@3.3.1` over `oidc-client-ts@3.5.0` — unchanged | ADR-0080 |
| API client | RTK Query `fetchBaseQuery` behind `gatewayBaseQuery` — unchanged | ADR-0075, ADR-0106 |
| Renewal policy | one silent renewal, one retry, then expire — unchanged | spec 011 FR-011/012, `gateway.ts:52-56` |
| Retry safety | the 401 retry stays a *replay of the same request with a valid credential*; ADR-0143's default (no `RetryEveryMethod`) is untouched and no `Idempotency-Key` is introduced | ADR-0142, ADR-0143 |
| Test framework | Vitest + `@testing-library/react` (already present in all three workspaces) | `apps/*/package.json` |
| Waiting | `vi.waitFor` on a condition; **no fixed yield counts** | ADR-0150, constitution §Testing |

---

## Out of scope

### #2236 is **not** this defect — do not close it with this spec

#2236 (*"the first WHEP open after the dev tunnel proxy idles returns a spurious
401"*) is a coincidentally similar symptom with a different cause. Five
discriminators, from #2236's own text:

1. **Trigger.** Idle time on the run-mode DCP tunnel proxy — not a token renewal.
   This spec's race requires a renewal to be in flight; #2236's 401 arrives with
   *"nothing wrong with the token, the realm, or the hook"*.
2. **Transport.** #2236 has `connection reset by peer` underneath. A stale bearer
   produces a clean HTTP 401 from an application that answered.
3. **Path.** #2236 is a **WHEP open** — `WhepClient.postOffer`
   (`apps/shared/src/streaming/WhepClient.ts:153-175`), direct to MediaMTX. It does
   not pass through `gatewayBaseQuery` at all (`gateway.ts:14-15`).
4. **Recovery.** #2236 *"clears on retry"*. This defect's retry is precisely the
   thing that fails.
5. **Environment.** #2236 is scoped to the local run-mode tunnel and is explicitly
   unreproduced in CI or the Aspire fixture. This defect reproduces in jsdom with no
   infrastructure at all.

They are **not** the same race seen from two sides. #2236 stays open on its own
terms; a closing keyword for it must not appear in this spec's PR.

### Also out of scope

- **`automaticSilentRenew` for management-web.** It would reduce how often this
  path is reached; it would not fix it, and it is an auth-policy change for the
  operator console that deserves its own issue. Noted, not taken.
- **The WHEP client's own token freshness.** `WhepClient` already re-resolves at
  request time and is tested on it (`WhepClient.test.ts:344-366`). Its
  `getToken` reaches a render-written ref (`CellPage.tsx:48-54`), which has the
  same render-lag *shape* — but no microtask-chained retry drives it, so there is
  no race to fix. Recorded so the next reader does not have to re-derive it.
- **An `Authorization`-header assertion in the Playwright e2e suite.** The
  browser controls the header; asserting it from the test would assert the test's
  own input (see MEMORY *"an assertion must not check its own input"*).

---

## Assumptions, marked

- **A1 (verified, not assumed).** `auth.signinSilent()` from `react-oidc-context`
  resolves `User | null` — `null` on failure, because the provider's navigator
  wrapper catches and returns `null`
  (`react-oidc-context.js:214-232`). So `user?.access_token` is a total mapping to
  `string | undefined`. Confirmed against the installed dist and used by
  `useSessionExpiry.ts:212-218`, which documents the same behaviour.
- **A2 (assumption).** A renewal that resolves a user carrying no `access_token`
  should count as a **failed** renewal. Nothing in the repo states this; it is the
  safe reading (a retry with no credential can only 401), and it is specified as an
  acceptance scenario above rather than left to the implementer.
- **A3 (assumption).** Changing `SessionRenewer`'s return type from `boolean` to
  `string | undefined` is preferable to adding a second registration hook. It keeps
  one registration per app and makes the compiler find every call site. The three
  existing tests that assert `resolves.toBe(true|false)`
  (`useSessionExpiry.test.ts:66,76,82`, `App.test.tsx:213,216`) change **as a
  specified contract change**, not as an adjustment to make a red test pass — see
  `tasks.md` §*Which test edits are permitted*.

## Residual uncertainty

**What is now observed:** the ordering, the stale header, the session teardown,
the failure of the ref remedy, and the success of the chosen remedy — all against
the real libraries at their installed versions.

**What remains unobserved:** the same sequence driven by a live Keycloak. It is
not worth a spec of its own: the defect's mechanism is entirely client-side
scheduler ordering, and Keycloak's only role is to supply a different token
string. A live run would add the *frequency* of the path (how often a management
console's token dies mid-session), which phase 5 can observe cheaply against the
running AppHost — step 5 of the procedure above — and which changes nothing about
the fix. This is stated so a later reader is not told the gap was closed when it
was narrowed.
