# ADR-0181: The default privilege set is narrowed on every boot after import, by the migration runner, which holds `manage-realm` permanently

**Status:** **Accepted** by the owner on PR #3085, 2026-10-22 (the owner ruled on 2026-09-05, 2026-10-21 and 2026-10-22 on #1995; this records the ruling)
**Date:** 2026-10-21
**Amended by:** ADR-0186 (accepted 2026-10-23) — Decision 2: `migration-runner` authenticates by signed JWT, not a client secret, and `identity-admin` administers clients through FGAP v2 with migration-runner denied; the permanent `manage-realm` grant is unchanged.
**Supersedes:** ADR-0134 (everything in ADR-0134 except the parts named below stands and is carried forward unchanged)
**Corrects:** ADR-0132 (one recorded measurement: "discarded on import")
**Relates to:** ADR-0067, ADR-0130, ADR-0131, ADR-0133, ADR-0144, spec 052, spec 080, issues 1976, 1995

## Context

Keycloak gives every account created **after** realm import the composite role
`default-roles-smart-sentinel-eye`, and that composite contains `offline_access`: the privilege that mints
a refresh token of type *Offline* with no expiry. Accounts declared in the realm file receive exactly the
roles they name and inherit nothing.

ADR-0134 contained the privilege on the path the system drives: enrolment strips it from every
runtime-created kiosk, device and webhook client (`StripInheritedRealmRolesAsync`). It recorded the rest
as an accepted negative, *"Accounts created by hand: still inherit it (issue 1995)"*, and declined the
total fix in its Alternatives: *"Narrow the provider's default privilege set. Total, and needs authority
broader than the privilege it contains."*

On 2026-09-05 the owner decided **least privilege**: remove `offline_access` from the composite so no
account inherits it by existing, and grant it by name to the four `wall-*` accounts. Spec 080
(branch `fix/1995-offline-access-is-granted-not-inherited`, `ede331a6`) then measured, against fresh
Keycloak 26.6 containers with no reused volume, what that takes:

**The realm file cannot do it** (spec 080 §2.3):

| Declaring the composite in the realm file | Result |
|---|---|
| naming a role the file does not declare (`uma_authorization`) | **Keycloak refuses to boot**: `Unable to find composite realm role`, exit 1, and the Aspire fixture hangs |
| with every referenced role also declared | boots; the declared role is stored as written, Keycloak creates **`default-roles-smart-sentinel-eye-1`** with `offline_access` in it and binds `realm.defaultRole` to *that*. New accounts still inherit. **A silent decoy**: the role read by its expected name looks correct |

**The admin API can, and only with `manage-realm`** (spec 080 §2.4, `DELETE
/roles/default-roles-smart-sentinel-eye/composites`):

| Principal | GET composites | DELETE composite |
|---|---|---|
| `identity-admin` as declared | 403 | 403 |
| + `realm-management/view-realm` | 200 | 403 |
| + `realm-management/manage-realm` | 200 | **204** |

The `DELETE` is idempotent (204 whether or not the member is present), and the composite resolves at
evaluation time, so it applies to accounts created before it too. After it, a new account no longer holds
`offline_access` and `wall-munich` still does, because the wall grants resolve against the realm role,
which Keycloak creates unconditionally, not against the composite.

**ADR-0134 was right that `manage-realm` is broad**: it is authority over roles, session lifetimes and
authentication flows alike. On 2026-10-21 the owner accepted that trade, for one holder, and on
2026-10-22 confirmed it is **permanent**: the grant is never revoked after the narrowing, because the
narrowing runs again on every boot.

## Decision

**1. Least privilege, as decided on 2026-09-05.** `offline_access` is removed from the realm's default
composite. The four `wall-*` accounts keep it as a **named** grant (already true in the realm file:
`"realmRoles": ["user", "offline_access"]` on each). A fifth account that needs it is a realm-file edit
someone has to justify.

**2. `manage-realm` is granted to the `migration-runner` service account permanently, and to nothing
else.** It joins that account's `realm-management` client roles (today `query-groups`, `view-users`) and
stays there; there is no grant-then-revoke. **This privilege is broader than the one it removes**:
`offline_access` is one realm role, `manage-realm` is authority over the whole realm's configuration. It
is accepted only because it is **confined to the one-shot migration worker**, the trade ADR-0134 had
declined and the owner has now taken. `migration-runner` is the narrowest available holder:

- it is a one-shot worker with no HTTP surface that exits before any API starts (ADR-0067; every service
  `WaitForCompletion`s on it);
- `identity-admin` was rejected as the holder because the live, gateway-reachable Identity API uses it for
  enrolment, device registration and webhook rotation;
- the master-realm bootstrap admin was rejected because it holds authority over every realm.

**3. The narrowing runs after import, as an `IMigrator`, on every boot.** It is **not** a one-time step:
it is idempotent and runs on every `MigrationRunner` start, because a fresh realm (every fixture boot,
every new deployment) re-creates the composite with `offline_access` in it. A failed narrowing **fails the
migrator and therefore the boot**: a swallowed 403 would leave the system running with every new account
privileged.

**4. The realm file does not declare the composite, and must not.** The invariant "`realm.defaultRole` is
`default-roles-smart-sentinel-eye` and no `-1` sibling exists" is held by an absence, so it is asserted
against the running realm.

**5. Enrolment's per-account strip stays.** It now removes nothing privileged, and it costs two calls; it
is what catches drift if the composite is ever widened again by hand.

**6. Security review is mandatory.** Every slice under this ADR runs `security-reviewer` at phase 6 in the
autonomous lane; the slice is not mergeable without it.

**Carried forward from ADR-0134 unchanged:** the `kiosk-wall` client and its five read scopes, the
optional (never default) `offline_access` client scope, `not_allowed` as a refusal (the ADR-0133
amendment), the cost table except its last two rows, and every "not demonstrated" item.

**Changed from ADR-0134:** its cost-table row *"Accounts created by hand: still inherit it"* becomes
*"no account inherits it; four hold it by name"*; its Alternatives entry *"Narrow the provider's default
privilege set"* is **taken**; and its Negative *"an account created by hand is not covered"* is retired.

### The correction to ADR-0132

ADR-0132 (§ "The realm file cannot express the fix") records that a declared composite *"was tried: the
import discarded it wholesale"*, and `tests/Architecture.Tests/WallDisplayAccountTests.cs:33-38` repeats
*"discarded on import, which was tried and verified"*. **On Keycloak 26.6 that is false.** The import
either aborts the boot or honours the declaration and orphans it behind a `-1` decoy (table above).
ADR-0132's **conclusion** survives (the realm file cannot close this; it needs a step after import); its
**measurement** does not. The worse failure is the decoy, because it passes any check that reads the
role by name.

## Consequences

**Positive: the privilege is held by exactly four named accounts**, on every path including one this
system does not drive. The issue ADR-0134 left open is closed.

**Positive: verifiable against the running realm.** A new account's effective roles answer the
question directly, which a realm-file guard cannot.

**Negative: a realm-wide authority now sits, permanently, on a service account.** The privilege granted
is broader than the one removed: `manage-realm` lets `migration-runner`'s credential change session
lifetimes, roles and flows. It is confined to a one-shot worker with no HTTP surface, which is the only
reason it is acceptable. Its secret is a dev value in the realm file (`dev-only-migration-runner-secret`)
like every other client's; a production deployment must provision it as a secret, and there is no
production deployment yet (ADR-0130). This is the trade ADR-0134 declined and the owner has now taken.

**Negative: the step must keep running.** If a future change removes the migrator or the grant, the
composite silently regains `offline_access` on the next fresh realm. The integration test is what notices.

**Neutral: the 30-day idle bound is still a vendor default.** `offlineSessionIdleTimeout` is not set in the
realm file and no test asserts it (spec 080; #1995 comments). Unchanged here.

## Alternatives Considered

**Declare the narrowed composite in the realm file.** Boot failure or silent decoy (measured). Rejected.

**Grant `manage-realm` to `identity-admin`.** Same result, wider blast surface: a live API's credential.
Rejected.

**An AppHost hook with the master bootstrap admin.** No realm account widened, but authority over every
realm. Kept only as the fallback spec 080 names if this ADR were refused.

**Periodic reconciliation with `manage-users`** (no new authority). Needs a rule for which accounts
outside this system's creation path may hold an offline grant, and points a destructive strip at human
accounts. Not chosen.

**Leave it** (ADR-0134's position). Inert only while a single optional scope on one client is the way in;
the owner chose least privilege instead.

## Implementation Notes

What the autonomous lane builds after approval (#1995 re-enrolled; spec 080 is the resumption point;
behaviour-changing, phase 4a **red**; `infra-engineer`; **`security-reviewer` mandatory** at phase 6).

1. **Realm file** (`src/AppHost/Realms/smart-sentinel-eye-realm.json`, a contention file, one PR at a
   time): `manage-realm` added to `service-account-migration-runner`'s `realm-management` roles; the
   `migration-runner` client description, which today says *"Read-only"*, rewritten to say what it now does
   and kept **under 255 characters** (a longer one kills the import). Nothing else in the file changes.
2. **Port**: `IKeycloakAdminClient.RemoveRealmDefaultPrivilegeAsync(privilege, cancellationToken)`;
   `HttpKeycloakAdminClient` does `GET roles/{privilege}` then `DELETE roles/default-roles-{realm}/composites`
   with that representation, `EnsureSuccessStatusCode()`, no early-return branch (the provider is idempotent).
3. **Migrator**: an `IMigrator` in `MigrationRunner` calling it with `"offline_access"`, registered
   **first** (it depends on the realm, not on Postgres).
4. **Tests** in `tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs` (Aspire
   fixture, fresh import each run): a new account does **not** hold `offline_access`; each `wall-*` does;
   `operator` holds exactly `user`; `realm.defaultRole.name` is `default-roles-smart-sentinel-eye` and no
   `-1` role exists; the migrator re-run is a no-op.
5. **The inverted test.** `An_account_the_provider_creates_holds_it_until_something_removes_it` (`:63`)
   asserts the world this ADR changes and **inverts** to "does not hold it". This is not a weakened gate:
   its anti-vacuity duty passes to `A_wall_display_account_does_hold_it` (`:115`), which still proves the
   privilege exists and is held. The PR body must say so (ADR-0144).
6. **Prose corrected, no assertion changed**: `WallDisplayAccountTests.cs:22-38` (its four facts read the
   realm file and are unaffected), the class comment of `KioskInheritedPrivilegeIntegrationTests.cs`,
   `HttpKeycloakAdminClient.cs` and `IKeycloakAdminClient.cs` around `StripInheritedRealmRolesAsync`, and
   `apps/kiosk-web/src/app/auth.ts`'s note on the inherited privilege.
7. **Unchanged, and the PR should say so**: `RealmIdentityTests.cs:77` (`ProvidedByKeycloak =
   ["offline_access"]` is the client *scope*, not the realm role); `e2e/wall-outlives-its-session.spec.ts`
   must pass unmodified.
8. **Dev volume**: the long-lived dev Keycloak keeps its old realm; the realm-file half reaches it only after
   the `keycloak-data` volume is dropped (coordinate with #91). The migrator half reaches it on every boot.
   The verification note names the realm it ran against.
