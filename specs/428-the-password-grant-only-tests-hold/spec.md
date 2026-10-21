# Spec 428 — The password grant only tests hold

**Issue:** #2809 — "Persona tokens and password-grant probes still need management-web's ROPC"
**Branch:** `test/2809-ropc-test-client` · **Worktree:** `D:\Github\sse-2809` (cut from `develop` `6206ecf9`)
**Lane:** autonomous (ADR-0144). **The mechanism was decided by a human.** Owner comment on #2809
(2026-10-21) chose option **(b)**: *"one dedicated confidential test-only ROPC client in the realm,
used for persona tokens and password-grant probes, so management-web can drop
`directAccessGrantsEnabled`. ... Must carry the same guard as the other dev-only clients (see
#2878 ...). No ADR."* This spec works out how to apply that decision and how far it reaches. It
does not reopen the choice between (a), (b) and (c).
**Predecessors:** spec 327 (#2511), which built `integration-test-admin` for admin tokens and
filed this issue for the two populations it could not reach. Spec 207 (#2285), whose "done looks
like" asked for `directAccessGrantsEnabled: false` on `management-web`. Spec 286 (#2488), which
retired `smart-sentinel-eye-web`.
**ADRs:** `docs/adr/0000-initial-decisions.md` rows 007–008 (Keycloak, one realm), ADR-0080
(browser auth: the console signs in by authorization code + PKCE), ADR-0111 (dev-only confidential
client in the shared realm), ADR-0159 (fab = group under `/fabs`), ADR-0103 (Aspire fixture),
ADR-0108 (Playwright e2e), ADR-0139 / ADR-0144 (red first; lane limits), ADR-0109 (`[P]` = disjoint
files).
**Constitution:** §VIII (safe by default at trust boundaries), §Testing (new behaviour starts red).
**New ADR needed:** no. The owner ruled this out, and nothing here decides anything an ADR covers.
The new client follows the shape `integration-test-admin` established in spec 327. That spec needed
no ADR either.
**Latency budget:** N/A. This touches realm configuration and test code only. No §IV leg is
involved.

---

## Problem (re-verified at `6206ecf9`)

### Premise check: still true, and nothing has landed

- `src/AppHost/Realms/smart-sentinel-eye-realm.json:132`: `management-web` still has
  `"directAccessGrantsEnabled": true`. It is the **only** client in the application realm with
  that flag set. `kiosk-web`, `kiosk-wall` and all six confidential clients set it to `false`.
- No client in the realm is named like a persona/ROPC test client. No open PR or unmerged branch
  touches it. `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:21` still has
  `ClientId = "management-web"`, and `FetchAccessTokenAsync` (`:169-187`) still posts
  `grant_type=password` against it.

### Site counts (the issue's figures were unverified; these replace them)

| Population | Issue said | Measured | Edits needed |
|---|---|---|---|
| `CreateAuthenticatedClientAsync(resource, user, pw)` | 109 | **111 calls in 60 files** (excl. the fixture and `///`) | **none**: each one routes through the fixture's single `FetchAccessTokenAsync` |
| `GetAccessTokenAsync(user, pw)`, non-admin | ~10 | **11 calls in 5 files** (TokenAttribution 3, TokenAudience 1, OverlayFrameFabScoping 1, WhepAuth 2, ResolvedTextReachesItsFab 4) | **none**: same single path |
| `GetAccessTokenForClientAsync("management-web" / `AspireFixture.ClientId`, …)` | (not counted) | **4 calls in 3 files** (ConsoleScopeGrant 2, EventTypeRegistryAuthorization 1, FabGroupClaim 1). The other 15 calls go to webhook or runtime-planted clients and are untouched. | client id plus secret at each site |
| Raw `grant_type=password` / `refresh_token` POSTs against `management-web` | "8 files" | **11 sites in 6 files**: BruteForceLockout 2, FabGroupClaim 1, LockoutSessionSurvival 2 (one is `refresh_token`), LockoutThroughputMeasurement 3, PasswordPolicyEnforcement 1, ConsoleScopeGrant 1 (`RawPasswordGrantAsync("management-web", …)` SC-9 positive control), plus `RunModeStackAddress.cs:37/107` (1) | client id plus `client_secret` |
| e2e | `wall-survives-a-lockout.spec.ts` | **2 POSTs, one const** (`MANAGEMENT_CLIENT_ID`, `:82/171/187`). The other e2e password grants use master-realm `admin-cli` and are out of scope. | const plus `client_secret` |

The persona population is large but cheap: **111 + 11 sites move by changing one method**. The
work that has to be done by hand is the **20 raw and explicit sites in 10 files** (8 integration
files, `RunModeStackAddress.cs`, and the e2e spec). The issue's "8 files" is right if
EventTypeRegistryAuthorization and ConsoleScopeGrant are counted and TokenAudience (a message
string only) is not.

### Does anything legitimate need `management-web`'s password grant? No.

- The console signs in through the real browser login (authorization code + PKCE, ADR-0080).
  `apps/management-web/src` and `apps/shared/src` contain no `grant_type`. The shared e2e sign-in
  (`e2e/support/sign-in.ts`, ADR-0108) fills Keycloak's real login form. `scripts/` and `ci.yml`
  mint no password grant against it.
- `src/` never names `management-web` as a token client.
- So dropping the flag affects **only** the test sites above. That is in scope, and it is the
  goal of the issue (US2).

## User stories

### US1 (P1): Persona tokens and password-grant probes mint through a dedicated test client

As the integration and e2e harness, I mint a persona's token, and drive the lockout and policy
probes, through a confidential `integration-test-persona` client. It exists for tests, has a
`dev-only-` secret, and grants exactly the console's scopes. `management-web`'s password grant
then has no test callers left.

**Acceptance scenarios**

```gherkin
Scenario: AS-1 persona token comes from the test client (happy)
  Given the realm import seeds a confidential client "integration-test-persona"
  When the fixture mints a token for "op-dresden@…" with that persona's password
  Then the token's azp is "integration-test-persona"
  And its sub is that user's sub and its groups name that user's fab(s) only
  And its scopes are the console's scopes (set-equal to management-web's defaultClientScopes)

Scenario: AS-2 wrong client secret (auth)
  When a password grant names "integration-test-persona" with a wrong client_secret
  Then Keycloak answers 401 unauthorized_client

Scenario: AS-3 the test client is not a service account (auth)
  When a client_credentials grant names "integration-test-persona" with its own secret
  Then Keycloak refuses it (401 unauthorized_client): the client grants a token for a named user only

Scenario: AS-4 lockout still locks (conflict: state the probe exists to change)
  Given a user and the realm's failureFactor
  When failureFactor+1 wrong-password grants are made through "integration-test-persona"
  Then the correct password is refused with invalid_grant
  (brute-force detection is per user, not per client, so the existing lockout suites keep their meaning)
```

### US2 (P1, depends on US1): The console's public client stops accepting a password grant

As a fab operator's console, `management-web` accepts sign-in **only** through the browser flow.
A password POSTed straight to the token endpoint for `management-web` is refused. The application
realm has exactly one client that accepts a password grant, and it is the dev-only test client.

```gherkin
Scenario: AS-5 password grant against the console is refused (auth)
  When a password grant names "management-web" with a correct operator password
  Then Keycloak answers 400 with error "unauthorized_client"

Scenario: AS-6 the browser login still works (happy, regression)
  When the e2e harness signs in through the real login form at :5173
  Then the console holds a token whose azp is "management-web" (e2e/management-identity.spec.ts, unchanged)

Scenario: AS-7 exactly one ROPC client, and it is dev-only (guard, static)
  Then in the application realm import exactly one client has directAccessGrantsEnabled true
  And that client is confidential, has no service account, no browser flow, no redirect URIs
  And its secret starts with "dev-only-"
```

## Independent end-to-end test procedure

1. `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~PasswordGrantClientTests`. All
   facts pass (AS-7, plus the shape and description-length facts).
2. Boot the fixture with a **fresh** Keycloak volume (the realm import skips an existing realm) and
   run `PersonaClientIntegrationTests` (AS-1, AS-2, AS-3, AS-5) and the migrated suites:
   `BruteForceLockout`, `LockoutSessionSurvival`, `LockoutThroughputMeasurement`,
   `PasswordPolicyEnforcement`, `FabGroupClaim`, `ConsoleScopeGrant`,
   `EventTypeRegistryAuthorization`, `TokenAudience`, `TokenAttribution`. All pass.
3. Run e2e `wall-survives-a-lockout.spec.ts` and `management-identity.spec.ts`. Both pass (AS-4
   e2e, AS-6).
4. By hand against the booted stack:
   `curl -k -d grant_type=password -d client_id=management-web -d username=operator -d password=… …/token`
   answers `400 {"error":"unauthorized_client",…}`.

## Locked choices

- One client, `integration-test-persona`. Mechanism (b) only; (a) and (c) are rejected by the owner.
- Confidential, password grant only, `defaultClientScopes` set-equal to `management-web`'s (same
  rule as `integration-test-admin`, so a persona never sees a permission the console lacks). Secret
  `dev-only-integration-test-persona-secret`, **not** an AppHost parameter: no service reads it,
  so it has no production-reachable default.
- `AspireFixture.ClientId` is renamed `ConsoleClientId`, so the compiler lists every site still
  pointing at the console. `PersonaClientId` and `PersonaClientSecret` are added beside
  `HarnessClientId`.

## Out of scope

1. **A production realm import that excludes dev-only clients.** That is #2878 (and #1015). No
   production realm import exists, so today *every* dev-only client (`integration-test-admin`,
   `scenario-simulator`, …) is in the one realm file. This spec gives the new client the guards
   those clients carry (plan §4). It does not invent a mechanism #2878 has yet to decide.
   **Net exposure goes down, not up:** today anyone with a user's password can mint a
   full-console-scope token through `management-web`, a public client that needs no secret. After
   this spec, the caller also needs a confidential client's secret.
2. The master realm's `admin-cli` password grant (e2e and the lockout suites' admin reads). That
   is a different realm and Keycloak's bootstrap admin.
3. The throwaway public password-grant clients that `ConsoleScopeGrant` (SC-1/SC-2) and
   `EventTypeRegistryAuthorization` plant at runtime and delete afterwards. The static guard
   cannot see them, and they do not involve `management-web`.

## Assumptions (marked, to be confirmed by phase 4a's observed output)

- **A1.** Keycloak answers a password grant on a client with `directAccessGrantsEnabled: false`
  with **400 `unauthorized_client`**, and a `client_credentials` grant on a client with no service
  account with **401 `unauthorized_client`** (observed in CI: "Client not enabled to retrieve service account"). 4a's red run against the current realm shows the first as 200. The
  error code is confirmed at 4b's green.
- **A2.** `LockoutSessionSurvival`'s `refresh_token` POST needs `client_secret` once the client
  is confidential. Keycloak authenticates confidential clients on every grant.
- **A3.** There is a loss of live coverage, accepted with a stated replacement. The facts that
  read *management-web's own minted token* (ConsoleScopeGrant SC-3/SC-4, TokenAudience) can no
  longer mint it by password. Afterwards they read the persona client's token, whose scopes are
  statically guarded as set-equal to the console's. The console's real token is still observed
  live by `e2e/management-identity.spec.ts` (azp, scopes, no legacy bundle).
