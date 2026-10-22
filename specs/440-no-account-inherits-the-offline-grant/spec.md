# Spec 440 — No account inherits the offline grant

**Issue:** #1995 · **ADRs:** **ADR-0181** (governing; Accepted 2026-10-22, supersedes ADR-0134, corrects ADR-0132),
ADR-0067 (MigrationRunner), ADR-0131 (the wall keeps its own grant), ADR-0132, ADR-0133, ADR-0130 (no production
deployment), ADR-0144 (lane), ADR-0139 (red first), ADR-0105 (guards), ADR-0103 (Aspire fixture, no Testcontainers)
**Built on:** spec 052 (`kiosk-wall`, per-account strip), spec 080 (measurements — branch
`fix/1995-offline-access-is-granted-not-inherited`, `ede331a6`; not merged, superseded by this spec), spec 019
(MigrationRunner reads the realm), spec 132 (strip reports whether it changed anything)
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** **red** (behaviour-changing)
**Security-sensitive:** yes — a realm-wide authority (`manage-realm`) is granted permanently to a service account.
`security-reviewer` at phase 6 is **mandatory** (ADR-0181 §6); the slice is not mergeable without it.
**Latency budget (constitution §IV):** **N/A** — a boot-time step in a one-shot worker that completes before any
API starts. No leg of the event→overlay path is touched.

## 1. The decision, as given (ADR-0181)

1. `offline_access` is removed from the realm's default composite `default-roles-smart-sentinel-eye`, so no account
   inherits it by existing. The four `wall-*` accounts keep it as a **named** grant.
2. `realm-management/manage-realm` is granted to `service-account-migration-runner` **permanently**, to nothing else.
3. The narrowing is an `IMigrator` in `MigrationRunner`, **registered first**, run on **every** boot, idempotent; a
   failed narrowing **fails the migrator and therefore the boot**.
4. The realm file does **not** declare the composite, and must not.
5. Enrolment's per-account strip (`StripInheritedRealmRolesAsync`) stays.

The lane implements ADR-0181's Implementation Notes and does not edit the ADR.

## 2. Premise, re-verified at `29968a0d` (current `origin/develop`)

| Claim | Observed at HEAD |
|---|---|
| The realm file does not declare `default-roles-*` nor `defaultRole` | `grep default-roles src/AppHost/Realms/smart-sentinel-eye-realm.json` → no match |
| The four wall grants are already explicit | realm file L432/454/476/498: `"realmRoles": ["user", "offline_access"]` on `wall-munich/-dresden/-berlin/-hamburg` |
| `migration-runner` holds only `query-groups`, `view-users` | realm file L618-627 |
| Its description says "Read-only" | realm file L325 — must be rewritten (ADR-0181 note 1), ≤ 255 chars |
| MigrationRunner already talks to Keycloak as `migration-runner` | `AppHost.cs:333-336` (`Keycloak__AdminClientId=migration-runner`, secret from `AddOverridableParameter("MigrationRunnerClientSecret", "dev-only-…")`, `AppHost.cs:35`); `Program.cs:59` `AddKeycloakAdminClient()` |
| Migrators run in **registration order**, from a scope, and any throw fails the run | `MigrationRun.cs:46-70` (`GetServices<IMigrator>()`, unfiltered catch → `Failed`) |
| The first registered migrator today is CameraCatalog's (singleton, inside `AddCameraCatalogPersistence`) | `Program.cs:31`, `CameraCatalogInfrastructureModule.cs:43` — so "first" means registered **above** `builder.AddCameraCatalogPersistence()` |
| `IKeycloakAdminClient` is scoped, so a migrator depending on it is scoped | precedent `EventPartitionRolloverMigrator`, `Program.cs:64` (`AddScoped`) |
| The control test that must invert | `tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs:63` `An_account_the_provider_creates_holds_it_until_something_removes_it` |
| The anti-vacuity control that inherits its duty | same file `:115` `A_wall_display_account_does_hold_it` (wall-munich only today) |
| Wrong prose, no assertion change | `tests/Architecture.Tests/WallDisplayAccountTests.cs:22-38` ("discarded on import, which was tried and verified"); class comment of `KioskInheritedPrivilegeIntegrationTests.cs:11-20`; `HttpKeycloakAdminClient.cs:85-89` ("Take back what the realm gave it for free"); `IKeycloakAdminClient.cs:133-143` ("Why this exists") |
| `apps/kiosk-web/src/app/auth.ts`'s "note on the inherited privilege" (ADR-0181 note 6) | **Not found at HEAD**: `grep -n inherit auth.ts` → no match. `:83-89` speaks of granting the realm role to a screen account, which stays true. **Phase 4 re-reads it; if nothing claims inheritance, it is left untouched and the PR says so.** |
| The test class is already sharded | `tests/Integration.Tests/ci-shards/shard-2.filter` contains `KioskInheritedPrivilegeIntegrationTests.` — new facts inside it need no filter edit; a **new** integration class would |
| Ten `IKeycloakAdminClient` implementers must grow the method | `HttpKeycloakAdminClient` + 9 test doubles (`Identity.Application.Tests/Fakes/{FakeKeycloakAdminClient,RecordingIdentityPorts,TokenHonouringPorts}.cs`, `DisableWebhookClientCommandHandlerTests.cs`, `Identity.Infrastructure.Tests/Fakes/{EnrolledKiosks,Silent,Unreachable}KeycloakAdminClient.cs`, `OrphanedClientSweepHostedServiceTests.cs`, `MigrationRunner.Tests/KeycloakProvisionedFabSourceTests.cs`) |

**The measurements are spec 080's** (§2.2-2.4, fresh Keycloak 26.6, no reused volume) and are restated in ADR-0181's
two tables; they are not re-run here. Summarised:

- A fresh import synthesises `default-roles-smart-sentinel-eye → {user, offline_access, uma_authorization,
  account/view-profile, account/manage-account}`. Declared users inherit nothing; an account created **after**
  import inherits all five.
- **The realm file cannot narrow it.** Declaring the composite naming an undeclared role → Keycloak refuses to boot
  (`Unable to find composite realm role: uma_authorization`, exit 1, fixture hangs). Declaring it with every role
  declared → boots, stores the declaration, creates **`default-roles-smart-sentinel-eye-1`** carrying
  `offline_access` and binds `realm.defaultRole` to that: **a silent decoy**.
- `DELETE /admin/realms/{realm}/roles/default-roles-{realm}/composites` with `[<offline_access representation>]`:
  403 for `identity-admin` as declared, 403 with `view-realm`, **204 with `manage-realm`**. 204 again when the member
  is already absent (idempotent). The representation from `GET /roles/offline_access` is accepted as-is (unlike the
  user role-mapping delete, which needs a read-back).
- After it: a new account holds `default-roles-…, user, uma_authorization`, **not** `offline_access`; `wall-munich`
  still holds `offline_access, user` (the grant resolves against the realm role, which Keycloak always creates).

## 3. User stories

### US1 (P1) — An account nobody in this system created does not inherit the offline grant

As the fab's security owner, I want an account created by hand in Keycloak (console, admin API, any route this
system does not drive) to hold no `offline_access`, so that a never-expiring credential can only be minted by an
account somebody deliberately named.

### US2 (P1, same slice) — Wall displays still hold it, by name

As a wall display, I keep `offline_access` so an unattended reboot recovers without a sign-in (ADR-0131); the grant is
visible in the realm file, and a fifth holder is a reviewed edit.

**One slice.** Neither half is observable alone: the grant without the migrator changes nothing, the migrator
without the grant fails every boot.

### Out of scope

- Removing `offline_access` from the wall accounts; #1976 (device credentials). Owner ruling, 2026-09-05.
- `offlineSessionIdleTimeout` / `offlineSessionMaxLifespanEnabled` (vendor defaults; ADR-0181 Neutral).
- Narrowing anything else in the composite (`uma_authorization`, `account` roles stay).
- The production realm overlay and templated secrets (#2878 / #1015).
- Editing ADR-0132/0134/0181 or merged specs (historical records).
- The mojibake in the `migration-runner` client's `name` (`â€”`, L324) — "nothing else in the file changes"
  (ADR-0181 note 1). Noted, not fixed.

## 4. Acceptance scenarios

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

# AS-5 Conflict / re-run — idempotent on every boot
Given the composite no longer contains "offline_access"
 When the narrowing runs again as migration-runner
 Then it succeeds
  And the composite's members are unchanged by the second run

# AS-6 Bad request / boot integrity — the edited realm file imports
Given the realm file after this change
 When the Aspire fixture imports it into a fresh Keycloak
 Then the import finishes and the fixture boots
  And the migration-runner client description is at most 255 characters and no longer says "Read-only"

# AS-7 Auth — the authority is confined
Given the realm was imported fresh
 When identity-admin attempts the same composite DELETE
 Then Keycloak answers 403 and the composite is unchanged
 And in the realm file, manage-realm is held by service-account-migration-runner and by no other account

# AS-8 Failure — a refused narrowing stops the boot
Given the narrowing call fails (403, 5xx, unreachable)
 When MigrationRunner runs
 Then the run exits Failed, the log names the realm-privilege migrator,
  And no exception is swallowed (every service gated on migrations stays unstarted)

# AS-9 Regression — operators gain and lose nothing
 When the effective realm roles of "operator" are read
 Then they are exactly ["user"]
```

## 5. Functional requirements

- **FR-001** `IKeycloakAdminClient.RemoveRealmDefaultPrivilegeAsync(string privilege, CancellationToken)`;
  `HttpKeycloakAdminClient` sends `GET admin/realms/{realm}/roles/{privilege}` then
  `DELETE admin/realms/{realm}/roles/default-roles-{realm}/composites` with `[<that representation>]`,
  `EnsureSuccessStatusCode()` on both, **no early-return branch** (the provider is idempotent).
  Guard: `Ensure.That(privilege).IsNotNull().IsNotNullOrWhiteSpace()` (ADR-0105).
- **FR-002** An `IMigrator` in `src/MigrationRunner` calls it with `"offline_access"`; registered `AddScoped` **above**
  `builder.AddCameraCatalogPersistence()` in `Program.cs`, with a one-line *why* (it depends on the realm, not on
  Postgres).
- **FR-003** Failure is not caught in the migrator. `MigrationRun` already turns it into `Failed` + a log naming
  `ContextName`.
- **FR-004** A 403 from the narrowing names its likely cause in the failure, in the style of
  `KeycloakProvisionedFabSource`'s verdicts: the running realm predates the `manage-realm` grant — a persistent dev
  Keycloak keeps its old realm until its data volume is dropped. *(Assumption A-2: the only reason this would fire
  on a correctly provisioned realm is a stale volume; the wording is the lane's, not the ADR's.)*
- **FR-005** Realm file: add `"manage-realm"` to `service-account-migration-runner`'s `realm-management` roles;
  rewrite the `migration-runner` description to say it reads `/fabs` **and** narrows the realm's default
  privileges (ADR-0181), ≤ 255 chars. Nothing else in the file changes. The file stays UTF-8 with BOM.
- **FR-006** Prose corrected, no assertion changed — the four sites in §2's "Wrong prose" row (and `auth.ts` only if
  phase 4 finds a claim of inheritance there).
- **FR-007** `KioskInheritedPrivilegeIntegrationTests:63` **inverts** (does not hold it). The PR body states that this
  is not a weakened gate: its anti-vacuity duty passes to the wall test, widened to all four (ADR-0181 note 5).
- **FR-008** A comment on **#2878**: `migration-runner`'s secret (`dev-only-migration-runner-secret`, one of the six
  service identities option B templates) now guards `manage-realm`, so its production replacement is no longer a
  read-only concern. `migration-runner` is **not** a drop-list entry (production needs it). No client is added.

## 6. Independent end-to-end test procedure

1. CI (Docker): the Aspire fixture boots `E2ETests` mode → throwaway Keycloak, fresh import every run
   (`DeveloperLanePersistence` applies only to the opt-in persistent dev stack). `KioskInheritedPrivilegeIntegrationTests`
   proves AS-1/2/4/5/7/9; the fixture booting proves AS-6 and that the migrator succeeded (AS-8's success half).
2. CI e2e: `e2e/wall-outlives-its-session.spec.ts` unmodified, green (AS-3).
3. Manual (phase 5, Docker up): boot the AppHost, then with a master `admin-cli` token: `GET
   /admin/realms/smart-sentinel-eye` → `defaultRole.name`; `GET …/roles/default-roles-smart-sentinel-eye/composites`
   → no `offline_access`; `POST …/users` a throwaway, `GET …/users/{id}/role-mappings/realm/composite` → no
   `offline_access`; `wall-munich` → has it; delete the throwaway. **The verification note names the realm it ran
   against** (fixture-fresh vs a dropped/recreated dev volume) — ADR-0181 note 8.

## 7. Consequences the PR must state

- **A persistent dev stack fails its boot after this merges until its `keycloak-data` volume is dropped**: its realm
  predates the grant, so the narrowing answers 403 and, per ADR-0181 §3, the migrator fails. ADR-0181 note 8 says the
  migrator half "reaches it on every boot" — it reaches it, and fails, by design. Coordinate the drop with #91.
  CI and the integration fixture are unaffected (fresh import).
- Unchanged, said so: `RealmIdentityTests.cs:77` (`ProvidedByKeycloak = ["offline_access"]` is the client **scope**);
  `KioskPrivilegeSweep` and enrolment's strip (now remove nothing privileged; kept, ADR-0181 §5).
- **Not demonstrated:** whether an offline token already minted by a hand-made account before the narrowing keeps
  refreshing. No such account exists in any realm this system has (spec 080 §2.2); recorded, not tested.

## 8. Success criteria

- **SC-001** A hand-made person account and a hand-made service account in a fresh fixture realm hold no
  `offline_access` (red before, green after).
- **SC-002** All four wall accounts hold it; `operator` holds exactly `user`.
- **SC-003** `realm.defaultRole` is the un-suffixed composite; no `-1` sibling.
- **SC-004** `e2e/wall-outlives-its-session.spec.ts` green, unmodified.
- **SC-005** Every planted violation in plan §6 turns its named test red, quoted in the PR.

## 9. Assumptions, marked

- **A-1** Keycloak 26.6 import semantics as measured by spec 080; an upgrade re-opens §2.
- **A-2** FR-004's wording assumes a 403 on a correctly imported realm means a stale volume.
- **A-3** Whether `GET /admin/realms/{realm}` and `GET /roles` need `view-realm` for the anti-decoy reads is
  unmeasured for `identity-admin`; the test uses a principal that can read (plan §5). If the fixture exposes the
  master bootstrap admin, prefer it, so the observer is not the subject's own credential.
