# Spec 327 — The admin token that needs no password

**Issue:** #2511 — "AspireFixture's ROPC dependency on management-web blocks closing #2285's second bullet"
**Branch:** `fix/2511-confidential-test-client` · **Worktree:** `D:\Github\sse-2511`
**Lane:** autonomous (ADR-0144). **The mechanism was decided by a human** — issue comment,
2026-10-08: *"add a dedicated confidential client_credentials test client mirroring the existing
identity-admin pattern, so the AspireFixture stops needing ROPC through management-web."* This
spec works out how to apply that decision and where it can reach. It does not re-open it.
**Predecessors:** spec 207 (#2285, brute-force lockout; its §Out of scope / T007 is the origin),
spec 200 (#2279, moved the fixture onto `management-web`), spec 325 (#2510, `SeededCredentials`).
**ADRs:** ADR-0007 / ADR-0008 (`docs/adr/0000-initial-decisions.md` rows 007–008, Keycloak
realm), ADR-0100 (service-account clients minted via `client_credentials`), ADR-0111 (a dev-only
confidential client in the shared realm: `scenario-simulator`), ADR-0159 (fab = group under
`/fabs`), ADR-0036 (smallest change), ADR-0037 (phases), ADR-0052/0053 (xUnit + Shouldly,
sentence names), ADR-0103 (Aspire fixture, no Testcontainers), ADR-0109 (`[P]` = disjoint files),
ADR-0139 / ADR-0144 (red first; lane limits).
**Constitution:** §VIII (safe by default at trust boundaries), §Testing (new behaviour starts red;
refactors stay green).
**New ADR needed:** no (plan §1).
**Latency budget:** N/A. Test harness and realm configuration only. No §IV leg is touched.

---

## Problem (re-verified at `19f4e2ee`, origin/develop)

`tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:12` pins `ClientId = "management-web"`.
`FetchAccessTokenAsync` (`:110-146`) always posts `grant_type=password`. Every authenticated
helper routes through it:

| Helper | Call sites (tests/, excl. the fixture) | What the caller wants |
|---|---|---|
| `CreateAdminClientAsync(resource)` | **370 in 84 files** | an API client with the console's full scope set, in fab `munich`. Who the caller is does not matter. |
| `GetAccessTokenAsync(AdminUsername, AdminPassword)` | **23 in 17 files** | the same admin token as a raw string (SignalR hubs, WHEP, device registration, sub lookups) |
| `CreateAuthenticatedClientAsync(resource, user, pw)` | **109 in 62 files** | a token for a *named persona*: `admin@munich.test`, `op-dresden@…`, `op-multi@…` (two fabs), `op-berlin@…`, `operator`, … |
| `GetAccessTokenAsync(user, pw)` (non-admin) | ~10 | the same, as a raw string |
| `GetAccessTokenForClientAsync(clientId, …)` | 16 | ROPC against *other* clients (webhook JWT clients, tile-span). These do not touch `management-web`. |

Turning off `directAccessGrantsEnabled` on `management-web` therefore fails the suite at its first
authenticated call. That is the blocker #2285's second bullet ran into.

### What this investigation established (it narrows the issue's framing)

1. **The "admin client" is an application-API caller, not a Keycloak admin.** None of
   the 370 + 23 admin sites calls the Keycloak Admin REST API. That work goes through
   `RealmProbe` (`tests/Integration.Tests/Identity/RealmProbe.cs`, `identity-admin` over
   `client_credentials`) or through master-realm `admin-cli`. So the new client needs **no
   `realm-management` roles**. It needs `management-web`'s default client scopes plus the `admin`
   user's fab group (`/fabs/munich`). It mirrors `identity-admin`'s *shape*: confidential,
   `serviceAccountsEnabled`, `client_credentials` only, a `dev-only-…` secret. It does **not**
   mirror `identity-admin`'s privileges. The closest existing precedent for the privilege set is
   `scenario-simulator`: a dev-only service account in `/fabs/munich` holding `sse.*` scopes.
2. **Nothing in `src/` reads anything a service-account token would lack.** There are zero hits for
   `RequireRole` / `IsInRole` / `realm_access` / `preferred_username`. The realm also emits no
   roles claim, because it defines its own `clientScopes` and has no `roles` scope. Identity comes
   from `sub` (`sse-identity`), permissions from `scope`, fab from `groups` (`sse-groups`), and
   audience from `aud` (`sse-audience`). A service account gets all four from the same client
   scopes. `azp` is read only to *refuse* (revoked clients, kiosk detection, webhook JWT
   matching), and a new client id trips none of those.
3. **`client_credentials` cannot produce a persona's token.** It yields exactly one principal per
   client: the service account. The 109 + ~10 persona sites test fab scoping, multi-fab membership
   and operator-versus-admin behaviour as *distinct identities*. One test client cannot stand in
   for them (§Out of scope, item 1).
4. **The lockout and password tests use the password grant by design.** They are
   `BruteForceLockout…`, `LockoutSessionSurvival…`, `LockoutThroughput…`, `PasswordPolicyEnforcement…`,
   `FabGroupClaim…`, `ConsoleScopeGrant…`, `e2e/wall-survives-a-lockout.spec.ts`, and
   `RunModeStackAddress.cs`. They post `grant_type=password` against `AspireFixture.ClientId`
   directly because a password guess *is* what they test. No `client_credentials` change can move
   them.

**Consequence, stated plainly:** this spec removes `management-web` ROPC from the **admin path**,
which is 393 of the ~520 fixture call sites. It does **not** by itself make it safe to flip
`management-web`'s `directAccessGrantsEnabled`. Items 3 and 4 need a different mechanism and a
new human decision (§Out of scope).

## Decision encoded by this spec

| Item | Value |
|---|---|
| New realm client | `integration-test-admin`: `publicClient: false`, `serviceAccountsEnabled: true`, `standardFlowEnabled: false`, `directAccessGrantsEnabled: false`, `secret: "dev-only-integration-test-admin-secret"` |
| Its `defaultClientScopes` | **set-equal to `management-web`'s**, kept equal by a build-enforced parity guard |
| Its service-account user | `service-account-integration-test-admin`, `groups` **equal to the `admin` user's** (`["/fabs/munich"]`), **no `clientRoles`** |
| Fixture admin path | `CreateAdminClientAsync` and a new `GetAdminAccessTokenAsync()` mint via `client_credentials` against `integration-test-admin`, sharing the existing token cache |
| Fixture persona path | unchanged: `CreateAuthenticatedClientAsync` / `GetAccessTokenAsync(user, pw)` keep ROPC via `management-web` |
| `management-web` | **unchanged**. `directAccessGrantsEnabled` stays `true`. |

**Admin is one identity.** Every admin accessor must present the same `sub`. Many tests mix
`CreateAdminClientAsync` with a raw admin token in one flow, and idempotency keys and resource
ownership are scoped by caller (ADR-0142, spec 302). Example:
`StaleIdempotencyReservationIntegrationTests.cs:476` seeds a reservation under the raw admin
token's `sub` and then replays through `CreateAdminClientAsync`. So the 19 admin
`GetAccessTokenAsync` sites move **together with** `CreateAdminClientAsync`, never separately
(plan §4).

## Out of scope (each needs its own issue; item 1 needs a human decision first)

1. **Persona tokens.** `CreateAuthenticatedClientAsync` and non-admin `GetAccessTokenAsync`, 109 +
   ~10 sites. The options carry different costs and none was decided:
   (a) one confidential service-account client per persona, about seven more realm clients;
   (b) one dedicated confidential *ROPC-enabled* test client, which still uses a password grant but
   not through the console's public client;
   (c) drive the browser login form.
   Filed as a follow-up at phase 7, `agent:blocked` until a human picks.
2. **Password-grant probes** (finding 4). Whichever option item 1 picks, the lockout tests need
   *some* client that accepts a password grant, or a form-login driver. They belong in the same
   follow-up.
3. **`TokenAttributionIntegrationTests` (3 facts) and `TokenAudienceIntegrationTests` fact 1.**
   They assert properties of *the console client's* token (spec 042 `sub`, spec 069 `aud`;
   fact 1's own message says *"the client every other integration test authenticates through"*).
   Moving them to the service account would quietly change what they prove. They stay on
   `management-web` and join item 1's inventory. Their failure messages that call
   `management-web` "the client every other integration test authenticates through" become
   untrue, and are reworded (plan §4, T010) with no assertion change.
4. **Flipping `management-web`'s `directAccessGrantsEnabled`.** That is #2285's second bullet, and
   it remains blocked by items 1 and 2.
5. **Excluding dev-only clients from a production realm.** `scenario-simulator`, the seeded human
   accounts and now `integration-test-admin` all live in the single realm import. There is no
   production deployment (CLAUDE.md, `deploy/helm/`). Whoever builds a production realm inherits
   this. It is noted here, not solved.
6. **`GetAccessTokenForClientAsync`** (16 sites, other clients' ROPC). It does not involve
   `management-web`.

---

## User stories

### US1 (P1, the whole slice): the admin harness authenticates as a service account

**As** the maintainer who will eventually turn off the console's password grant,
**I want** every admin token the integration harness mints to come from a confidential
`client_credentials` client, not from a password grant against `management-web`,
**so that** the admin path (most of the suite) no longer depends on `management-web` accepting
passwords, and the dependency that remains is a short, named inventory.

It ships independently: one realm client plus the fixture plus the call-site migration. It can be
observed end to end by decoding the token any admin test presents.

## Acceptance scenarios

File-level facts run in `Architecture.Tests` with no stack. Live facts run against the Aspire stack
(ADR-0103).

**AS-1: the admin token comes from the test client (happy path, red today).**
```gherkin
Given the Aspire stack with a freshly imported realm
 When a test obtains a client from AspireFixture.CreateAdminClientAsync
 Then the bearer token it carries has azp "integration-test-admin"
  And its groups claim contains "/fabs/munich"
  And its scope claim contains every token-scope management-web default-grants
  And its aud contains "smart-sentinel-eye-api"
  And its sub parses as a non-empty Guid
```

**AS-2: the realm declares the client the harness needs and nothing more (red today).**
```gherkin
Given src/AppHost/Realms/smart-sentinel-eye-realm.json
 Then exactly one client is named "integration-test-admin"
  And it is confidential, service-account-enabled, and has standard flow and direct access grants disabled
  And its defaultClientScopes equal management-web's, compared in both directions
  And a service-account user exists for it whose groups equal the "admin" user's groups
  And that service-account user holds no realm-management client roles
```

**AS-3: admin is one identity across accessors (conflict / consistency).**
```gherkin
Given the migrated fixture
 When a test reads sub from CreateAdminClientAsync's bearer token and from GetAdminAccessTokenAsync()
 Then the two subjects are equal
  And StaleIdempotencyReservationIntegrationTests (seed under one accessor, replay under the other) passes unmodified
```

**AS-4: a wrong secret is refused (bad request / auth).**
```gherkin
Given the running realm
 When client_credentials is requested for "integration-test-admin" with a wrong secret
 Then Keycloak answers 401 with error "unauthorized_client" or "invalid_client"
  And when a password grant is requested against "integration-test-admin" with the admin user's credentials
 Then Keycloak refuses it (direct access grants are disabled on this client)
```

**AS-5: the fixture's secret constant mirrors the realm (drift guard).**
```gherkin
Given AspireFixture's harness client id and secret constants
 Then the realm import seeds exactly one client of that id with exactly that secret
```
(This follows the `RealmImportMirrorTests` pattern from spec 248. Its red is observed by
counterfactual, plan §5.)

**AS-6: no regression.**
```gherkin
Given a fresh keycloak-data volume
 When the full Integration.Tests suite (all four shards) runs
 Then it is green with no assertion edited in any migrated test file
```

**Auth.** A token minted without the secret, or by password grant, is refused (AS-4). The new
client's privileges are bounded by a guard: scope parity with `management-web`, the `admin`
user's groups, and no `realm-management` roles (AS-2). It can do in the API exactly what the
seeded `admin` user, whose password is already in the repo, could do, and nothing in Keycloak's
admin surface.

## Independent end-to-end test procedure

1. `docker volume rm` the `keycloak-data` volume. A realm edit is not re-imported otherwise
   (`AppHost.cs:165-169`). Then boot the AppHost.
2. Run
   `curl -sk -d grant_type=client_credentials -d client_id=integration-test-admin -d client_secret=dev-only-integration-test-admin-secret https://<keycloak>/realms/smart-sentinel-eye/protocol/openid-connect/token`
   and decode the token. Expect `azp=integration-test-admin`, `groups=[/fabs/munich]`, the
   console's `sse.*` scopes, and `aud` containing `smart-sentinel-eye-api`.
3. `GET /cameras?fabId=munich` on `camera-catalog` with that token answers 200.
   `?fabId=dresden` answers 403.
4. Repeat step 2 with a wrong secret: 401.
5. Run the AS-1/AS-3/AS-4 class and the AS-2 architecture class, then the full suite. Quote the
   output in the PR.

## Assumptions (marked, to be checked at phase 4b/5)

- **A1 (checked by AS-6):** no migrated test depends on the admin principal being a *human* user,
  such as a Keycloak user lookup by `sub` or an assertion on `preferred_username`. The grep in
  finding 2 found no consumer. If the full run turns one up, that site moves to the persona path
  (`CreateAuthenticatedClientAsync(…, "admin", …)`) with a one-line reason. **Its assertion is
  never edited** (ADR-0144).
- **A2 (checked at 4b):** a realm-import `users[]` entry with `serviceAccountClientId` and
  `groups` puts the service account in that group. The realm already relies on this for
  `scenario-simulator` and `stream-distribution-attribution`, and AS-1's `groups` clause proves it
  live.
- **A3:** `client_credentials` access tokens get the realm's `accessTokenLifespan` (3600 s) like
  password-grant tokens. The fixture's existing cache with a one-minute safety margin covers
  either way.
