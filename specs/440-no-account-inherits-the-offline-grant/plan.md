# Plan 440 — No account inherits the offline grant

**Spec:** [spec.md](spec.md) · **Issue:** #1995 · **ADR:** ADR-0181 (Accepted) · **Phase:** 2
**Constitution check:** no new architecture — ADR-0181 decides the mechanism, holder and failure mode; this plan
places it. No `[NEEDS CLARIFICATION]`. No new ADR needed.

## 1. Shape: no domain, one port method, one migrator, one realm edit

Reused from spec 080's plan §1-2 and confirmed at `29968a0d`. There is no aggregate, value object or event —
realm role composition is provider configuration, and inventing a domain model would be speculative generality
(ADR-0036). Constitution §II (primitive ban) binds domain models only; nothing here is one.

| Layer | Change |
|---|---|
| `Identity/Domain`, `Identity/Api`, `Shared.Contracts` | none |
| `Identity/Application/KeycloakAdmin/IKeycloakAdminClient.cs` | + `RemoveRealmDefaultPrivilegeAsync(string privilege, CancellationToken cancellationToken)`; prose of `StripInheritedRealmRolesAsync` corrected |
| `Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.Realm.cs` | the implementation (two calls) — this partial already holds realm-level reads; prose at `HttpKeycloakAdminClient.cs:85-89` corrected |
| `MigrationRunner/RealmDefaultPrivilegeMigrator.cs` (new) | `IMigrator`, `ContextName` naming the realm privilege step |
| `MigrationRunner/Program.cs` | `AddScoped<IMigrator, RealmDefaultPrivilegeMigrator>()` above `AddCameraCatalogPersistence()` |
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | `manage-realm` + description (contention file — one PR at a time) |

**Why `MigrationRunner` and not Identity.Infrastructure**: ADR-0181 §3 names it; the migrator is composition-root
glue that only calls an Identity port, the same reason `KeycloakProvisionedFabSource` lives there
(`BoundaryTests.cs:86-104`). No cross-context reference is added — MigrationRunner already references Identity.

## 2. The port and adapter

```
GET    admin/realms/{realm}/roles/{privilege}                         → 200, role representation (id, name, …)
DELETE admin/realms/{realm}/roles/default-roles-{realm}/composites    body [ representation ] → 204
```

- `{realm}` from `KeycloakAdminOptions.Realm` (as every sibling); `privilege` URL-escaped like siblings do.
- `EnsureSuccessStatusCode()` on each. **No pre-read of the composite, no early return** — the provider is idempotent
  (204 when absent) and a guard would be unreachable (spec 080 plan §3). Say so at the call site, with the measured
  fact that this endpoint accepts the `/roles/{name}` representation (contrast with the read-back
  `StripInheritedRealmRolesAsync` needs).
- The 403 case (FR-004): the adapter stays a translator (ADR-0141) — it throws `HttpRequestException` as
  `EnsureSuccessStatusCode` does; the **migrator** catches nothing. FR-004's verdict is attached by the migrator
  with an exception filter on `HttpRequestException { StatusCode: Forbidden }` that **rethrows wrapped**
  (`InvalidOperationException(verdict, inner)`), never swallows. If the engineer finds that adds a branch without
  value, the alternative is a `[LoggerMessage]` at Critical before rethrow — either way the run fails.
- Response bodies are not logged (they carry nothing secret, but the token handler's header must never be).

## 3. The migrator

```csharp
internal sealed class RealmDefaultPrivilegeMigrator(IKeycloakAdminClient keycloak, ILogger<…> logger) : IMigrator
{
    public string ContextName => "Identity realm default privileges";
    public async Task RunAsync(CancellationToken cancellationToken) => /* RemoveRealmDefaultPrivilegeAsync("offline_access", …) */;
}
```

The privilege name is a `const` with a *why* comment (ADR-0181 §1). Registered **first** and **scoped**
(`IKeycloakAdminClient` is scoped; `MigrationRun` resolves from a scope, `MigrationRun.cs:42-46`).

## 4. Invariants (system, not domain)

| Invariant | Held by | Asserted by |
|---|---|---|
| The default composite in force carries no `offline_access` after boot | the migrator, every boot | AS-1, AS-4 (integration) |
| `realm.defaultRole` is `default-roles-smart-sentinel-eye`, no `-1` sibling | the realm file **not** declaring it | AS-4 (integration — an absence, so asserted against the running realm, ADR-0181 §4) |
| Exactly the four `wall-*` accounts hold `offline_access` | four `realmRoles` entries | AS-2 (running) + existing `WallDisplayAccountTests` (file) |
| `manage-realm` is held by `migration-runner` alone | realm file | AS-7 (file: new architecture fact; running: identity-admin 403) |
| A failed narrowing never yields a booted system | no catch in migrator; `MigrationRun` → `Failed`; services `WaitForCompletion` | AS-8 (unit) |

## 5. Tests (phase 4a, red — written by test-writer, observed failing, output quoted in the PR)

To make red an **assertion** failure rather than a compile error, test-writer adds the interface member and the
migrator class as stubs that `throw new NotImplementedException()`, and the member to the nine doubles (recording or
`NotSupportedException`, per each double's existing style). The engineer may not edit these tests.

**T-A Docker-free, adapter** — `tests/Identity.Infrastructure.Tests/KeycloakAdmin/RealmDefaultPrivilegeRemovalTests.cs`,
`StubKeycloakHandler` seam (precedent `FabGroupTreeTests`):
1. sends exactly `GET …/roles/offline_access` then `DELETE …/roles/default-roles-smart-sentinel-eye/composites`, body
   a one-element array carrying the GET's `id` and `name`;
2. a second call sends the identical two requests and succeeds (idempotency is the provider's; no pre-read, no
   early return);
3. 403 on DELETE throws; 403/404 on GET throws and **no DELETE is sent**.

**T-B Docker-free, migrator** — `tests/MigrationRunner.Tests/RealmDefaultPrivilegeMigratorTests.cs`, hand-written
recording fake of `IKeycloakAdminClient` (ADR-0052/0054):
1. one call with `"offline_access"`;
2. run twice → two calls, both succeed (the boot-every-time contract);
3. port throws (403 `HttpRequestException`) → `RunAsync` throws; via `MigrationRun.ExecuteAsync` → `Failed`, log
   names the `ContextName` (precedent `MigrationRunTests`); 403 message carries the FR-004 verdict.

**T-C Architecture (file facts)** — `tests/Architecture.Tests/MigrationRunnerClientTests.cs` (precedent
`IntegrationTestAdminClientTests` / `PasswordGrantClientTests`): description ≤ 255 and does not contain "Read-only";
`service-account-migration-runner` realm-management roles are exactly `{query-groups, view-users, manage-realm}`;
no other user/service account in the file holds `manage-realm`. (Not a substitute for T-D: the composite claim cannot
be made from the file.)

**T-D Integration, CI-only** (Docker is down locally; ADR-0103, Aspire fixture, fresh import) — all in
`KioskInheritedPrivilegeIntegrationTests` (already in `shard-2.filter`; no filter edit):
1. **inverted** `:63` → `An_account_the_provider_creates_does_not_hold_it` (same direct-create path), plus it still
   holds `user` and `uma_authorization` (narrowing removed one member, not the composite);
2. new: a **person** account created by `POST /users` does not hold it (the issue's literal case); delete in `finally`
   (RealmProbe gains a user-delete helper if none fits);
3. wall test widened to a `[Theory]` over all four;
4. `operator` holds **exactly** `["user"]`;
5. anti-decoy: `realm.defaultRole.name` exact, no `-1` role, composite lacks `offline_access` (reader per spec A-3);
6. re-run: `RemoveRealmDefaultPrivilegeAsync("offline_access")` as `migration-runner` (`dev-only-migration-runner-secret`)
   succeeds and the composite member set is identical before/after;
7. authority: the same call as `identity-admin` throws with 403 and the composite is unchanged.

Red: 1, 2, 5's composite clause and 6 fail on today's realm (6 fails 403 — no grant yet); 3, 4, 7 are characterisation
(green before and after — 7 pins identity-admin was not widened). The PR distinguishes the two.

**E2E**: `e2e/wall-outlives-its-session.spec.ts` unmodified.

## 6. Planted-violation counterfactuals (phase 4, quote each red in the PR, then revert)

| Plant | Must turn red |
|---|---|
| Remove the migrator registration from `Program.cs` | T-D 1, 2, 5 |
| Remove `manage-realm` from the realm file | fixture boot fails (migrations `Failed`) and T-C roles fact |
| Catch-and-log in the migrator instead of throwing | T-B 3 |
| Declare the complete `default-roles-smart-sentinel-eye` composite in the realm file | T-D 5 (`-1` exists / defaultRole renamed) and T-D 1 |
| Delete `"offline_access"` from one `wall-*` | T-D 3 for that fab and `WallDisplayAccountTests` |
| Add `manage-realm` to `service-account-identity-admin` | T-D 7 and T-C holder fact |
| Add a "read composite, return early if absent" branch to the adapter | T-A 2 |
| Target `default-roles-{realm}-1` or `offline-access` | T-A 1 |
| Pad the description past 255 / leave "Read-only" | T-C description fact (and, for >255, the fixture boot) |

## 7. Boundary, messaging, latency

- No messaging, no integration event, no `Shared.Contracts` change. No cross-context reference.
- Latency: N/A — boot-time, before any API starts; two extra admin calls per boot.
- Coverage (ADR-0065): one Application interface member; Infrastructure/MigrationRunner ungated.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Realm edit breaks the import → fixture hangs | one array entry + a ≤ 255 description; T-C; the fixture boot itself |
| Persistent dev stacks fail to boot after merge | by design (ADR-0181 §3); FR-004 names the fix; PR + #91 coordination |
| Reviewer reads the inversion as a weakened gate | FR-007 argument in PR; widened wall Theory carries anti-vacuity |
| `manage-realm` on a dev-default secret | overridable parameter exists (`AppHost.cs:35`, `RealmImportMirrorTests.ProductionDefaults`); #2878 comment (FR-008); no production deployment (ADR-0130) |
