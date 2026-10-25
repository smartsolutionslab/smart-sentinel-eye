# Plan 455: The permissions client manages nothing remotely

**Spec:** [spec.md](spec.md) · **Research:** [research.md](research.md) · **Issue:** #3121

## Constitution and ADR check

| Gate | Result |
|---|---|
| Composition root (AppHost) | Realm file only; the `WithRealmImport("../AppHost/Realms")` wiring is unchanged. |
| ADR-0186 Decision 5 | The `admin-permissions` `authorizationSettings` block keeps every policy and permission; one boolean changes. The research note that "`allowRemoteResourceManagement` imported fine unchanged" (spec 440) recorded the export, not a decision. |
| ADR-0187 Decision 3 | Applied as written: the mechanism's own credential joins the matrix (FR-3). |
| ADR-0188 | Unchanged; its guard facts in `IdentityAdminClientAuthorityTests` read policies, not this boolean. |
| ADR-0103 | Integration facts run on the Aspire fixture; the throwaway containers were research only. |
| Lane limits (ADR-0144) | No ADR written, no guard narrowed: the removed CI fact is replaced by a strictly stronger measurement (spec FR-3). |
| §IV latency | N/A. |

No bounded context, entity, value object, message or HTTP contract is touched. There is no domain model; the
"layers" are the realm file, a file-reading guard and a running-provider probe.

## Changes

### 1. Realm file (`src/AppHost/Realms/smart-sentinel-eye-realm.json`)

In the `admin-permissions` entry, `authorizationSettings.allowRemoteResourceManagement`: `true` → `false`. Keep the
file's BOM and formatting; a one-token diff. Nothing else in that entry (R2: Keycloak forces or discards it).

**Developer lane:** a persistent dev Keycloak keeps its volume and does not re-import (AppHost note on
`WithKeycloakDeveloperLanePersistence`); delete the volume to observe the change locally. CI boots fresh.

### 2. Realm-file guard (`tests/Architecture.Tests/IdentityAdminAllClientsGrantTests.cs`)

Add to the existing class (same subject: the `admin-permissions` authorization settings; Architecture.Tests are not
sharded):

- `The_permissions_client_does_not_allow_remote_resource_management` — reads `RealmGrantGraph.ReadRealm()`, finds the
  `admin-permissions` client, asserts `authorizationSettings.allowRemoteResourceManagement` is present **and** `false`
  (absent is a failure: Keycloak's default for a new resource server is not this repo's to assume).
- Counterfactuals on the in-memory realm, as the class's other facts do: set to `true` → seen; remove the property →
  seen. The message cites spec 455 R4 (the protection API discloses the FGAP resource set).

### 3. Confinement matrix (`tests/Integration.Tests/Identity/MigrationRunnerConfinementIntegrationTests.cs`)

- Delete `The_admin_permissions_client_has_no_secret_so_no_token_can_be_minted_for_it` and the comment block above it
  that justifies leaving the principal out.
- `[InlineData("admin-permissions")]` on `Every_principal_is_refused_the_migration_runner_client_and_its_secret`.
- `MintPrincipalTokenAsync`: case `"admin-permissions"` → `MintByRotationAsync("admin-permissions")`: as identity-admin
  (`realm.AuthorisedAdminClientAsync`) look the client up by `clientId`, `POST .../client-secret`, read `value`, then
  `ClientCredentialsAsync`. Rotating as **identity-admin** is the point: it proves the route R1 measured, not merely
  that a master admin could. No restore: Keycloak offers no way to clear a secret, and the matrix itself proves the
  token left behind is denied.
- New fact `A_rotated_permissions_client_credential_is_refused_the_protection_api` — mint the same way, `GET
  /realms/{realm}/authz/protection/resource_set` with the bearer, assert 400 and `error == "not_supported"`. Asserting
  the error code, not only the status, so a 400 for some other reason (e.g. a malformed request) cannot pass it.
  Existing class → no `ci-shards/*.filter` change.

### 4. The two scope guards

Untouched (spec FR-4). Recorded for Q2: they pass on declarations the import discards (R2).

## Agents

- Phase 4a: `test-writer` writes §2 and §3, runs them, returns verbatim output (guard red, protection-API fact red,
  matrix row green as the spec predicts).
- Phase 4b: `infra-engineer` makes §1 only; may not edit the tests.
- Phase 6: `infra-reviewer` (realm import, CI proof) and `security-reviewer` (FGAP, credential surface), both mandatory.

## Risks

- **Fixture order.** After the matrix row runs, `admin-permissions` holds a secret for the rest of the CI run. No other
  test reads that secret once the premise fact is gone (grep `admin-permissions` in `tests/` before 4b).
- **A future Keycloak** may start honouring the client-level fields (R2). Nothing here depends on their being ignored;
  Q2/Q3 would then be worth revisiting.
