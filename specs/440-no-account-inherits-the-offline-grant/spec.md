# Spec 440 — No account inherits the offline grant

**Issue:** #1995 · **ADRs:** **ADR-0181** (governing for the narrowing; Accepted 2026-10-22, supersedes ADR-0134,
corrects ADR-0132), **ADR-0186** (governing for the credential and the confinement; Accepted by the owner on PR #3106,
2026-10-23; amends ADR-0181 Decision 2), ADR-0067 (MigrationRunner), ADR-0131 (the wall keeps its own grant), ADR-0132,
ADR-0133, ADR-0130 (no production deployment), ADR-0143 (retry safety), ADR-0144 (lane), ADR-0139 (red first),
ADR-0105 (guards), ADR-0103 (Aspire fixture, no Testcontainers)
**Built on:** spec 052 (`kiosk-wall`, per-account strip), spec 080 (measurements — branch
`fix/1995-offline-access-is-granted-not-inherited`, `ede331a6`; not merged, superseded by this spec), spec 019
(MigrationRunner reads the realm), spec 132 (strip reports whether it changed anything), spec 128 (a failed migration
is loud), spec 188 (overridable parameters), spec 254 (realm-import mirror tests)
**Lane:** supervised for this revision (phase 1-3 gate; owner confirms before phase 4) · **Phase-4a colour:** **red**
(behaviour-changing), with named characterisation and comment-only sub-sets (§8a)
**Security-sensitive:** yes — a realm-wide authority (`manage-realm`) is granted permanently to a service account, and
the live Identity credential's client authority moves from a role to an FGAP policy. `security-reviewer` at phase 6 is
**mandatory** (ADR-0181 §6, ADR-0186 Implementation Notes); the slice is not mergeable without it.
**Latency budget (constitution §IV):** **N/A** — a boot-time step in a one-shot worker that completes before any
API starts, plus realm-file authorisation configuration. No leg of the event→overlay path is touched.

## 0. Revision 2 (2026-10-23) — what changed and why

Revision 1 (commit `96751aed`) specified ADR-0181 alone and was built in `8f2c4d32` (red), `68c9a641` (fix),
`96f5b3ad` (prose). The mandatory phase-6 security review of that build found that `identity-admin` — the live,
gateway-reachable Identity credential — holds `manage-clients`, which lets it **read** migration-runner's client secret
and mint a `manage-realm` token (two hops), and also **rewrite** migration-runner's credential configuration (measured,
ADR-0186 Context). The owner ruled on 2026-10-23 and ADR-0186 records it:

| ADR-0186 | What it adds to this spec |
|---|---|
| Decision 1 | migration-runner authenticates by **signed JWT** (`client-jwt`); only the public key is in the realm file; its former secret stops working |
| Decision 2 | the assertion: RS256, `iss`=`sub`=client id, `aud`= realm token endpoint, fresh `jti` per request, lifetime ≤ 60 s, never reused |
| Decision 3 | dev-only key pair: public key in the realm file, private key via an AppHost parameter with a dev-only default; #2878 gains both as a coupled pair |
| Decision 4 | rotation = realm-file edit + worker-config edit in one change; exactly one current public key |
| Decision 5 | `identity-admin` loses `manage-clients` + `view-clients`; gains an FGAP v2 all-clients `view`+`manage` grant; a migration-runner-only permission with a **NEGATIVE** policy denies it that one client; migration-runner's client `id` is **pinned** |
| Decision 6 | a realm-file guard fails when `identity-admin` holds `manage-clients`/`view-clients` directly, via a group or via a composite, and checks the deny's shape — **proven by counterfactual** |

Plus the review items ADR-0186 Implementation Notes §5 carried into scope (FR-004 rewording, two stale comments, the
architecture test's group/composite coverage, `KioskInheritedPrivilegeIntegrationTests`' class comment and message, a
README line, reading the default role's name from the realm, the #2878 comment).

**Everything ADR-0181 decided stands unchanged** and so does everything built for it, except where §5 marks a
requirement *revised*. The task-level map of what stays and what changes is `tasks.md` §A.

## 1. The decisions, as given

### 1a. ADR-0181 (unchanged)

1. `offline_access` is removed from the realm's default composite, so no account inherits it by existing. The four
   `wall-*` accounts keep it as a **named** grant.
2. `realm-management/manage-realm` is granted to `service-account-migration-runner` **permanently**, to nothing else.
3. The narrowing is an `IMigrator` in `MigrationRunner`, **registered first**, run on **every** boot, idempotent; a
   failed narrowing **fails the migrator and therefore the boot**.
4. The realm file does **not** declare the composite, and must not.
5. Enrolment's per-account strip (`StripInheritedRealmRolesAsync`) stays.

### 1b. ADR-0186 (new in revision 2)

Decisions 1-6 as tabulated in §0. Its *Implementation Notes* item 6 (open questions: deny over the system clients;
further `manage-users` routes over migration-runner's service-account user; other `manage-clients` holders; ADR-0160
/ enrolment interplay) are **not ruled** and are **out of scope** here; behaviour stays as it is until the owner rules.
Phase 6's security review answers the last one as a review finding, not as scope.

The lane implements ADR-0181's and ADR-0186's Implementation Notes and does not edit either ADR.

## 2. Premise, re-verified

### 2a. Revision 1's premises (at `29968a0d`) — still hold

| Claim | Observed at HEAD |
|---|---|
| The realm file does not declare `default-roles-*` nor `defaultRole` | `grep default-roles src/AppHost/Realms/smart-sentinel-eye-realm.json` → no match |
| The four wall grants are already explicit | realm file L432/454/476/498: `"realmRoles": ["user", "offline_access"]` on `wall-munich/-dresden/-berlin/-hamburg` |
| Migrators run in **registration order**, from a scope, and any throw fails the run | `MigrationRun.cs:46-70` (`GetServices<IMigrator>()`, unfiltered catch → `Failed`) |
| `IKeycloakAdminClient` is scoped, so a migrator depending on it is scoped | precedent `EventPartitionRolloverMigrator`, `Program.cs` (`AddScoped`) |
| The test class is already sharded | `tests/Integration.Tests/ci-shards/shard-2.filter` contains `KioskInheritedPrivilegeIntegrationTests.` — new facts inside it need no filter edit; a **new** integration class does |

(The rows about the realm-file description, the `manage-realm` grant, the migrator and the ten implementers were
discharged by `68c9a641`/`96f5b3ad` and are not repeated.)

### 2b. Revision 2's premises, verified on `fix/1995-narrow-default-roles` at `96f5b3ad` (2026-10-23)

| Claim | Observed |
|---|---|
| migration-runner still authenticates by client secret | realm file L331 `"secret": "dev-only-migration-runner-secret"`; no `clientAuthenticatorType` on the client; no client in the file carries a pinned `"id"` |
| `identity-admin` holds `manage-clients` and `view-clients` | realm file L631-644 (`service-account-identity-admin`: `manage-clients, manage-users, view-clients, view-users, query-clients, query-users, query-groups`) |
| No FGAP configuration exists | `grep -n "adminPermissionsEnabled\|authorizationSettings\|admin-permissions"` on the realm file → no match |
| The Keycloak image is the version ADR-0186 was measured on | `AppHost.cs:135` `.WithImageTag("26.6.4")` |
| How MigrationRunner obtains its admin token | `Program.cs` `AddKeycloakAdminClient()` → `IdentityInfrastructureModule.AddKeycloakAdminClient` (L267) → `KeycloakAdminTokenProvider` (`Credentials()` builds `ClientCredentials(BaseUrl, Realm, AdminClientId, AdminClientSecret)`) → shared `ServiceDefaults/Authentication/ClientCredentialsTokenProvider.MintAsync`, which always posts `client_secret` |
| That token client retries POSTs | `IdentityInfrastructureModule.cs:290-291` `AddHttpClient(KeycloakAdminTokenProvider.HttpClientName, …).RetryEveryMethod()` — reason stated: "a second token supersedes the first" |
| AppHost wires the secret | `AppHost.cs:35` `AddOverridableParameter("MigrationRunnerClientSecret", "dev-only-migration-runner-secret", secret: true)`; `AppHost.cs:334-335` `Keycloak__AdminClientId=migration-runner`, `Keycloak__AdminClientSecret=<that>` |
| Stale "read-only" comment, AppHost | `AppHost.cs:30-34` ("Its own client, read-only, holding query-groups + view-users …") |
| Stale "read-only" comment, Identity | `IdentityInfrastructureModule.cs:260-262` ("presents the narrower `migration-runner` credential, which holds `query-groups` and `view-users` — read-only") |
| The adapter assumes the default role's name | `HttpKeycloakAdminClient.Realm.cs:85` `…/roles/default-roles-{realm}/composites` |
| The 403 verdict advises the volume drop unconditionally | `RealmDefaultPrivilegeMigrator.cs` (`68c9a641`): "…drop the keycloak-data volume so the realm file is imported afresh (ADR-0181)." |
| The architecture guard sees direct user mappings only | `tests/Architecture.Tests/MigrationRunnerClientTests.cs` `No_account_other_than_the_migration_runner_holds_manage_realm` reads `users[].clientRoles["realm-management"]` only — no groups, no composites |
| `KioskInheritedPrivilegeIntegrationTests`' class comment and first message are now false | class comment L9-27 ("The realm hands every account created after import a default privilege that includes `offline_access`"); `A_kiosk_enrolled_at_runtime…` message "a kiosk is born holding the realm's default privilege, and enrolment must take it back" |
| The strip's request sequence has Docker-free coverage | `tests/Identity.Infrastructure.Tests/KeycloakAdmin/KeycloakRequestSequenceCharacterisationTests.cs` |
| Surfaces that name the migration-runner secret and must move with it | `AppHostParameterOverrideTests.cs:32-33,52,73,192-204`; `AppHostMigrationGateTests.cs:27` (doc); `AppHostStackStatusTests.cs:124`; `RealmImportMirrorTests.ProductionDefaults.cs:33`; `OverridableParameters.cs:32-35` ("six `*ClientSecret` values"); `KioskInheritedPrivilegeIntegrationTests.cs:184`; five `tests/Integration.Tests/AppHostComposition/*.snapshot.txt` (`resource MigrationRunnerClientSecret`, `env Keycloak__AdminClientSecret=…`) |
| `System.IdentityModel.Tokens.Jwt` is available without a new package | transitive through `Microsoft.AspNetCore.Authentication.JwtBearer` (`ServiceDefaults.csproj:42`); `System.IdentityModel.Tokens.Jwt.dll` and `Microsoft.IdentityModel.Tokens.dll` are in `src/MigrationRunner/bin/Release/net10.0` |
| README already has a general realm-edit line | `README.md:65-70` |
| ADR-0186 is on `develop` | **No** — PR #3106 is **OPEN** (accepted by the owner, not merged). See §7. |
| Nothing on `origin/develop` since `29968a0d` touches the contention files | `git log 29968a0d..origin/develop -- src/AppHost/Realms src/AppHost/AppHost.cs src/MigrationRunner src/Identity/Infrastructure/KeycloakAdmin src/ServiceDefaults/Authentication tests/Integration.Tests/AppHostComposition` → empty (2026-10-23) |

**The measurements are spec 080's** (ADR-0181) and **ADR-0186's spike** (Keycloak 26.6.4, fresh imports, five
containers); they are not re-run here. ADR-0186 lists what the spike did **not** verify — the real C# adapter and the
Identity integration suite under the narrowed role set, and FGAP v2 across Keycloak upgrades. The first is an
obligation of this spec (FR-016); the second is a standing caveat (§7).

## 3. User stories

### US1 (P1) — An account nobody in this system created does not inherit the offline grant

As the fab's security owner, I want an account created by hand in Keycloak (console, admin API, any route this
system does not drive) to hold no `offline_access`, so that a never-expiring credential can only be minted by an
account somebody deliberately named.

### US2 (P1, same slice) — Wall displays still hold it, by name

As a wall display, I keep `offline_access` so an unattended reboot recovers without a sign-in (ADR-0131); the grant is
visible in the realm file, and a fifth holder is a reviewed edit.

### US3 (P1, same slice; new in revision 2) — The live Identity credential cannot reach the realm-wide authority

As the fab's security owner, I want the credential the gateway-reachable Identity API holds (`identity-admin`) to be
unable to obtain a `manage-realm` token by any route — reading migration-runner's credential, rewriting it, or seeing
the client at all — so that ADR-0181's confinement of `manage-realm` to a one-shot worker is true as built.

**One slice.** None of the three is shippable alone. The grant without the migrator changes nothing; the migrator
without the grant fails every boot; and the grant without US3 hands realm-wide authority to the live API's credential
two hops away — a regression the security review has already blocked once. ADR-0186 Implementation Notes §7 makes the
same call: delivered together on `fix/1995-narrow-default-roles`, because the realm file is a contention file and the
reconfigure test is only green with all of it in.

### Out of scope

- Removing `offline_access` from the wall accounts; #1976 (device credentials). Owner ruling, 2026-09-05.
- `offlineSessionIdleTimeout` / `offlineSessionMaxLifespanEnabled` (vendor defaults; ADR-0181 Neutral).
- Narrowing anything else in the composite (`uma_authorization`, `account` roles stay).
- The production realm overlay and templated secrets (#2878 / #1015) — this spec only **comments** on #2878 (FR-008).
- ADR-0186 Implementation Notes item 6, all four open questions (deny over system clients, further `manage-users`
  routes, other `manage-clients` holders, ADR-0160/enrolment interplay). Unchanged until the owner rules.
- Switching any **other** service account to signed JWT. ADR-0186 confines the second credential kind to one account.
- A JWKS URL published by the worker (`use.jwks.url`); ADR-0186 Alternatives prefers `jwks.string`.
- Editing ADR-0132/0134/0181/0186 or merged specs (historical records).
- The mojibake in the `migration-runner` client's `name` (`â€”`, L324) — still noted, still not fixed.

## 4. Acceptance scenarios

AS-1 … AS-9 are revision 1's and are unchanged except AS-5 (now over the new credential) and AS-8 (wording). AS-10 …
AS-19 are new.

```gherkin
# AS-1 Happy path — a hand-made account inherits nothing privileged
Given the realm was imported fresh and MigrationRunner has completed
 When a person account is created directly through the Admin API
 Then its effective realm roles do not include "offline_access"
  And they include "user" and "uma_authorization"
 When a confidential client with a service account is created directly through the Admin API
 Then its service account's effective realm roles do not include "offline_access"

# AS-2 Safety-critical direction — every wall keeps it
Given the realm was imported fresh and narrowed
 When the effective realm roles of each of wall-munich, wall-dresden, wall-berlin, wall-hamburg are read
 Then each includes "offline_access"

# AS-3 Observable end behaviour — a wall still mints an offline token
Given the stack is booted
 When wall-munich signs in at kiosk-wall requesting "openid offline_access"
 Then the stored refresh token has typ "Offline" and no exp
# (e2e/wall-outlives-its-session.spec.ts — passes UNMODIFIED)

# AS-4 Anti-decoy — the composite we narrowed is the one in force
Given the realm was imported fresh
 When the realm representation and its realm roles are read
 Then realm.defaultRole.name is exactly "default-roles-smart-sentinel-eye"
  And no realm role named "default-roles-smart-sentinel-eye-1" exists
  And the composites of "default-roles-smart-sentinel-eye" do not include "offline_access"

# AS-5 Conflict / re-run — idempotent on every boot  (revised: over the signed-JWT credential)
Given the composite no longer contains "offline_access"
 When the narrowing runs again as migration-runner, authenticated by a signed client assertion
 Then it succeeds
  And the composite's members are unchanged by the second run

# AS-6 Bad request / boot integrity — the edited realm file imports
Given the realm file after this change
 When the Aspire fixture imports it into a fresh Keycloak
 Then the import finishes and the fixture boots
  And the migration-runner client description is at most 255 characters, does not say "Read-only",
      and says it authenticates by signed JWT and holds manage-realm

# AS-7 Auth — the authority is confined
Given the realm was imported fresh
 When identity-admin attempts the same composite DELETE
 Then Keycloak answers 403 and the composite is unchanged
 And in the realm file, manage-realm is held by service-account-migration-runner and by no other account,
     directly, through a group, or through a composite role

# AS-8 Failure — a refused narrowing stops the boot  (revised: verdict wording, FR-004)
Given the narrowing call fails (403, 5xx, unreachable) or the token mint is refused
 When MigrationRunner runs
 Then the run exits Failed, the log names the realm-privilege migrator,
  And no exception is swallowed (every service gated on migrations stays unstarted)
  And a 403 or refused mint carries a verdict that offers the non-destructive checks first

# AS-9 Regression — operators gain and lose nothing
 When the effective realm roles of "operator" are read
 Then they are exactly ["user"]

# AS-10 Happy path (credential) — migration-runner authenticates by signed JWT
Given the realm was imported fresh
 When a client_credentials request for migration-runner carries a client_assertion signed with the dev private key
 Then a token is issued whose realm-management roles include "manage-realm"

# AS-11 Bad request (credential) — the former secret no longer works
Given the realm was imported fresh
 When a client_credentials request for migration-runner carries client_secret "dev-only-migration-runner-secret"
 Then Keycloak answers 400 or 401 with error "invalid_client"

# AS-12 Conflict (credential) — an assertion is single-use
Given an assertion that has already minted a token
 When the identical assertion is presented again
 Then Keycloak rejects it, and MigrationRunner never presents an assertion twice (each mint signs a fresh jti)

# AS-13 Auth (confinement) — identity-admin cannot rewrite migration-runner into a credential it can use
Given the realm was imported fresh and identity-admin is authenticated with its own declared credential
 When it PUTs migration-runner's client replacing jwks.string with a public key it controls
 Then Keycloak answers 403 and an assertion signed by its private key mints no token
 When it PUTs migration-runner's client with clientAuthenticatorType "client-secret" and then regenerates the secret
 Then each call answers 403 and no manage-realm token results
# Red on today's realm: both PUTs answer 204 (ADR-0186 Context)

# AS-14 Auth (confinement) — identity-admin cannot see or remove migration-runner
Given the realm was imported fresh
 When identity-admin reads migration-runner, reads its client secret, or DELETEs it
 Then each answers 403 and migration-runner still exists
 When identity-admin lists clients, or looks up ?clientId=migration-runner
 Then migration-runner is absent from the result

# AS-15 Characterisation — identity-admin's real work is unchanged under the narrowed roles
Given the realm was imported fresh with identity-admin narrowed (no manage-clients, no view-clients)
 When the existing Identity integration suite runs with the real HttpKeycloakAdminClient
 Then it passes unmodified
# A failure is a finding against ADR-0186 Decision 5 reported to the owner, never a test to adjust

# AS-16 Guard (Decision 6) — the silent regression fails the build
Given the realm file
 Then service-account-identity-admin holds neither manage-clients nor view-clients,
      directly, through a group, or through a composite role
  And the realm sets adminPermissionsEnabled true
  And a permission scoped to migration-runner's pinned id exists, covering view, manage, map-roles,
      map-roles-composite and map-roles-client-scope, whose policy names service-account-identity-admin
      with NEGATIVE logic
  And migration-runner's client "id" is the id that permission names
# Proven by counterfactual: re-adding manage-clients to identity-admin turns it red

# AS-17 Key-pair coupling (Decision 4) — the two halves move together
Given the AppHost's dev-only default for the migration-runner private key
 And the realm file's migration-runner jwks.string
 Then the realm file registers exactly one public key
  And it is the public half of that private key, with kid equal to its RFC 7638 thumbprint

# AS-18 Default role read from the realm (review item)
Given the realm reports defaultRole.name N
 When the narrowing runs
 Then the DELETE targets N's composites, not a name derived from the realm name

# AS-19 Everything that was green still is
 Then the ADR-0181 assertions (AS-1, AS-2, AS-4, AS-5, AS-9) pass over the new credential and the narrowed roles
```

## 5. Functional requirements

Status column: **kept** = built in revision 1 and unchanged; **revised** = built, changes now; **new** = revision 2.

| ID | Status | Requirement |
|---|---|---|
| FR-001 | **revised** | `IKeycloakAdminClient.RemoveRealmDefaultPrivilegeAsync(string privilege, CancellationToken)`; `HttpKeycloakAdminClient` sends `GET admin/realms/{realm}` and reads `defaultRole.name` (**new**: not assumed to be `default-roles-{realm}`, ADR-0186 note 5), then `GET admin/realms/{realm}/roles/{privilege}`, then `DELETE admin/realms/{realm}/roles/{defaultRoleName}/composites` with `[<that representation>]`; `EnsureSuccessStatusCode()` on each; **no early-return branch** (the provider is idempotent). A realm representation with no `defaultRole.name` throws `InvalidOperationException` naming the realm. Guard: `Ensure.That(privilege).IsNotNull().IsNotNullOrWhiteSpace()` (ADR-0105). |
| FR-002 | kept | An `IMigrator` in `src/MigrationRunner` calls it with `"offline_access"`; registered `AddScoped` **above** `builder.AddCameraCatalogPersistence()`. |
| FR-003 | kept | Failure is not caught in the migrator; `MigrationRun` turns it into `Failed` + a log naming `ContextName`. |
| FR-004 | **revised** | A refusal carries a verdict, wrapped and rethrown (never swallowed), that offers the **non-destructive** checks first and names the volume drop **only** as the dev-stack remedy (ADR-0186 note 5). Two cases: **(a)** 403 on the narrowing — "migration-runner lacks `manage-realm` in the running realm: check its service-account role mapping / that the realm was imported from this realm file; re-running the migrator is safe. On the local persistent dev stack only, a realm imported before ADR-0181 is fixed by dropping the `keycloak-data` volume." **(b)** the token mint refused with `invalid_client` (400/401) — "the running realm does not accept migration-runner's signed assertion: the realm predates ADR-0186 or the registered public key and the configured private key are not one pair (rotate both together, ADR-0186 Decision 4). On the local persistent dev stack only, …volume…". *(Assumption A-2, revised: on a correctly provisioned realm neither should fire; wording is the lane's.)* |
| FR-005 | **revised** | Realm file, `migration-runner` client: pinned `"id"` (a fixed UUID), `"clientAuthenticatorType": "client-jwt"`, **no `"secret"`**, attributes `"use.jwks.string": "true"`, `"jwks.string": "<JWKS, one RS256 key, kid = RFC 7638 thumbprint>"`, `"use.jwks.url": "false"`, `"token.endpoint.auth.signing.alg": "RS256"`; description ≤ 255 chars, says it authenticates by signed JWT and holds `manage-realm` (ADR-0181) solely to narrow default roles. `service-account-migration-runner` keeps `query-groups, view-users, manage-realm`. The file stays UTF-8 with BOM. |
| FR-006 | **revised** | Prose corrected, no assertion changed — revision 1's four sites (done, `96f5b3ad`) **plus** `AppHost.cs:30-34`, `IdentityInfrastructureModule.cs:260-262` (both: no longer read-only; holds `manage-realm`; authenticates by signed JWT), `KioskInheritedPrivilegeIntegrationTests` class comment (L9-27), `OverridableParameters.cs:32-35` (the parameter count). |
| FR-007 | kept | `KioskInheritedPrivilegeIntegrationTests:63` inverts; anti-vacuity passes to the four-wall Theory (done). |
| FR-008 | **revised** | A comment on **#2878**: migration-runner's credential is now a **signed-JWT key pair**, and it authenticates a `manage-realm` holder — #2878's "realm-takeover credential" wording is adopted (ADR-0186 Decision 3). The templated-secret list gains **two coupled items**: the migration-runner **private key** (to the worker) and its **registered public key** (to the realm), rotated together (Decision 4). `dev-only-migration-runner-secret` leaves the list. migration-runner is not a drop-list entry. No client is added. |
| FR-009 | new | MigrationRunner obtains its admin token by `client_credentials` with `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` and a `client_assertion` built with `System.IdentityModel.Tokens.Jwt` (`JwtSecurityTokenHandler`, `SigningCredentials(RsaSecurityKey, SecurityAlgorithms.RsaSha256)`): header `kid` = RFC 7638 thumbprint of the public key; claims `iss`=`sub`=`migration-runner`, `aud`= `{authority}/realms/{realm}/protocol/openid-connect/token`, `jti` fresh per mint, `iat`, `exp` = now + ≤ 60 s. No `client_secret` field. One assertion per mint; none cached. (ADR-0186 Decisions 1-2.) |
| FR-010 | new | The assertion mint is **not retried at the HTTP layer**: it uses a named client **without** `RetryEveryMethod()` (ADR-0143 default — POST gets one attempt). Reason, stated at the registration: the handler resends the identical body, i.e. the same `jti`, which Keycloak rejects as a replay (ADR-0186, measured), so a retry would turn a transient failure into a misleading `Token reuse detected`. The `identity-admin` secret mint keeps its existing client and its opt-in, untouched. |
| FR-011 | new | Configuration: `Keycloak:AdminClientPrivateKey` (base64 PKCS#8 DER, one line). If it is set the admin token is minted by assertion; if `Keycloak:AdminClientSecret` is set the existing secret mint runs unchanged; if **both** are set the first mint throws `InvalidOperationException` naming both keys (ambiguous credential configuration at a trust boundary, never resolved silently). The five other `ClientCredentials` callers are untouched. |
| FR-012 | new | AppHost: `MigrationRunnerClientSecret` is **removed**; `MigrationRunnerClientPrivateKey` is added via `AddOverridableParameter(…, "<dev-only base64 PKCS#8>", secret: true)` and passed as `Keycloak__AdminClientPrivateKey`; `Keycloak__AdminClientSecret` is no longer set on `migrations`. No production-reachable default exists beyond the overridable dev default every other `*ClientSecret` already has (ADR-0186 Decision 3; ADR-0130). |
| FR-013 | new | Realm file, `identity-admin`: `service-account-identity-admin` loses `manage-clients` and `view-clients`, keeps `manage-users, view-users, query-clients, query-users, query-groups`. |
| FR-014 | new | Realm file, FGAP v2: `"adminPermissionsEnabled": true`; the `admin-permissions` client's `authorizationSettings` carry (i) a user policy naming `service-account-identity-admin` (positive), (ii) an **all-clients** permission granting it `view` and `manage`, (iii) a permission on **migration-runner's pinned id only** for `view, manage, map-roles, map-roles-composite, map-roles-client-scope` whose policy names the same user with **`NEGATIVE`** logic. JSON shape **taken from a realm export of a working FGAP v2 configuration on 26.6.4**, not from memory (ADR-0186 note 1) — recorded in `research.md`. |
| FR-015 | new | Architecture guards (realm file): (a) Decision 6 — AS-16, with "holds" meaning direct `clientRoles`, membership of any group (recursively through `subGroups`) whose `clientRoles` include it, or any realm/client role whose `composites` (recursively) include it; the built-in `realm-admin` counts as holding every `realm-management` role; (b) the existing `manage-realm` holder guard (AS-7) is widened to the same three routes (ADR-0186 note 5); (c) AS-17 key-pair coupling. |
| FR-016 | new | Integration (Aspire fixture, fresh import): AS-10, AS-11, AS-13, AS-14 pass; AS-15 — the existing Identity integration suite passes **unmodified** under the narrowed roles. |
| FR-017 | new | `KioskInheritedPrivilegeIntegrationTests`: the class comment and `A_kiosk_enrolled_at_runtime…`'s assertion message are corrected (a kiosk is no longer born holding it; the test now pins that enrolment does not *add* it). Runtime-strip coverage: a note naming `KeycloakRequestSequenceCharacterisationTests` as where the strip's behaviour is covered, because a narrowed realm gives the strip nothing to remove end-to-end (ADR-0186 note 5 permits the note). |
| FR-018 | new | README (`README.md:65-70`): one line — after #1995 merges, a persistent dev stack whose `keycloak-data` volume predates it fails the `migrations` step (refused mint) until the volume is dropped; coordinate with #91. |

## 6. Independent end-to-end test procedure

1. **Docker-free (local and CI):** `Architecture.Tests` (FR-015 a/b), `ServiceDefaults.Tests` (assertion form and
   claims), `Identity.Infrastructure.Tests` (adapter, credential selection), `MigrationRunner.Tests` (verdicts),
   `Integration.Tests` `FixtureLogic` category (AS-17 mirror; parameter override; stack status) — no containers.
2. **CI (Docker):** the Aspire fixture boots `E2ETests` mode → throwaway Keycloak, fresh import every run. The fixture
   booting proves AS-6, AS-10 indirectly (migrations reached **Finished** over the signed JWT) and the pinned id (a bad
   pin fails Keycloak's start, ADR-0186 Decision 6). `KioskInheritedPrivilegeIntegrationTests` proves AS-1/2/4/5/7/9;
   the new confinement class proves AS-10/11/13/14; the whole Identity integration subset proves AS-15.
3. **CI e2e:** `e2e/wall-outlives-its-session.spec.ts` unmodified, green (AS-3).
4. **Manual (phase 5, Docker is available locally as of 2026-10-23):** boot the AppHost on a **dropped** `keycloak-data`
   volume (coordinate with whoever runs the shared stack — one machine, one stack). Then, as `identity-admin` with
   `dev-only-identity-admin-secret`: `GET /admin/realms/smart-sentinel-eye/clients?clientId=migration-runner` → empty;
   `PUT /clients/<pinned id>` → 403. With the dev private key: sign an assertion and mint → 200, token carries
   `manage-realm`; mint with `client_secret=dev-only-migration-runner-secret` → `invalid_client`. With a master
   `admin-cli` token: `defaultRole.name`, composite lacks `offline_access`, a throwaway user lacks it, `wall-munich`
   has it. **The verification note names the realm it ran against** (ADR-0181 note 8).

## 7. Consequences the PR must state

- **A persistent dev stack fails its boot after this merges until its `keycloak-data` volume is dropped.** Under
  revision 2 it fails **earlier** than revision 1 said: the stale realm still has migration-runner as a client-secret
  client, so the signed assertion is refused at the mint (`invalid_client`), before the narrowing's 403 could occur.
  FR-004(b) names the cause. Coordinate the drop with #91; README line FR-018. CI and the fixture are unaffected.
- **ADR-0186 must be on `develop` before this PR merges.** PR #3106 is open (accepted, unmerged). Spec 440 cites it;
  the branch must not be stacked on it (rebase onto `develop` after #3106 merges — CLAUDE.md "Stacked PRs").
- **Every Keycloak version bump re-runs the deny tests (AS-13, AS-14, AS-16) before it merges** (ADR-0186 Consequences).
  The PR states this; the Dependabot/Renovate configuration is not changed by this spec.
- **Granting `manage-clients` (or `view-clients`) to anything in the app realm now needs a security review against
  ADR-0186**: any holder bypasses the deny silently; AS-16 guards `identity-admin` only.
- **`identity-admin` can still rewrite the system clients** (`realm-management`, `admin-permissions`, `broker`,
  `admin-cli`, `security-admin-console`); no escalation was found; open question, ADR-0186 note 6.
- Unchanged, said so: `RealmIdentityTests.cs:77` (`offline_access` client **scope**); `KioskPrivilegeSweep` and
  enrolment's strip; the five other secret-authenticated service accounts.
- **Not demonstrated:** whether an offline token minted by a hand-made account before the narrowing keeps refreshing
  (spec 080 §2.2); the `manage-users` routes over migration-runner's service-account user beyond the three ADR-0186
  probed.

## 8. Success criteria

- **SC-001** A hand-made person account and a hand-made service account in a fresh fixture realm hold no
  `offline_access` (red before revision 1, green after — **met**, CI-pending).
- **SC-002** All four wall accounts hold it; `operator` holds exactly `user`.
- **SC-003** `realm.defaultRole` is the un-suffixed composite; no `-1` sibling.
- **SC-004** `e2e/wall-outlives-its-session.spec.ts` green, unmodified.
- **SC-005** Every planted violation in plan §6 turns its named test red, quoted in the PR.
- **SC-006** (new) migration-runner mints by signed JWT; its former secret is `invalid_client`.
- **SC-007** (new) Both measured reconfigure breaks answer 403 from `identity-admin` and yield no `manage-realm`
  token — **red on the realm as of `96f5b3ad`**, green after.
- **SC-008** (new) The Identity integration suite passes unmodified under the narrowed `identity-admin`.
- **SC-009** (new) Re-adding `manage-clients` to `identity-admin` turns the Decision 6 guard red (counterfactual quoted).

### 8a. Phase-4a colour, per slice

| Slice | Colour | Why |
|---|---|---|
| S1 — signed-JWT credential (FR-009..012, FR-005 credential half, AS-10/11/12/17) | **red** | the worker's authentication changes; the former secret stops working |
| S2 — FGAP confinement (FR-013, FR-014, FR-016 AS-13/14, FR-015a) | **red** | AS-13/14 answer 204/200 today; the guard fails on today's file |
| S2' — Identity suite under narrowed roles (AS-15) | **characterisation** | must pass unmodified before and after; an edited assertion is a block, not a fix |
| S3 — review carry-overs: FR-001 default-role read, FR-004 verdicts | **red** | request sequence and message text change |
| S3' — FR-015b widened `manage-realm` guard | **guard, proven by counterfactual** | correct code cannot make a guard red; a planted group/composite grant must |
| S4 — FR-006/FR-017 prose, FR-018 README | **comment-only characterisation** | strip comments, hash code before/after |

## 9. Assumptions, marked

- **A-1** Keycloak 26.6.4 semantics as measured by spec 080 and ADR-0186; an upgrade re-opens §2 and the deny tests.
- **A-2** (revised) FR-004's wording assumes a 403 or a refused mint on a correctly imported realm means a stale
  volume or a mismatched key pair.
- **A-3** The anti-decoy reads use the master bootstrap admin (as built in revision 1), so the observer is not the
  subject's own credential.
- **A-4** (new) `manage-realm` suffices for `GET admin/realms/{realm}` (FR-001's `defaultRole` read). ADR-0186 measured
  this for a `manage-realm` holder over the app realm from the master realm (measure 2), not for migration-runner
  itself; the fixture boot (migrations Finished) proves it in CI, and FR-004(a) names it if not.
- **A-5** (new) An Aspire parameter carries a one-line base64 PKCS#8 value through `Parameters:` / environment
  unchanged (a PEM's newlines are why base64 DER is chosen). Proven by the fixture boot.
- **A-6** (new) The spike's "all 13 runtime flows unchanged" holds for the real adapter — **not assumed**: AS-15 is
  the test, and a failure is reported to the owner as a finding against ADR-0186 Decision 5.
