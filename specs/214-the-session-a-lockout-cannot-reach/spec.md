# Spec 214 — The session a lockout cannot reach

**Issue:** #2509 — "Whether a locked-out account's live session survives the
lockout is unverified"
**Branch:** `2509-lockout-session-survival`
**Lane:** autonomous (ADR-0144) — **and this spec ends inside that lane's
limits on purpose**; see *What this spec delivers*.
**ADRs:** ADR-0007 (Keycloak per fab), ADR-0008 (two browser auth flows),
ADR-0037 (the seven phases), ADR-0080 (browser auth), ADR-0103 (integration
tests against the Aspire fixture, no Testcontainers), ADR-0109 (disjoint-file
parallelism), ADR-0131 (a kiosk keeps its own grant across a restart),
ADR-0132 (what an offline grant costs), ADR-0139 (a rule fails the build, not
the review), ADR-0144 (the autonomous lane and what it may not do)
**Constitution:** §VIII (safe by default at trust boundaries), §Availability
(24/7, "a wall of 20 kiosks rebooting must come up unattended"),
NFR §Security ("token-bound, short-lived credentials"), §Testing

---

## Problem

Spec 207 (#2285) turned on Keycloak's brute-force detector and, during its own
phase-5 run, found the lock trips after **two** rapid wrong guesses rather than
`failureFactor`'s ten — `quickLoginCheckMilliSeconds: 1000` gets there first.
`specs/207-a-guess-that-runs-out/verification.md` records the moment:

```json
{"failedLoginNotBefore":1789999447,"numFailures":2,"numTemporaryLockouts":0,"disabled":true}
```

`management-web` is a public client with `directAccessGrantsEnabled: true`
(`src/AppHost/Realms/smart-sentinel-eye-realm.json:163`), deliberately left
open by spec 207 because `AspireFixture` depends on it. So an unauthenticated
party who can reach the realm can hold **any named account** locked with two
HTTP `POST`s per minute — including `wall-munich`, `wall-dresden`,
`wall-berlin`, `wall-hamburg`, which nobody is standing next to.

Spec 207 wrote down what it did not know, in as many words:

> **Open question, not resolved here:** whether a locked account's *existing*
> session survives via `grant_type=refresh_token` — i.e. whether the lockout
> denies only new sign-ins or also revokes live sessions — was not checked
> against the running server. Recorded as unresolved rather than assumed
> either way.

That question is this spec. It is not cosmetic: it is the difference between a
bounded nuisance and a two-request remote kill switch on a 24/7 video wall.

---

## Premise check — the issue's claims, re-verified at HEAD (`cf55f203`)

The standing lesson is that an issue's premise goes stale before it is
delivered. Everything material here **holds**, and reading it turned up three
facts the issue did not have.

| Issue claim | At HEAD | Verdict |
|---|---|---|
| `quickLoginCheckMilliSeconds: 1000` locks after two rapid guesses | `:17`, value `1000`; `numFailures: 2` at lockout in 207's verification | **Holds** |
| `maxFailureWaitSeconds` caps the lock at 15 min | `:15`, `900` | **Holds** |
| ROPC open on `management-web` | `:163` `directAccessGrantsEnabled: true` | **Holds** |
| `wall-*` accounts are ordinary realm users | `:371`, `:393`, `:415`, `:437`, all `enabled: true`, `realmRoles: ["user","offline_access"]` | **Holds** — and see A1 |
| The wall accounts are as lockable as `operator` | brute-force state is keyed on the **user**, not the client (`DefaultBruteForceProtector.failedLogin(realm, user, …)`), so a wrong guess posted at `management-web` locks that user for **every** client | **Holds, and is worse than stated** — `kiosk-wall` has `directAccessGrantsEnabled: false` (`:236`), which does **not** protect it |
| Investigation step 3 names `e2e/wall-survives-a-process-death.spec.ts` | the file exists under that exact name | **Holds** |
| The issue has a third investigation step | body step 3 is "run the wall-survives spec against the locked state"; there are **four** steps, the fourth being "record the result" and, if refresh is blocked, escalate | **Holds**; no comment has narrowed the scope — the issue has **zero** comments |

**A1 — the wall accounts are *not* exempt from the policy, and nobody made
them so.** Checked rather than assumed, because the task asked for it. Keycloak
has no per-user brute-force opt-out, the realm sets none, and `wall-*` carry no
attribute or role that would change it. `"sse.purpose"` on each wall user
(`:386-389`) explains only the `offline_access` role. They are subject to
exactly the same detector as `operator` and `admin`.

**A2 — the wall does not hold an ordinary refresh token. It holds an *offline*
one, and it keeps it in `localStorage`.** `apps/kiosk-web/src/app/auth.ts:51`:

```ts
const SCREEN = IS_WALL_DISPLAY
  ? { clientId: 'kiosk-wall', scope: 'openid offline_access' }
  : { clientId: 'kiosk-web', scope: 'openid' };
```

with `userStore: new WebStorageStateStore({ store: window.localStorage })` and
`automaticSilentRenew: true` (ADR-0131). The issue's framing — "within its
token's remaining lifetime" — therefore under-describes both branches. Under
the benign answer the wall's grant has no expiry to run out (`typ: "Offline"`,
no `exp`, pinned today by `e2e/wall-outlives-its-session.spec.ts`). Under the
severe answer the damage lands *sooner* than the issue supposed, not later —
see A3.

**A3 — under the severe answer the wall does not merely fail to renew; it
stops rendering at the next silent renew and never restarts itself.** This is
new, and it comes from reading the kiosk rather than from theory.
`apps/kiosk-web/src/app/identityFailure.ts` lists `invalid_grant` in
`REFUSED_CODES`, so a refused refresh classifies as `'refused'`, not
`'recoverable'`. `apps/kiosk-web/src/App.tsx`'s `AuthGate` then returns
`<NotAuthorizedScreen />` **unconditionally** — explicitly not gated on
`!auth.isAuthenticated`, because a screen the provider has shut out must stop
showing a wall it is no longer entitled to. The whole `RouterProvider`
unmounts, every WHEP tile with it, and a sibling effect calls
`auth.stopSilentRenew?.()`. There is no retry ladder on that path
(`e2e/kiosk-identity-refused.spec.ts`: *"does not retry, because retrying
cannot help"*). `automaticSilentRenew` fires *before* expiry, so the trigger is
the renewal attempt, not the token running out. So if refresh is blocked:

- the wall goes dark within one renewal interval, not one token lifetime; and
- it stays dark after the 15-minute lock lifts, until a person restarts the
  browser.

That is the shape of the finding if the severe branch is real, and it is worth
stating before the run rather than discovering it after.

---

## The researched hypothesis, formed before any live run

The task asked for a hypothesis from Keycloak's own semantics rather than a
purely empirical poke. Read at the Keycloak source for the 26.x line the
AppHost runs (`src/AppHost/AppHost.cs:144-145`, image `keycloak/keycloak`;
spec 207 verified 26.6.4 live):

| Question | Evidence | Answer |
|---|---|---|
| Does a temporary lockout set `enabled: false` on the user? | `DefaultBruteForceProtector.failedLogin` reaches `user.setEnabled(false)` only *after* `if (!realm.isPermanentLockout()) { return; }`. The realm sets `permanentLockout: false` (`:12`). | **No.** It writes `failedLoginNotBefore` on a separate `UserLoginFailureModel`. `UserRepresentation.enabled` stays `true`. The `disabled: true` spec 207 observed is the **attack-detection** record's own field, a different thing. |
| What does the refresh-token grant actually check? | `TokenManager.validateToken`: session validity (`AuthenticationManager.isSessionValid`), `user != null`, `user.isEnabled()`, `isIssuedBeforeSessionStart`, client-session presence, then `validateTokenReuseForRefresh`. | Five checks, **none of them brute force**. |
| Does anything on the refresh path consult the protector? | `grep -nE 'BruteForce\|isTemporarilyDisabled'` over `TokenManager.java` (1550 lines) and `RefreshTokenGrantType.java`: **zero hits**. | **No.** |
| Where *is* the check, then? | `AuthenticationProcessor:750` — `if (realm.isBruteForceProtected()) { … getBruteForceProtector().failedLogin(…) }`, i.e. inside the authentication flow, which the browser flow and the direct-grant flow run and the refresh grant does not. | **Only on a new authentication attempt.** |
| Does the offline branch differ? | Same method, same fall-through: the `offline` branch changes only how the `UserSessionModel` is found, then joins the identical `user.isEnabled()` path. | **No.** |

**Predicted answer: refresh is NOT blocked.** A currently-signed-in wall
display keeps renewing straight through a lockout, and the DoS denies only
*new* sign-ins.

**This prediction is confident, and it is still not the answer.** A source read
of a version line is not an observation of the server this repo boots, and this
repository has been wrong in exactly that direction before — the same spec 207
whose `spec.md` asserted `numFailures >= 10` while the running realm said `2`.
The live run is what settles it. What the prediction buys is a *stated*
expectation the run can contradict, which is the only thing that makes the
run's outcome evidence rather than a transcription.

**What would falsify it, and would be the severe finding:** the refresh POST
answering `400 invalid_grant` while the account's attack-detection record reads
`disabled: true` and a fresh password grant for the same account is refused in
the same window.

**What stays true on either branch.** The bounded case is not the harmless
case. `operator` and `admin` sign in through `management-web` with **no**
`offline_access`, and the realm sets no `ssoSession*` fields, so Keycloak's
defaults apply — a ten-hour `ssoSessionMaxLifespan`, confirmed live and
recorded in ADR-0131. An operator who is signed in survives the lock; an
operator arriving for a shift change cannot sign in at all while it is held,
and every signed-in operator is turned out at the ten-hour ceiling regardless.
That residual risk is real under the benign branch and is exactly what the
out-of-scope mitigations address.

---

## What this spec delivers, and what it deliberately does not

**It delivers an executable answer, not a fix.** Concretely:

1. an integration test that locks an account holding a live refresh token and
   records, against the real Keycloak, what `grant_type=refresh_token` does;
2. an e2e case that puts the same question to the thing the issue actually
   cares about — a wall display with an offline grant;
3. the finding written into `verification.md` and back into spec 207's open
   question, so the next reader meets the answer rather than the question;
4. under the severe branch, a filed follow-up carrying the evidence.

**It does not choose a remedy, on either branch, and that is a scope decision
rather than a shortfall.** Every candidate mitigation is a security-control
change: gateway rate limiting on the token endpoint (spec 207 §Out of scope
retracted the "speculative generality" framing but kept it out of scope),
closing ROPC on `management-web` (#2488, which also wants an ADR amendment),
raising `quickLoginCheckMilliSeconds` or `minimumQuickLoginWaitSeconds` (a
straight weakening of the control spec 207 just added), or exempting service
accounts. ADR-0144: *"It may not amend the constitution or write an ADR
autonomously … it implements decisions, it does not make them."* #2510 — the
sibling #2285 follow-up — is the established precedent: its body says the
trade-off is *"deliberately **not** something the autonomous lane decided, per
its own standing constraint against weakening/strengthening security controls
without a stated rationale a reviewer can weigh."*

So the gate at the end of this spec is **"investigated, answered, recorded,
human decides the remedy"** — on the benign branch because no remedy is owed by
this issue, and on the severe branch because the remedy is exactly the kind of
decision the lane may not take alone.

---

## Locked tech choices

Nothing new is chosen. No package, no bounded context, no runtime resource, and
**no production code**.

| Concern | Choice | Where |
|---|---|---|
| Identity provider | Keycloak 26.x, one realm per fab | ADR-0007; `src/AppHost/AppHost.cs:143-151` |
| Realm configuration | Declarative import, unchanged by this spec | `src/AppHost/Realms/smart-sentinel-eye-realm.json` |
| Integration testing | `AspireFixture` against the real stack, no Testcontainers | ADR-0103 |
| Lock mechanism used by the test | repeated `grant_type=password` at the public token endpoint | precedent: `tests/Integration.Tests/Identity/BruteForceLockoutIntegrationTests.cs` |
| Admin reads and cleanup | `identity-admin` service account via `RealmProbe.AuthorisedAdminClientAsync` | `tests/Integration.Tests/Identity/RealmProbe.cs` |
| Test framework | xUnit + Shouldly, hand-written helpers, no AutoFixture | ADR-0052, ADR-0054 |
| Browser-level proof | Playwright, persistent Chromium profile | precedent: `e2e/wall-survives-a-process-death.spec.ts` |
| Wall client and grant | `kiosk-wall`, `openid offline_access`, `localStorage` | ADR-0131; `apps/kiosk-web/src/app/auth.ts:51,110` |

---

## User stories

### US1 (P1) — a live session's refresh, put to the running server

**As** the engineer who has to say how bad #2509 is,
**I want** the real Keycloak asked, while an account is genuinely locked,
whether that account's refresh token still mints,
**so that** the severity of an open ROPC endpoint stops being a guess.

One vertical slice: one integration test file, one fixture, one observable
answer. It is buildable and shippable on its own — US2 refines the fidelity, it
does not unblock this — and nothing else in the repository has to move.

### US2 (P2) — the wall's own grant, through the browser it actually uses

**As** the operator of a fab floor,
**I want** the question asked of a real wall display holding a real offline
grant on `kiosk-wall`,
**so that** the answer is about the thing at risk and not only about the code
path it shares with `management-web`.

Separable from US1 on purpose. US1 proves the server's rule; US2 proves the
wall survives (or does not) end to end, which is the issue's own step 3. It
owns different files and runs in parallel (ADR-0109). **If US2 cannot be
executed in this pass, US1 still answers the issue** — say so rather than
reporting a colour nobody observed.

---

## Acceptance scenarios

All scenarios run against the real stack (ADR-0103). A test that read the realm
JSON would prove the configuration was written down, not that Keycloak obeys it
— this repository's own recorded lesson about guards that read the design
artefact.

### The observation — SC-1 is the reason this spec exists

**SC-1 — a locked account's refresh token is still spent (predicted), or is
not (the severe finding).**

```gherkin
Given a throwaway realm account with a known password
  And that account has just been issued an access token and a refresh token
 When the account is locked out by repeated wrong password guesses at the
      token endpoint
  And its attack-detection record reports it temporarily disabled
  And the token endpoint is then sent grant_type=refresh_token with that
      account's refresh token
 Then a new access token is issued, 200
```

The assertion is written for the **predicted** outcome. A red here is not a
defect to adjust away — it is the severe finding, reported as such, and under
ADR-0144 the lane may not edit the test to make it pass.

### The controls — each rules out a way SC-1 could be lying

**SC-2 (control on the lock) — the account really is locked at the moment the
refresh is attempted.**

```gherkin
Given SC-1's account has been given its wrong guesses
 When the token endpoint is sent that account's *correct* password
 Then it refuses with 400 and error "invalid_grant"
  And the attack-detection record for that account reports disabled == true
```

Without this, a successful refresh is indistinguishable from a lock that had
already expired, never tripped, or landed on a different user. It runs in the
same window as SC-1, against the same account.

**SC-3 (control on the token) — the refresh token was mintable a moment ago.**

```gherkin
Given a throwaway realm account newly created with a known password
 When the token endpoint is sent that account's correct password
 Then a token response is returned carrying both access_token and refresh_token
```

Runs *before* the wrong guesses. Without it, a refused refresh is
indistinguishable from a malformed request, a client that issues no refresh
token, or a broken stack.

**SC-4 (counterfactual) — the refresh assertion can actually fail.**

```gherkin
Given a throwaway account holding a valid refresh token
  And the account is disabled outright through the Keycloak Admin API
      (enabled: false — a different mechanism from a brute-force lockout)
 When the token endpoint is sent grant_type=refresh_token with that token
 Then it refuses with 400 and error "invalid_grant"
```

This is the load-bearing scenario of the whole spec, and it exists because of a
standing lesson: an assertion that cannot fail is not evidence. SC-1 asserts a
*success*, so on its own it would pass just as happily against a Keycloak that
ignores lockouts and one that ignores everything. SC-4 drives the same endpoint
with the same kind of token down `TokenManager.validateToken`'s
`if (!user.isEnabled())` branch — a refusal path that provably exists in the
source — and proves the instrument registers a refusal when there is one to
register. The account is re-enabled immediately afterwards; the throwaway user
is deleted regardless.

**SC-5 (blast-radius) — one account's lockout is one account's.**

```gherkin
Given SC-1's account is locked out
 When a different account authenticates with its correct password
 Then a token is issued
```

Also protects the rest of the suite: if this fails, this test is poisoning
every other integration test in the collection.

### Bad request

```gherkin
Given the realm has brute-force protection enabled
 When the token endpoint is sent grant_type=refresh_token with a syntactically
      invalid refresh token
 Then it refuses with error "invalid_grant"
  And no account's failure counter moves
```

A malformed refresh is not a failed login. Pinned so that a later tightening of
the realm cannot quietly start counting refresh failures against real accounts
— which would turn a renewing wall into its own attacker.

### Auth / scope

```gherkin
Given the attack-detection record and the enabled flag for an account
 When they are read or written by a caller holding no realm-management role
 Then the Admin API refuses
```

Not a new guarantee — Keycloak's own. Recorded because SC-2 and SC-4 reach the
Admin API through `identity-admin`, whose `manage-users` + `view-users` roles
(`smart-sentinel-eye-realm.json:582-591`) are what make them possible, so
narrowing that service account shows up here rather than as a mystery.

### US2 — the wall, end to end

**SC-6 — a running wall display keeps its wall while its account is locked.**

```gherkin
Given a wall display signed in as wall-munich on the kiosk-wall client,
      holding an offline grant
 When wall-munich is locked out at the token endpoint by wrong password guesses
  And the wall display's stored access token is expired in place, forcing a
      silent renewal
 Then the display renews on grant_type=refresh_token without a provider prompt
  And it is still showing its wall
  And no sign-in prompt appears anywhere on it
```

Under the severe branch this goes red by rendering `NotAuthorizedScreen`
instead of the layout — which is A3 observed rather than argued.

---

## Independent end-to-end test procedure

Runnable by a person with a booted stack and no knowledge of the tests. It uses
`operator`, not a wall account, so a mistake cannot leave a wall locked; the
mechanism is identical.

1. Boot the AppHost. Confirm the process start time is *after* the commit under
   test — a persistent stack keeps serving the binaries it booted with.
2. Mint a token pair for `operator` at Aspire's **proxied** Keycloak endpoint
   (not the container's mapped port, or the issuer will not match):
   `POST /realms/smart-sentinel-eye/protocol/openid-connect/token`,
   `grant_type=password&client_id=management-web&username=operator&password=Operator1234&scope=openid`.
   Keep `access_token` **and** `refresh_token`. Expect `200`.
3. Post the same form three times in a tight loop with
   `password=WrongPassword1..3`. Expect `400 invalid_grant` each time.
4. Post the **correct** password once more. Expect `400 invalid_grant` — the
   lock is on. (If this returns `200` the lock did not trip; repeat step 3
   faster and re-check before going on.)
5. Read the record with a master-realm admin token:
   `GET /admin/realms/smart-sentinel-eye/attack-detection/brute-force/users/{id}`.
   Expect `"disabled": true`.
6. **The observation.** Post
   `grant_type=refresh_token&client_id=management-web&refresh_token=<step 2>`.
   Record the status, the `error`, and whether an `access_token` came back.
   - `200` plus a new token ⇒ the benign branch; the prediction held.
   - `400 invalid_grant` ⇒ the severe branch; go to step 8.
7. Counterfactual, so step 6 means something:
   `PUT /admin/realms/{realm}/users/{id}` with `{"enabled": false}`, repeat
   step 6 with a freshly-minted refresh token, expect `400 invalid_grant`, then
   set `enabled` back to `true`.
8. Clean up: `DELETE /admin/realms/{realm}/attack-detection/brute-force/users/{id}`
   → `204`, then the correct password → `200`. Confirm `operator` is unlocked
   before leaving the stack.
9. (US2) With `VITE_KIOSK_MODE=wall`, sign a browser in at `:5175` as
   `wall-munich`, open a layout, lock `wall-munich` via steps 3–5, then expire
   the stored `expires_at` in place and watch. Expect the wall to stay up under
   the benign branch, and `NotAuthorizedScreen` under the severe one.

---

## Latency budget impact

**N/A.** Constitution §IV's six legs are untouched: nothing here sits between
event arrival and overlay render, no production code changes, and the realm is
not edited. The nearest neighbour is §Availability — a wall going dark is an
availability failure, not a latency one — and §VII's dashboard obligation does
not attach, because no leg is implemented or altered by this spec.

---

## Out of scope

| Not done here | Why |
|---|---|
| Gateway / token-endpoint rate limiting | The standard mitigation, and a real one (spec 207 retracted calling it speculative), but a different control at a different layer and its own design decision. Belongs to a human. |
| Closing ROPC on `management-web` | `AspireFixture.ClientId` is `management-web` and every `CreateAdminClientAsync` goes through its password grant. Removing that is a harness redesign, not a realm edit. Spec 207 §*The correction that decides this spec's scope*. |
| Closing ROPC on `smart-sentinel-eye-web` | #2488, which additionally wants the client deleted and ADR-0080 amended — an ADR amendment the lane may not make. |
| Tuning `quickLoginCheckMilliSeconds` / `minimumQuickLoginWaitSeconds` | Directly weakens the control spec 207 added. Human decision, with the DoS-versus-guessing trade-off in view. |
| Raising the password policy | #2510, explicitly marked as needing a human decision. |
| Exempting `wall-*` from brute-force protection | Keycloak has no per-user opt-out; the nearest equivalents (service accounts, a second realm) are architecture, not configuration. |
| Any production code change | This spec adds tests and records a finding. If the severe branch lands, the remedy is filed, not built. |

---

## Success criteria

- **SC-A.** The question in spec 207's "Open question, not resolved here" has a
  recorded answer, observed against a running Keycloak, with the request and
  the response quoted.
- **SC-B.** That answer is pinned by a test that runs in CI, so it cannot
  silently reverse — the point of writing a test rather than a note.
- **SC-C.** The instrument is proven able to fail (SC-4's counterfactual), not
  merely observed passing.
- **SC-D.** `specs/207-a-guess-that-runs-out/spec.md`'s open-question paragraph
  is amended in place to point at this spec's answer. A question recorded as
  open after it is answered is the same clerical defect as §IV recording a
  built leg as unbuilt.
- **SC-E.** On the severe branch only: a follow-up issue exists carrying the
  verbatim evidence and the A3 consequence, labelled for a human decision, and
  this delivery stops there.
- **SC-F.** No realm field, no production file, and no existing test assertion
  is changed to make anything pass.

## Assumptions, marked

- **G1 (guess).** The AppHost pulls `keycloak/keycloak` without a pinned tag
  (`AppHost.cs:145`), so the running version may have moved past the 26.6.4
  spec 207 verified. The source read above is against the 26.x line; if the
  live answer contradicts it, the live answer wins and the version is recorded
  in `verification.md`.
- **G2 (guess).** `management-web` carries no `offline_access` in
  `optionalClientScopes` (`:173-198` lists default scopes only), so US1's
  refresh token is an **online** one. The offline branch is covered by US2 and
  by the shared code path, not by US1.
- **G3 (assumption).** The delivery machine may not be able to boot Aspire in
  this pass; three deliveries in this session already deferred phase 5 to CI.
  `tasks.md` is written so CI can settle both US1 and US2 on its own.
