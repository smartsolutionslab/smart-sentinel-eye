# Spec 207 — A guess that runs out

**Issue:** #2285 — "The realm has no brute-force protection and password grants
on two public clients"
**Branch:** `2285-realm-brute-force-protection`
**Lane:** autonomous (ADR-0144)
**ADRs:** ADR-0007 (Keycloak per fab), ADR-0008 (two browser auth flows),
ADR-0080 (browser auth), ADR-0103 (integration tests against the Aspire
fixture, no Testcontainers), ADR-0139 (new behaviour starts red),
ADR-0144 (the autonomous lane)
**Constitution:** §VIII (safe by default at trust boundaries), NFR §Security
("token-bound, short-lived credentials"), §Testing (new behaviour, red first)

---

## Problem

`src/AppHost/Realms/smart-sentinel-eye-realm.json:11` says
`"bruteForceProtected": false`. Keycloak therefore counts nothing: an
attacker who can reach the realm may submit password guesses against a known
username at whatever rate the network allows, for as long as they like, and
nothing in the realm ever says stop. The usernames are not secret — `admin`,
`operator`, `op-<fab>@<fab>.test`, `wall-<fab>` are all in the realm import
itself — so the only thing standing between reach and a token is the password.

This is dev-only today, and the dev passwords are deliberately published
(`README.md`, nine `specs/*/quickstart.md`). That is not the point. The point
is that **the lockout control is realm configuration, and the realm import is
where realm configuration lives**, so the control has to exist here or it will
not exist anywhere. Filed by the 2026-09-13 whole-project security review.

### Premise check — the issue's three claims, re-verified at HEAD (`f75c00cb`)

The repository's standing lesson is that an issue's premise goes stale before
it is delivered. Two of the three claims hold; one is wrong, and one has had
its ground shifted underneath it by work that shipped after the issue was
filed.

| Issue claim | At HEAD | Verdict |
|---|---|---|
| `:11` `"bruteForceProtected": false` | `:11` `"bruteForceProtected": false` | **Holds**, line number included |
| `:130`/`:156` `directAccessGrantsEnabled: true` on the two public clients | `:131` (`smart-sentinel-eye-web`) and `:156` (`management-web`) | **Holds** as fact; **the remedy does not** — see below |
| password policy `length(8)`, no lockout | `:13` `"length(8) and upperCase(1) and lowerCase(1) and digits(1)"` | **Stale/wrong.** The policy has carried upper/lower/digit requirements since it was introduced — `git log -p` on the realm shows exactly one `+passwordPolicy` line, at this value. "No lockout" is the true half, and it is what this spec fixes. |

`kiosk-web` (`:200`) and `kiosk-wall` (`:229`) already have
`directAccessGrantsEnabled: false`, so the two named are genuinely the only
two.

### The correction that decides this spec's scope

The issue's "done looks like" makes disabling direct-access grants on
`smart-sentinel-eye-web` conditional on "the console-bundle issue" being
fixed. That work **has shipped** — #2279 / spec 200, PR #2489 — which is
exactly why the premise needs re-reading rather than acting on. Re-reading it
reverses the conclusion:

**1. ROPC on `management-web` is not dead capability. It is the test
harness.**

`tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:12` reads
`public const string ClientId = "management-web";`, and
`FetchAccessTokenAsync` (`:114-126`) posts `grant_type=password` with it.
Every `CreateAdminClientAsync` / `CreateAuthenticatedClientAsync` /
`GetAccessTokenAsync` call in the whole `Integration.Tests` suite goes through
that path, and `GetAccessTokenForClientAsync` names `management-web`
explicitly in `ConsoleScopeGrantIntegrationTests`,
`EventTypeRegistryAuthorizationIntegrationTests`,
`WebhookBearerValidationIntegrationTests`, `FabGroupClaimIntegrationTests` and
`AuditObservability/RunModeStackAddress.cs`.

Spec 200 *created* this dependency: it moved `AspireFixture.ClientId` off
`smart-sentinel-eye-web` and onto `management-web`. So ROPC on `management-web`
became load-bearing eleven days ago, after #2285 was filed.

The app code is clean — `apps/management-web/src/app/auth.ts` is
authorization-code + PKCE (`scope: 'openid'`, `redirect_uri`,
`onSigninCallback`) with no token-endpoint call anywhere, and no
`grant_type=password` appears under `apps/*/src`. The browser does not need
ROPC. **The integration suite does**, and flipping the flag would fail the
whole suite at the first `CreateAdminClientAsync`. Removing that dependency is
a harness redesign — a confidential fixture client, or minting through the
browser flow — not a one-line realm edit, and it does not belong in a spec
whose subject is a lockout counter.

**2. ROPC on `smart-sentinel-eye-web` is #2488's coupling, not a free win.**

Re-verified independently of #2488's own claim: at HEAD the only live-code
references to `smart-sentinel-eye-web` are the realm entry itself
(`:126`), one prose `description` on `management-web` (`:152`), five doc
comments, one negative `azp` example in
`tests/ServiceDefaults.Tests/BrowserKioskPrincipalTests.cs:28` (a string
literal, not a mint), and **four live password grants** in
`tests/Integration.Tests/Identity/ConsoleScopeGrantIntegrationTests.cs` —
`:53` and `:80`, which are SC-1 and SC-2 and mint from that client on purpose,
to prove #2279's defect stays dead. So #2488's "no consumer left" is right
about production and wrong about tests, in precisely the way #2488's own
"Coupling to handle" paragraph says.

Disabling ROPC there breaks SC-1 and SC-2. Fixing them *is* #2488's scope, and
#2488 additionally wants the client deleted and ADR-0080's stale sketch
amended — an ADR amendment the autonomous lane may not make (ADR-0144). #2488
is unlabelled, so a human has not released it.

**What staying open actually costs.** `management-web` (public client,
`directAccessGrantsEnabled: true`) carries 24 default client scopes
(`smart-sentinel-eye-realm.json:173-198`) covering nearly every write surface
in the system — cameras, streams, layouts, overlays, variables, rules,
events, webhooks, device/kiosk identity, audit. One guessed password on that
client yields a token with essentially all of it, scoped to the caller's fab.
This spec's lockout is the mitigation for exactly that: it does not close the
open door, but it does put a cap on how many times an attacker gets to try
the handle before the door locks for up to 15 minutes.

**Therefore: this spec is brute-force protection alone.** Both ROPC flags stay
`true` and are explicitly out of scope, with the reason recorded rather than
left to be rediscovered. See *Out of scope*.

---

## Locked tech choices

Nothing new is chosen. This feature adds no code, no package, no bounded
context and no runtime resource.

| Concern | Choice | Where |
|---|---|---|
| Identity provider | Keycloak 26.6.4, one realm per fab | ADR-0007; `src/AppHost/AppHost.cs:143-151` |
| Realm configuration | Declarative import, `WithRealmImport("../AppHost/Realms")` | `src/AppHost/AppHost.cs:151` |
| Lockout mechanism | Keycloak's own realm-level brute-force detector | built in; nothing hand-rolled |
| Integration testing | `AspireFixture` against the real stack, no Testcontainers | ADR-0103 |
| Admin-API access from tests | `identity-admin` service account via `RealmProbe` | `tests/Integration.Tests/Identity/RealmProbe.cs:55-56` |

---

## User stories

### US1 (P1, and the whole spec) — A repeated wrong guess stops being free

**As** the operator of a fab realm,
**I want** Keycloak to stop answering an account's password attempts once it
has refused too many in a row,
**so that** knowing a username is no longer enough to make unlimited,
full-speed guesses against it.

This is one vertical slice: one realm field block, one integration test that
watches the real Keycloak refuse, one verification run. It is buildable and
observable end to end on its own, and nothing else in the repository has to
move for it to ship.

**Configuration this slice locks in** (`src/AppHost/Realms/smart-sentinel-eye-realm.json`):

| Field | Value | Why this value |
|---|---|---|
| `bruteForceProtected` | `true` | the control itself |
| `permanentLockout` | `false` | a locked-out operator on a 24/7 fab floor must recover without an administrator; a temporary lock costs an attacker everything and costs a fat-fingered operator a minute |
| `failureFactor` | `10` | Keycloak's default is 30. Ten consecutive refusals is already an incident, and the first lock is only 60 s, so the operator cost is small while the attacker's cost compounds |
| `waitIncrementSeconds` | `60` | Keycloak default; each further lock doubles from here |
| `maxFailureWaitSeconds` | `900` | Keycloak default; caps the lock at 15 min, bounding how long any one lockout can last |
| `maxDeltaTimeSeconds` | `43200` | Keycloak default; the counter forgets after 12 h |
| `quickLoginCheckMilliSeconds` | `1000` | Keycloak default; two refusals inside a second is a script, not a person |
| `minimumQuickLoginWaitSeconds` | `60` | Keycloak default; what a script earns |

Only `failureFactor` departs from the Keycloak default, and only downward.
Every field is written out rather than left to import defaults: a security
control whose values live in a server's defaults is a control no reviewer of
this repository can read.

**A lockout is a real, deliberately-accepted trade-off, not a costless one.**
`quickLoginCheckMilliSeconds: 1000` means only **two** rapid wrong guesses —
not `failureFactor`'s ten — lock an account for up to `maxFailureWaitSeconds`
(15 min); verified live (`verification.md`, `numFailures: 2` at the moment of
lockout). An unauthenticated party that can reach `management-web`'s open
password-grant endpoint can therefore hold any named account — including the
unattended wall-display accounts (`wall-munich`, `wall-dresden`,
`wall-berlin`, `wall-hamburg`) and `operator`/`admin` — locked indefinitely
with two HTTP `POST`s per minute. **The alternative is worse**: no lockout at
all lets the same caller run unlimited full-speed password guesses instead of
merely denying service, so this spec still chooses the lockout — but "a
lockout is never an outage" overclaims, and the honest framing is that this
trades an unbounded credential-guessing risk for a bounded, cheap
denial-of-service one.

**Open question, not resolved here:** whether a locked account's *existing*
session survives via `grant_type=refresh_token` — i.e. whether the lockout
denies only new sign-ins or also revokes live sessions — was not checked
against the running server. Recorded as unresolved rather than assumed either
way.

**Gateway-level rate limiting is the standard mitigation for exactly this
risk, not a hypothetical future need.** The *Out of scope* table below
previously framed it as "speculative generality" (ADR-0036); that framing is
retracted. It stays out of scope for this spec regardless — it is a different
control at a different layer than a realm's brute-force detector, and adding
it is its own design decision — but the reason is scope, not that the risk it
would address is imaginary.

Keycloak 26's `bruteForceStrategy` and `maxTemporaryLockouts` are deliberately
**not** written. They are 25+/26-only fields, their defaults (`MULTIPLE`, `0`)
are already the behaviour asked for, and an import that names a field this
server version does not know can drop it in silence — the exact failure mode
§*Risks* in `plan.md` guards against. Fewer fields, fewer silent drops.

---

## Acceptance scenarios

All scenarios run against the real stack booted by `AspireFixture`
(ADR-0103), because the thing under test is a decision Keycloak makes at
runtime and not a string in a JSON file. The repository's own lesson applies:
a guard that reads the design artefact proves the design was written down, not
that it holds.

### The refusal — SC-1 is the reason this spec exists

**SC-1 — a correct password is refused after enough wrong ones.**

```gherkin
Given a realm account whose password is known to the test
  And the account has just authenticated successfully with that password
 When the token endpoint is sent that account's username with a wrong
      password, repeatedly, more times than the realm's failure factor allows
  And the token endpoint is then sent the account's *correct* password
 Then the token endpoint refuses, answering 400 with error "invalid_grant"
  And no access token is issued
```

Before the fix this fails: with `bruteForceProtected: false` the correct
password is accepted and a token comes back. That failure is the phase-4a red
(ADR-0139) and is quoted in the PR body.

### The controls — each rules out a specific way SC-1 could be lying

**SC-2 (control on the mint) — the same password worked a moment ago.**

```gherkin
Given a realm account newly created with a known password
 When the token endpoint is sent that account's correct password
 Then a token is issued
```

Without this, SC-1's refusal is indistinguishable from a wrong password, a
mistyped username, a bad client, or a broken stack. It runs *before* the wrong
guesses, in the same test, against the same account.

**SC-3 (control on the diagnosis) — Keycloak says it is a lockout.**

```gherkin
Given SC-1's account has just been refused its correct password
 When the realm's attack-detection record for that account is read through the
      Keycloak Admin API
 Then the record reports the account as temporarily disabled
  And the recorded failure count is at least one
```

Corrected: **not** "at least the realm's failure factor". Verified live
(`verification.md`) — `quickLoginCheckMilliSeconds: 1000` locks an account
after two failures inside one second, independently of `failureFactor`, so a
tight loop of wrong guesses trips that first and `numFailures` reads **2**,
below `failureFactor: 10`. `disabled == true` is the load-bearing claim;
`numFailures` is diagnostic, asserted only as `>= 1`.

This separates "locked out" from "the password changed", "the account was
disabled", "the client was rejected" — every other cause of `invalid_grant`.

**SC-4 (blast-radius control) — one account's lockout is one account's.**

```gherkin
Given SC-1's account is locked out
 When a *different* account authenticates with its correct password
 Then a token is issued
```

Brute-force protection is per-account, and this pins that, so a future change
that accidentally made the lock realm-wide would be caught here rather than as
a mystery outage. It also protects the rest of the suite: if this ever fails,
the lockout test is poisoning every other integration test in the collection.

**SC-5 (recovery) — the lock is temporary, and clearing it works.**

```gherkin
Given SC-1's account is locked out
 When the lockout is cleared through the Keycloak Admin API
 Then the account's correct password is accepted again
```

`permanentLockout: false` is a claim about recovery. This is the claim being
checked, and it is also the test's own cleanup path.

### Bad request

```gherkin
Given the realm has brute-force protection enabled
 When the token endpoint is sent a grant with no username at all
 Then it answers 401 with error "invalid_request", not a lockout
  And no account's failure counter moves
```

Corrected against the real, unmodified realm at Keycloak 26.6.4: this shape
answers `401`, not the RFC 6749 §5.2 `400` the `invalid_request` error name
would suggest. The load-bearing distinction is the `error` value itself —
`invalid_request` (a malformed request), never `invalid_grant` (what a
lockout answers with) — not the status code.

A malformed request is not a failed login. Keycloak already behaves this way;
this is pinned so that a later tightening of the realm cannot quietly start
counting parse errors against real accounts.

### Auth / scope

```gherkin
Given the attack-detection record for an account
 When it is read by a caller holding no realm-management role
 Then the Admin API refuses
```

Not a new guarantee — Keycloak's own — but the test reaches this endpoint
through `identity-admin`, whose `manage-users` + `view-users` roles
(`smart-sentinel-eye-realm.json:579-587`) are what make SC-3 and SC-5
possible. Recorded so that the next reader knows *why* SC-3 can read that
record, and so that narrowing `identity-admin` shows up here.

### The conflict case

There is no write-conflict surface. This feature adds no endpoint, no
aggregate, no `If-Match` version and no `Idempotency-Key`. The nearest thing
to a conflict is two tests locking the same account at once, which SC-4 and
the dedicated throwaway account in `plan.md` §3 exist to make impossible.

---

## Independent end-to-end test procedure

Reproducible by hand, without the test suite, by someone who does not trust
it. Assumes Docker and a clean checkout of the branch.

1. **Drop the Keycloak volume.** `docker volume ls` → remove the
   `keycloak-data` volume (and `docker rm` the persistent `keycloak`
   container). `WithRealmImport` runs only against a fresh volume
   (`src/AppHost/AppHost.cs:150-155`); skipping this gives a stack that looks
   perfectly healthy and is still running the old realm. **This step is the
   one that makes the rest mean anything.**
2. **Boot.** `aspire run` from `src/AppHost`, wait for `keycloak` healthy.
3. **Read the realm back, from the server and not from the file.** Mint an
   `identity-admin` token
   (`client_credentials`, secret `dev-only-identity-admin-secret`) and
   `GET /admin/realms/smart-sentinel-eye`. Confirm `bruteForceProtected` is
   `true` and `failureFactor` is `10` **in the response**. A field the import
   silently dropped is invisible in the file and visible here.
4. **Positive control.** `POST /realms/smart-sentinel-eye/protocol/openid-connect/token`
   with `grant_type=password&client_id=management-web&username=operator&password=Operator1234&scope=openid`.
   Expect `200` and an `access_token`. (Use a throwaway account in the
   scripted test — see `plan.md` §3 — but by hand on a scratch stack
   `operator` is fine, and step 7 unlocks it.)
5. **Guess.** Repeat the same POST eleven times with
   `password=wrong-on-purpose`. Expect `401`/`400` `invalid_grant` each time.
6. **The observation.** Repeat step 4 exactly — the *correct* password. On
   `develop` today this answers `200` with a token. On this branch it answers
   `invalid_grant` and no token. **That difference is the feature.**
7. **Diagnose and recover.** `GET /admin/realms/smart-sentinel-eye/attack-detection/brute-force/users/{id}`
   for that account — expect `disabled: true` and `numFailures >= 10`. Then
   `DELETE` the same path and repeat step 4: `200` again.
8. **Blast radius.** With the account still locked (redo 5), authenticate
   `admin` / `Admin1234`. Expect `200` — the lock is per-account.

**Negative control on the whole procedure:** run steps 1–6 against `develop`
with the volume dropped. Step 6 must answer `200`. If it does not, something
other than this change is refusing the token and the procedure is not
measuring what it claims.

---

## Latency budget

**N/A.** No leg of the event-to-overlay path is touched (constitution §IV).
This feature changes realm configuration; no request on the
camera → SFU → decode → presentation buffer → event → overlay → composite path
authenticates per frame, and no token is minted on that path.

The one adjacent measurement worth naming: `NFR001_JwtValidationLatencyTests`
measures JWT *validation*, which is signature checking against the realm JWKS
and is untouched by brute-force detection — the detector runs at the token
endpoint, not at the resource server. No change expected there, and it is
checked at phase 5 only as a regression guard, not as a budget claim.

---

## Out of scope — each with the reason and the successor

| Not doing | Why | Where it goes |
|---|---|---|
| `directAccessGrantsEnabled: false` on **`management-web`** | `AspireFixture.ClientId` is `management-web` and the whole `Integration.Tests` suite mints through `grant_type=password` on it (`AspireFixture.Auth.cs:12,121-126`). Flipping the flag fails every integration test. Removing the dependency is a harness redesign — a confidential fixture client, or a browser-flow mint — with its own design decision to make | **needs a new issue**; noted below |
| `directAccessGrantsEnabled: false` on **`smart-sentinel-eye-web`** | Four live password grants remain, at `ConsoleScopeGrantIntegrationTests.cs:53,80` (SC-1/SC-2 of spec 200), minting from this client deliberately to prove #2279 stays fixed. Reworking those facts is #2488's stated coupling, and #2488 also wants the client deleted and ADR-0080 amended | **#2488**, which is unlabelled and awaits a human |
| Strengthening the password policy | The issue mis-states it (it is not `length(8)`; it already requires upper, lower and a digit) and its "done looks like" does not ask for a change. Raising it — length 12, `specialChars(1)` — invalidates every seeded dev password in `README.md`, nine `specs/*/quickstart.md`, `AspireFixture.AdminPassword`, and each `e2e/` sign-in helper. That is a wide, purely-dev blast radius for a soft decision with real UX trade-offs, unlike a lockout counter, which has none | **deferred**; a separate issue for a human to weigh |
| Deleting `smart-sentinel-eye-web` | #2488's subject; needs the SC-1/SC-2 rework and an ADR amendment the lane may not make (ADR-0144) | **#2488** |
| Amending ADR-0080's stale code sketch | ADR work is forbidden to the autonomous lane (ADR-0144); also #2488 already names it | **#2488** / a human |
| Any production realm | There is no production deployment (ADR-0118, constitution §VII). This is the dev realm import, which is the only realm this repository has | — |
| The Keycloak **`master`** realm | `WithRealmImport` only imports `smart-sentinel-eye` (`AppHost.cs:151`) — `master`, created by `AddKeycloak`'s `adminPassword` and holding the bootstrap `admin`/`admin-cli` account used throughout this very spec's own verification, keeps Keycloak's own default `bruteForceProtected: false` untouched. This is a higher-value target than anything else in this spec's scope — full control of every realm on this server on success — and closing it needs its own decision: either a second, master-realm partial-import shape, or a startup Admin API call. Either is new architectural surface, not a configuration tweak this lane can make | **needs a new issue**; not filed by this PR — recorded here so it is not rediscovered |
| Rate-limiting at the gateway | A different control at a different layer than a realm's brute-force detector, and its own design decision. **Not** "speculative generality" (ADR-0036) — it is the standard mitigation for the quick-login-check DoS trade-off recorded in §US1 above, and the risk it would address is real, not hypothetical | — |

**Two issues this spec's findings should produce**, filed at phase 7 rather
than acted on here:

1. **Higher priority — the password-policy question**, stated with the
   correct current value (`length(8) and upperCase(1) and lowerCase(1) and
   digits(1)`, not the `length(8)` #2285 records) so the next reader does not
   repeat #2285's mis-transcription. This is the higher-priority successor to
   this PR: a `length(8)` policy is exhaustible against a top-1000 password
   list well within this lockout's own math (ten failures, or two under the
   quick-login check, before a 60 s–15 min wait), so the lockout alone is a
   speed bump, not a stop, against a competent guesser.
2. `management-web`'s ROPC dependency in `AspireFixture` — the harness redesign
   that must precede disabling direct-access grants on the console's client.
   Without it, #2285's second bullet can never be closed. Lower priority than
   (1): it gates removing a capability, not shrinking what a guessed password
   already grants.

---

## File contention

| File | This spec | Anyone else |
|---|---|---|
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | edits 8 lines in the realm header (before `:14`) | no open PR touches it (checked: the only open PR, #2501 / spec 206, is `2429-wrong-guid-audit-timeline`) |
| `tests/Integration.Tests/Identity/` | adds one new file | #2501 does not touch it |
| `tests/Integration.Tests/Identity/RealmProbe.cs` | **may** gain two helpers | shared file — see `plan.md` §3 for why the new test should prefer its own helpers over widening this one |

The realm header is edited **above** the `clients` array, so this change and
any client-level change (#2488, when released) do not overlap textually.

---

## Success criteria

| ID | Criterion | How it is known |
|---|---|---|
| SC-1 | After more consecutive failed password grants than `failureFactor`, the correct password is refused | integration test, observed **red** first (ADR-0139) |
| SC-2 | The same account's correct password is accepted before the failures | same test, positive control |
| SC-3 | Keycloak's attack-detection record names the account temporarily disabled (`disabled == true`); `numFailures` is reported but can be as low as 2 — see the quick-login-check trap in `plan.md` §4 — so it is not asserted `>= failureFactor` | integration test via `identity-admin` Admin API |
| SC-4 | A second account authenticates normally while the first is locked | integration test |
| SC-5 | Clearing the lockout restores authentication | integration test, and the test's cleanup |
| SC-6 | The running realm — not the file — reports `bruteForceProtected: true` and `failureFactor: 10` | `GET /admin/realms/smart-sentinel-eye`, at phase 5; guards a silently-dropped import field |
| SC-7 | The full `Integration.Tests` suite is green after the realm change, with the volume dropped | phase 5; the lockout test must not poison a suite that authenticates constantly |
| SC-8 | `e2e` sign-in flows still work | phase 5; the browser flows use the same accounts, and nothing in `e2e/` submits a deliberately wrong password to the real Keycloak (verified: every `invalid_grant` in `e2e/` and `apps/*/src` is a mocked route or a unit-test double) |

---

## Assumptions, marked

1. **Keycloak's brute-force detector counts user logins, not client
   authentications.** Service accounts authenticate by `client_credentials`
   with a secret; a wrong secret is a client error, not a user login failure,
   so the five service accounts (`identity-admin`, `migration-runner`,
   `event-ingestion`, `stream-distribution-attribution`,
   `scenario-simulator`, `system-variables-seeder`) should not be lockable this
   way. **Not verified against 26.6.4** — it is checked at phase 5 (SC-7
   covers it indirectly, since those accounts authenticate throughout the
   suite), and if it proves false the finding is an escalation, not an
   adjustment.
2. **`WithRealmImport` accepts these fields on 26.6.4.** All eight have
   existed in `RealmRepresentation` since Keycloak 8. SC-6 exists precisely
   because this assumption is checkable and worth checking rather than
   trusting.
3. **`failureFactor: 10` is a product decision, made here rather than asked.**
   The lane does not stop for a gate; the value is recorded with its reasoning
   above so a reviewer can overturn it in one line. Nothing else in the spec
   depends on the number — every test reads the realm's own value rather than
   hard-coding 10 (see `plan.md` §4).
4. **The dev realm's published passwords stay published.** That is the point
   of a dev realm, and changing it is the password-policy question this spec
   defers.
