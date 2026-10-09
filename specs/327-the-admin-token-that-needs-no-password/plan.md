# Plan 327 — The admin token that needs no password

**Spec:** [spec.md](spec.md) · **Issue:** #2511 · **Phase:** 2 (Plan)

## 1. Context, boundaries, ADR position

- **Bounded context:** none. The change touches the realm import
  (`src/AppHost/Realms/smart-sentinel-eye-realm.json`), the integration-test fixture
  (`tests/Integration.Tests/Fixtures/`), the call sites in `tests/Integration.Tests/**`, and one
  new `Architecture.Tests` guard. It has no Domain, Application, Infrastructure or Api code, no
  entity, no value object, no message, no `Shared.Contracts` change, and no cross-context
  reference. **No production code under `src/` changes except the realm JSON.**
- **No AppHost parameter.** Every existing confidential client's secret is an
  `AddOverridableParameter` because a *service* reads it (`AppHost.cs:31-70`). No service reads
  this secret, only the test fixture does. The fixture keeps it as a constant, as `RealmProbe`
  already does for `identity-admin` (`RealmProbe.cs:55-56`), and a mirror fact stops it drifting
  from the realm (§5, T-C). Adding a parameter nothing consumes would be speculative generality
  (ADR-0036).
- **No new ADR.** The mechanism is a recorded human decision (issue comment, 2026-10-08). It
  reuses two accepted patterns:
  - confidential `client_credentials` service-account clients (ADR-0100; `identity-admin`,
    `scenario-simulator`, `stream-distribution-attribution`);
  - a dev-only client in the shared realm import (ADR-0111, `scenario-simulator`).

  No new kind of principal, trust boundary or runtime component appears. Spec 207 and spec 324
  changed realm security configuration without an ADR. **What would need an ADR:** choosing
  among the persona options (spec §Out of scope 1), because option (b) keeps a password grant on
  purpose and option (a) multiplies service principals. That choice belongs to the follow-up, not
  here.
- **Lane limits respected:** no test is deleted. No assertion is edited (the four
  console-token facts keep their assertions, and only their messages lose a claim that becomes
  false). No threshold, suppression or analyzer changes. `management-web` is untouched.

## 2. The realm client (`smart-sentinel-eye-realm.json`)

Append to `clients[]`, after `identity-admin`. It mirrors `identity-admin`'s shape exactly; the
scopes are copied from `management-web` at `:142-167`:

```json
{
  "clientId": "integration-test-admin",
  "name": "Smart Sentinel Eye - Integration test admin (dev/CI-only service account)",
  "description": "Spec 327 (#2511). The Integration.Tests harness's admin caller via client_credentials, replacing ROPC through management-web. Scopes equal management-web's (guarded). In /fabs/munich like the seeded admin. No realm-management roles. Never deployed.",
  "enabled": true,
  "publicClient": false,
  "serviceAccountsEnabled": true,
  "standardFlowEnabled": false,
  "directAccessGrantsEnabled": false,
  "secret": "dev-only-integration-test-admin-secret",
  "protocol": "openid-connect",
  "defaultClientScopes": [ "...exactly management-web's 24 entries, same order..." ]
}
```

**The description must stay under 255 characters.** A longer client description kills the realm
import and hangs the whole Aspire fixture (project memory, "Keycloak realm description limit").
The draft above is 248 characters (measured), which leaves almost no room: trim it rather than
extend it. T004 re-measures before booting, and the AS-2 guard
asserts `description.Length <= 255` so the next edit cannot cross it.

Append to `users[]`, after `service-account-identity-admin`:

```json
{
  "username": "service-account-integration-test-admin",
  "enabled": true,
  "serviceAccountClientId": "integration-test-admin",
  "groups": ["/fabs/munich"]
}
```

There are deliberately **no `clientRoles` and no `realmRoles`.** No API reads realm roles
(spec finding 2), and the realm has no `roles` scope to emit them anyway. Keycloak Admin work in
tests stays on `RealmProbe` / `identity-admin`.

Preserve the file's encoding and line endings. It starts with a UTF-8 BOM (`EF BB BF`, checked
at `19f4e2ee`), and that BOM must be kept.

**Existing realm guards this must satisfy (read, not assumed):**
`RealmIdentityTests.Every_client_holds_the_identity_scope` (holds via `sse-identity`),
`Every_scope_a_client_names_exists` (all copied names exist),
`No_client_carries_its_own_mapper` (none added),
`RealmAudienceTests` (`sse-audience` present),
`LegacyBundleGrantTests` (`sse.management` absent),
`ScopeGrantTests` (adds grants only).
The comments in `RealmAudienceTests.cs:10` ("nine clients") and `RealmIdentityTests.cs:13`
("eight clients") are prose counts that are already stale. They are not touched here.

## 3. Fixture design (`AspireFixture.Auth.cs`)

Smallest change that keeps one cache and one response parser:

| Member | Change |
|---|---|
| `ClientId = "management-web"` | **kept**, name unchanged (10 lockout-test references read it). Doc comment: *the console client; persona and password-probe tokens only.* |
| `HarnessClientId = "integration-test-admin"`, `HarnessClientSecret = "dev-only-integration-test-admin-secret"` | **new** `public const string` |
| `CreateAdminClientAsync(resource, ct)` | builds on `GetAdminAccessTokenAsync` instead of `CreateAuthenticatedClientAsync(AdminUsername, AdminPassword)` |
| `GetAdminAccessTokenAsync(ct)` | **new.** Cache key `client_credentials|integration-test-admin`, same `tokenCache`, same `ExpirySafetyMargin` |
| `FetchAccessTokenAsync(...)` | the POST + status check + JSON parse is extracted into one private `RequestTokenAsync(form, describe, ct)`. The password overloads and a new `FetchClientCredentialsTokenAsync` build their forms and call it. The error message names the grant type, so a failure says "client_credentials grant failed for 'integration-test-admin'" rather than "password grant failed". |
| `AdminUsername`, `AdminPassword` | **kept.** Still used by the lockout probes, the webhook `GetAccessTokenForClientAsync` sites and the four console-token facts. |
| `GetAccessTokenAsync(user, pw)`, `CreateAuthenticatedClientAsync`, `GetAccessTokenForClientAsync` | **unchanged** |

There is **no special-casing** of `GetAccessTokenAsync("admin", …)` to quietly reroute. A caller
that wants the human admin gets the human admin. A caller that wants the harness admin says so by
name.

**Reuse considered and declined:** `ServiceDefaults.Authentication.ClientCredentialsTokenProvider`
is the production `client_credentials` cache. Using it here would add a second cache next to the
fixture's existing `tokenCache`, plus an `HttpClient` / `TimeProvider` / logger construction (the
`RealmProbe` → `KeycloakAdminTokenProvider` shape). That is more code than the five-line form it
replaces. The fixture's cache is what the existing helpers already share.

## 4. Call-site classification (the exact scope)

| Population | Count | Action |
|---|---|---|
| `CreateAdminClientAsync(...)` | 370 in 84 files | **No edit.** Migrates by the fixture change alone. |
| `GetAccessTokenAsync(AspireFixture.AdminUsername, AspireFixture.AdminPassword)` that wants "an admin token for API / hub / WHEP / device registration" | **19 in 16 files**: `Automation/{EventReachesItsEffects,FirstPublishPerType,RuleSwitchesAWallIntegration}Tests`, `CameraCatalog/StaleIdempotencyReservationIntegrationTests`, `SystemVariables/{VersionSurvivesARestart,ResolvedTextReachesItsFab}Tests`, `LayoutComposition/{SignalRRevocation,OverlayFrameFabScoping}IntegrationTests`, `Identity/{MqttAudienceIntegration,NFR001_JwtValidationLatency,NFR002_MqttConnectAuth}Tests`, `Identity/TokenAudienceIntegrationTests` (fact 3, `:79` only), `StreamDistribution/WhepAuthIntegrationTests` (×4), `StreamDistribution/WhepHandshakeLatencyTests`, `OverlayDesigner/{OverlayPushIntegration,ReconnectReconcileIntegration}Tests` | → `aspire.GetAdminAccessTokenAsync()`. A one-token mechanical replacement. **No assertion edits.** |
| `TokenAttributionIntegrationTests` ×3, `TokenAudienceIntegrationTests` fact 1 (`:49`) | 4 | **Stay** on `GetAccessTokenAsync(AdminUsername, AdminPassword)`, since they assert the console token's shape (spec §Out of scope 3). Only fact 1's `customMessage` changes: drop *"This is the client every other integration test authenticates through"*, which is no longer true. |
| `CreateAuthenticatedClientAsync` / non-admin `GetAccessTokenAsync` | 109 + ~10 | **Unchanged.** Persona follow-up. |
| Direct `grant_type=password` posts (lockout/password/fab-claim/console-scope probes, `RunModeStackAddress`) | 8 files | **Unchanged.** Password-probe follow-up. |
| `GetAccessTokenForClientAsync` | 16 | **Unchanged.** Not `management-web`. |

Recount at 4b before editing. The memory note *grep the invariant half of a shape* applies.
Use a multiline search for `GetAccessTokenAsync\(\s*AspireFixture\.AdminUsername`, because many
sites break the line after `(`. The count at `19f4e2ee` is 23 in 17 files; 23 − 4 = 19.

## 5. Tests

### Phase 4a: red (test-writer). Must compile against the *current* fixture.

**T-A `tests/Architecture.Tests/IntegrationTestAdminClientTests.cs`** (no stack). It reads the
realm with the same `ReadRealm()` shape as `RealmAudienceTests`.
1. `The_realm_seeds_exactly_one_integration_test_admin_client`: red (0 found).
2. `It_is_a_confidential_service_account_with_no_browser_or_password_flow`: asserts
   `publicClient == false`, `serviceAccountsEnabled == true`, `standardFlowEnabled == false`,
   `directAccessGrantsEnabled == false`. It looks the client up through the fact-1 lookup and
   fails with "absent" rather than passing vacuously.
3. `Its_default_scopes_equal_the_console_clients_in_both_directions`: two `Except`
   comparisons, as in the `KioskScopeParityTests` precedent. Red (absent).
4. `Its_service_account_is_in_exactly_the_admin_users_fab_groups`: compares against the
   `admin` user's `groups` read from the file, never a literal. Red (no user).
5. `Its_service_account_holds_no_realm_management_role`: asserts the user exists first, then
   that `clientRoles` is absent or has no `realm-management` key. Red (no user). It must not pass
   vacuously: the existence check comes first.
6. `Its_description_fits_keycloaks_255_character_limit`. Red (absent).

The client id in this class is a local `private const string`. The fixture constant does not exist
yet, and Architecture.Tests does not reference Integration.Tests.

**T-B `tests/Integration.Tests/Identity/AdminHarnessTokenIntegrationTests.cs`**
(`[Collection(AspireCollection.Name)]`, live). It uses only the existing `CreateAdminClientAsync`
and reads the token back from `client.DefaultRequestHeaders.Authorization.Parameter`.
1. `The_admin_harness_token_is_minted_by_the_integration_test_admin_client`: `azp` equals
   `"integration-test-admin"`. **Red today with `azp = management-web`. This is the slice's
   defining red.**
2. `The_admin_harness_token_carries_the_admin_users_fab_and_the_consoles_scopes`: `groups`
   contains `/fabs/munich`; `scope` ⊇ every `management-web` default scope whose client scope has
   `include.in.token.scope == "true"` (read from the realm file, same path helper as
   `RealmImportMirrorTests`); `aud` contains `smart-sentinel-eye-api`; `sub` is a non-empty Guid.
   **Green on arrival, declared.** Today's `management-web` token already satisfies it. It is the
   characterisation half: the migration must not lose a claim the 393 sites rely on, and it must
   pass unmodified after 4b.
3. `A_wrong_secret_for_the_integration_test_admin_client_is_refused`: posts
   `client_credentials` with a wrong secret and expects 401. **Red today:** the client does not
   exist, so Keycloak answers with `invalid_client` and 401. That is *the same status*, so this
   fact would arrive green for the wrong reason. **Therefore** it also asserts that the correct
   secret yields 200 in the same fact (control first), which makes it red today.
4. `The_integration_test_admin_client_refuses_a_password_grant`: control first (the correct
   secret gives a `client_credentials` 200, which is red today), then the password grant with the
   admin user's credentials against `integration-test-admin` must be refused (400
   `unauthorized_client`).

T-B needs an entry in `tests/Integration.Tests/ci-shards/shard-N.filter`. Choose the shortest
shard; the README in that folder says how. A missing entry fails deterministically, which is the
memory note "new test classes need a shard-filter entry".

**4a run, required outcome.** T-A facts 1–6 red with "absent" reasons. T-B 1, 3 and 4 red for the
reasons named. T-B 2 green. No compile error. No fixture boot failure. Any other outcome: stop
and report.

### Phase 4b: guards written alongside the fixture change, red by counterfactual (spec 248 precedent)

These reference `GetAdminAccessTokenAsync` / `HarnessClient*`, which do not exist at 4a, so they
cannot be compile-clean red tests. The test-writer writes them **after** T006 lands and before
the call-site migration. The engineer does not write them. Red is observed by mutating the guarded
code and quoting the output.

**T-C, append to `RealmImportMirrorTests.cs`** (`FixtureLogic`):
`AspireFixture_HarnessClientId_names_a_client_the_import_seeds` and
`AspireFixture_HarnessClientSecret_is_the_secret_the_import_seeds_for_that_client`. These mirror
`:53-84` and reuse `ClientsNamed` / `SecretForClient`. Counterfactual: change one character of
`HarnessClientSecret`, watch the second fact go red, then revert.

**T-D, append to T-B:** `Every_admin_accessor_presents_the_same_subject`. `sub` from
`CreateAdminClientAsync`'s header must equal `sub` from `GetAdminAccessTokenAsync()`.
Counterfactual: point `GetAdminAccessTokenAsync` at `GetAccessTokenAsync(AdminUsername,
AdminPassword)`, watch it go red, then revert. `StaleIdempotencyReservationIntegrationTests` is
the existing behavioural witness of the same invariant (spec §Decision).

## 6. Commit plan (each commit builds on its own; ADR-0087)

1. `test(identity): prove the admin harness still mints its token through the console's password grant`.
   T-A, T-B and the shard entry. It builds. T-A and T-B are red, the stated pair (CLAUDE.md
   "Builds is not passes").
2. `fix(2511): mint the integration harness's admin token by client_credentials`: realm client,
   service-account user, fixture (`HarnessClient*`, `GetAdminAccessTokenAsync`,
   `CreateAdminClientAsync` re-pointed, `RequestTokenAsync` extraction). T-A and T-B turn green.
   **The realm and the fixture land in one commit.** A fixture pointing at a client the realm
   lacks fails all 370 admin sites.
3. `test(identity): guard the harness client's secret and single admin identity`: T-C and T-D,
   with counterfactual output kept for the PR body.
4. `refactor(tests): take the admin token from GetAdminAccessTokenAsync at nineteen call sites`:
   the §4 migration plus the `TokenAudience` fact-1 message reword. Commit 2 already moved
   `CreateAdminClientAsync`, so commits 2–3 temporarily have two admin identities. Commit 4 must
   follow before the full-suite run. The full suite is run **after commit 4**. Commits 2–3 are
   verified with the T-A/T-B/T-C/T-D filters plus `StaleIdempotencyReservationIntegrationTests`.
   That test is *expected* red between 2 and 4 (mixed identities), and the PR body states this
   bisect window. **Alternative the engineer may take instead:** fold commit 4 into commit 2 so
   no commit has mixed identities. This is preferred if the diff stays reviewable (19 one-token
   edits). Recommended: fold, and keep T-C/T-D as commit 3.

## 7. Follow-up issue (filed at phase 7, not by this PR's code)

The title should say something like *"Persona tokens and password-grant probes still need
management-web's ROPC"*. The body carries:
- the inventory: §4 rows 3–5 with counts, plus `e2e/wall-survives-a-lockout.spec.ts` and
  `RunModeStackAddress.cs`;
- options (a)/(b)/(c) from spec §Out of scope 1 and their costs;
- the note that the lockout probes need *some* password-accepting client or a form-login driver
  whatever is chosen;
- labels `tech-debt`, `agent:blocked` until a human decides. Add it to Project #13
  (`gh project item-add 13 --owner smartsolutionslab --url …`). Cross-reference #2285, #2488 and
  #2511.

**Closing #2511.** This PR applies the approved mechanism everywhere it can reach. The remaining
dependency needs a different mechanism and a new decision. Recommendation: `Closes #2511` with
the follow-up linked in the PR body and on #2511. **Gate question D1 for the reviewer:** close
#2511 with the follow-up filed, or keep #2511 open as the umbrella and use `Refs #2511`.

## 8. Risks

| Risk | Mitigation |
|---|---|
| A migrated test silently relied on the admin being a human user (A1) | Full four-shard run after the migration. A failure moves that one site to the persona path with a stated reason, and **never** edits its assertion. |
| Persistent `keycloak-data` volume keeps the old realm, so the new client is missing and every admin site fails with `invalid_client` | `docker volume rm` before the 4b run (memory: realm edits need the volume deleted). The fixture's error message names the grant and the client. |
| Description over 255 characters hangs the fixture boot | T-A fact 6, plus checking the length at T005 before booting. |
| Scope drift: a scope later added to `management-web` but not to the harness client makes admin tests 403 | T-A fact 3 fails the build in both directions. |
| A higher-privileged non-human principal is added to the realm | Its privileges are bounded by T-A facts 3–5 to exactly the seeded `admin` user's API power, and none in Keycloak admin. Its secret is as public as `admin`'s password already is. `security-reviewer` covers it at phase 6. |
