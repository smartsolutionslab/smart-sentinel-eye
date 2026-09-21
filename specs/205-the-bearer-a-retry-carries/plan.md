# Plan — Spec 205, the bearer a retry carries

**Spec:** `specs/205-the-bearer-a-retry-carries/spec.md` · **Issue:** #2301 ·
**Branch:** `2301-stale-bearer-retry` · **Base:** `origin/develop` @ `1b4b9956`

## Context and layers

**No .NET bounded context is touched.** This is entirely a frontend change, in
the shared package and its two consumers. There is no aggregate, no value object,
no invariant, no repository, no migration, no domain event, no integration event,
and no `Shared.Contracts` change — so ADR-0040/0073's `V<N>` rules, ADR-0092/0093's
folder rules, ADR-0113's concurrency rules and NetArchTest's boundary rules are
all untouched and hold trivially.

| Workspace | Role | Files |
|---|---|---|
| `apps/shared` | the gateway client all REST goes through (ADR-0106) | `src/api/gateway.ts`, `src/api/gateway.test.ts` |
| `apps/management-web` | registers the bearer + renewer + expiry hooks | `src/App.tsx`, `src/App.test.tsx`, `src/app/staleBearerRetry.test.tsx` (new), `package.json` |
| `apps/kiosk-web` | registers the same hooks via `useSessionExpiry` | `src/app/useSessionExpiry.ts`, `src/app/useSessionExpiry.test.ts` |

**The frontend boundary rule that does apply** is CLAUDE.md's *"`apps/shared`
is app-agnostic"*: the shared clients must not import from either app. The change
preserves that — the token still arrives through a registered hook; only the
hook's *shape* changes.

## The invariant being established

Stated once, so every task below can be checked against it:

> **A retry issued after a renewal carries the credential that renewal produced —
> never a credential that predates it.**

The current code cannot state this at all, because `renewSessionOnce()` resolves a
`boolean` and discards the token. The whole change is making that sentence
expressible and then true.

This is the gateway's version of a requirement the repository already holds on the
media path: `WhepClient` re-resolves `getToken()` at request time and is tested on
*"the token current at close time, not the connect-time one"*
(`apps/shared/src/streaming/WhepClient.ts:249-261`, `WhepClient.test.ts:344-366`,
spec 142 FR-015). The gateway is the one authenticated path without it.

---

## US1 — the renewer hands back the token it minted

### The change, `apps/shared/src/api/gateway.ts`

Three edits, all inside the module. **The exported surface changes by one type.**

**1. `SessionRenewer` carries the token** (`:57-59`):

```ts
// Spec 205 (#2301): a boolean tells the retry that a renewal happened and
// withholds the one thing it needs. `react-oidc-context` publishes the renewed
// user through a `useReducer` dispatch, which React schedules as a macrotask;
// the retry below is a microtask on the renewal's own promise chain and always
// runs first. Reading the token from any render-written slot — a module getter
// or a ref alike — therefore re-sends the bearer that just failed.
type SessionRenewer = () => Promise<string | undefined>;

let sessionRenewer: SessionRenewer = () => Promise.resolve(undefined);
```

**2. `renewSessionOnce` resolves the token** (`:72-86`). The shared-in-flight
promise is unchanged in structure — concurrent 401s still await one renewal and
now all receive the same fresh token. `renew-success` / `renew-failure` are keyed
on a *usable* token, which is what makes assumption A2 (`spec.md`) enforced rather
than assumed:

```ts
let renewalInFlight: Promise<string | undefined> | null = null;

const renewSessionOnce = (): Promise<string | undefined> => {
  if (renewalInFlight === null) {
    logResilienceEvent('session', 'renew-start');
    renewalInFlight = sessionRenewer()
      .catch(() => undefined)
      .then((token) => {
        renewalInFlight = null;
        logResilienceEvent('session', isUsable(token) ? 'renew-success' : 'renew-failure');
        return token;
      });
  }
  return renewalInFlight;
};
```

**3. The base query is built against a named bearer** (`:88-117`). Factoring
`prepareHeaders` out is what lets the retry name *its* token instead of reaching
for a shared slot:

```ts
const isUsable = (token: string | undefined): token is string => token !== undefined && token !== '';

const gatewayQueryFor = (route: string, bearer: AccessTokenGetter): ReturnType<typeof fetchBaseQuery> =>
  fetchBaseQuery({
    baseUrl: gatewayApiUrl(route),
    prepareHeaders: (headers) => {
      const token = bearer();
      if (isUsable(token)) {
        headers.set('Authorization', `Bearer ${token}`);
      }
      return headers;
    },
  });

export const gatewayBaseQuery = (route: string): ReturnType<typeof fetchBaseQuery> => {
  const baseQuery = gatewayQueryFor(route, () => accessTokenProvider());

  return async (args, queryApi, extraOptions) => {
    let result = await baseQuery(args, queryApi, extraOptions);
    if (result.error === undefined || result.error.status !== 401) {
      return result;
    }

    const renewed = await renewSessionOnce();
    if (isUsable(renewed)) {
      result = await gatewayQueryFor(route, () => renewed)(args, queryApi, extraOptions);
      if (result.error === undefined || result.error.status !== 401) {
        return result;
      }
    }

    logResilienceEvent('session', 'expired');
    onSessionExpired();
    return result;
  };
};
```

**`setAccessTokenProvider` is untouched.** The first attempt reads the
render-written slot exactly as it does today — and that is correct, because at the
moment of the *first* attempt no renewal is pending and the last committed render
does hold the current token. Only the retry needed a different source, and only
the retry gets one. This is the smallest change that makes the invariant true
(CLAUDE.md §*Smallest possible change*).

**Why a second `fetchBaseQuery` call is not a cost worth avoiding.** It is a
closure factory, not a client; it allocates nothing beyond a small object, and it
happens only on the 401 path, which has already paid for a failed HTTP round trip
and an identity-provider round trip. Threading the token through `extraOptions`
instead would need a cast through RTK's `{}`-typed extra options — more machinery
and less obvious, for no measurable gain.

### The two registration sites

`apps/management-web/src/App.tsx:27-32`:

```ts
setSessionRenewer(() =>
  auth
    .signinSilent()
    .then((user) => user?.access_token)
    .catch(() => undefined),
);
```

`apps/kiosk-web/src/app/useSessionExpiry.ts:177-190` — same mapping, keeping its
`renewalInFlight.current` bookkeeping and its `beginReauthentication(cause)` on
rejection:

```ts
setSessionRenewer(() => {
  renewalInFlight.current = true;
  return auth
    .signinSilent()
    .then((user) => {
      renewalInFlight.current = false;
      return user?.access_token;
    })
    .catch((cause: unknown) => {
      renewalInFlight.current = false;
      beginReauthentication(cause);
      return undefined;
    });
});
```

`auth.signinSilent()` resolving `User | null` is verified, not assumed — the
provider's navigator wrapper catches and returns `null`
(`react-oidc-context.js:214-232`), which `useSessionExpiry.ts:212-218` already
documents in prose. `user?.access_token` is therefore total.

**One deliberate behaviour change to record.** A renewal that resolves a user
carrying no `access_token` previously counted as success (`user !== null`) and
triggered a retry that could only fail; it now counts as failure and escalates
directly. That is spec.md A2, made explicit here so a reviewer meets it rather
than discovers it.

### Observability

No new `logResilienceEvent` category, no new event name. `renew-start`,
`renew-success`, `renew-failure`, `expired` keep their spellings; only what
`renew-success` *means* is tightened (a usable token, not merely a non-null user).
Nothing is added to the resilience log per request.

---

## US2 — the mechanism under test, against the real provider

**New file:** `apps/management-web/src/app/staleBearerRetry.test.tsx`.

management-web is the right home: its `AuthGate` registers the renewer inline
at `App.tsx:26-33`, in the same synchronous-during-render style the bug lives
in. (Its `oidcConfig` sets no `automaticSilentRenew` at `src/app/auth.ts:22-46`
— **correction, phase-6 review: this does not make the 401 path its only
renewal path**. `oidc-client-ts@3.5.0` defaults that setting to `true`, so
management-web already runs background renewal exactly as kiosk-web does; it
simply never states so. The `AuthGate` registration shape, not the absence of
another renewal path, is why this app is the right home for the test.)

The existing `App.test.tsx` **mocks `react-oidc-context` wholesale**
(`App.test.tsx:32-42`), so it can never see this ordering. The new file must not
mock it.

Shape, established by the phase-1 experiment (transcripts in `spec.md` §*Observed*):

1. A real `UserManager` from `oidc-client-ts`, with a `WebStorageStateStore` over
   `window.sessionStorage`, pre-loaded with a user carrying `OLD-TOKEN`.
2. `signinSilent` replaced on the instance by the two statements the library's own
   `_useRefreshToken` performs before resolving —
   `await manager.storeUser(newUser); await manager.events.load(newUser); return newUser;`
   (`oidc-client-ts.js:3208-3211`; `_signin` does the same at `:3376-3378`). This
   is the only stub, and it exists solely to avoid a network round trip.
3. A gate component that calls the same three setters with the same expressions as
   `App.tsx:26-33`, rendered inside the **real** `<AuthProvider userManager={…}>`.
4. `fetch` stubbed 401-then-200; assert the **second** call's `Authorization`
   header is `Bearer NEW-TOKEN`, and that `onSessionExpired` was not called.

**One dependency is added:** `oidc-client-ts: "3.5.0"` to management-web's
`devDependencies`. It is already installed as `react-oidc-context`'s peer and is
already a direct dependency of `apps/kiosk-web` at exactly that version, so the
lockfile gains a reference, not a package. The version must match kiosk-web's
character for character.

**The drift this test cannot close, and what does close it.** The gate is a mirror
of `AuthGate`, not `AuthGate` itself — `<App/>` builds its `AuthProvider` from
`oidcConfig`, the settings member of the union, so no stub manager can be injected
into it without changing production code for a test's benefit. The mirror is
therefore bound to the original by a second, cheap guard: `App.test.tsx:205-217`
already captures the **real** renewer that the **real** `AuthGate` registers, and
US1 changes it to assert the resolved *token*. Between them, the mirror proves the
ordering and the original proves the contract.

---

## Alternatives considered and rejected

| Alternative | Verdict |
|---|---|
| **Read the token from a `useRef` written on every render** (#2301's first suggestion, citing `CellPage.tsx:48-54`) | **Rejected — disproved.** A ref written during render becomes visible exactly when a provider re-registered during render does, which is what `gateway.ts` already has. The counterfactual in `spec.md` §*Observed* item 3 carries the same stale bearer and still fires `onSessionExpired`. The ref in `CellPage` solves effect-dependency churn (issues 1888, 1889), a different problem. |
| **Re-resolve via `userManager.getUser()` inside `prepareHeaders`** (#2301's second suggestion) | **Rejected as written — not reachable, and costly if made reachable.** `useAuth()` does not expose `getUser`: `userManagerContextKeys` is `clearStaleState, querySessionStatus, revokeTokens, startSilentRenew, stopSilentRenew` and `navigatorKeys` is the signin/signout set (`react-oidc-context.js:159-175`). Reaching a manager would mean each app constructing its own `UserManager` and passing it to `AuthProvider` — a change to both apps' auth bootstrap (ADR-0080 territory) — and would then put an async storage read and a JSON parse on **every** request to service a failure that happens once per token lifetime. |
| **Subscribe to `auth.events.addUserLoaded` and push the token into the gateway** | **Rejected — correct but larger.** It would work: the library raises `userLoaded` before `signinSilent` resolves, so the push beats the retry. But it needs an effect and a subscription in both apps, converts `accessTokenProvider` from a getter to a pushed value with two writers, and introduces a last-write-wins question between a render and an event. It buys nothing over threading the token the renewal already returned. |
| **Give management-web `automaticSilentRenew: true`** | **Rejected — not a fix.** It changes how often the path is reached, not what the path does. Filed as out of scope in `spec.md`. |
| **Add an `Idempotency-Key` to the retry** (ADR-0142) | **Rejected — wrong mechanism.** The retry is not a duplicate write the server must deduplicate; it is a replay of a request the server *rejected* and never applied. ADR-0143's default (`POST`/`PATCH` get one attempt from the resilience handler) is untouched — this retry has always been the gateway client's own, gated on 401. |

---

## Risks

1. **`SessionRenewer`'s type change is a breaking change to a shared export.** It
   is also the point: TypeScript finds all three call sites (two apps, one default).
   Mitigated by the compiler, not by vigilance.
2. **Three existing assertions on the old boolean must change.** They are listed
   exactly in `tasks.md` §*Which test edits are permitted*, so a phase-4 engineer
   is not left deciding whether editing them violates "may not edit the tests to
   pass". They are a specified contract change, decided here at phase 3.
3. **The kiosk's `renewalInFlight.current` bookkeeping must keep its current
   timing.** It is set false on both branches before returning; the mapping change
   must not move either assignment.
4. **`pnpm-lock.yaml` changes** because of the one devDependency. Expected; CI's
   frozen-lockfile install needs the committed lockfile to match.

## Definition of done

- The retry's `Authorization` header is asserted — the first such assertion on the
  gateway path — and carries the renewal's token.
- #2301's counterfactual (`gateway.ts:92` → a hard-coded stale token) now **fails**
  `gateway.test.ts`.
- The real-`AuthProvider` ordering test passes, and is observed failing first.
- `pnpm -r --filter "./apps/**" test`, `lint`, `typecheck`, `format:check` clean.
- No new ADR, no constitution amendment.
