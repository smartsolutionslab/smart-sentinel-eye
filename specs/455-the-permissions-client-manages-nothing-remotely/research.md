# Research 455: what Keycloak 26.6.4 does with the `admin-permissions` client on import

**Issue:** #3121 · **Measured 2026-10-25** against `develop` at `fc48ed75`.

## Method

Three throwaway containers `spike-3121-v0|v1|v2` = `quay.io/keycloak/keycloak:26.6.4` (the AppHost pin),
`start-dev --import-realm`, no volume, bootstrap `admin`/`admin`, ports 18190-18192, realm file mounted at
`/opt/keycloak/data/import` (so every result below is after a real import). All three logged
`KC-SERVICES0032: Import finished successfully`. Removed afterwards with `docker rm -f` (only these three; the
developer's Aspire stack was not touched). Probe scripts were Node `fetch` over the admin REST API and the token
endpoint; `M` = `6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11` (migration-runner).

| Variant | `admin-permissions` entry in the realm file |
|---|---|
| v0 | committed, unchanged |
| v1 | `serviceAccountsEnabled:false`, `standardFlowEnabled:false`, `defaultClientScopes:[]`, `optionalClientScopes:[]`, `authorizationSettings.allowRemoteResourceManagement:false` |
| v2 | v1 plus `bearerOnly:true` |

## R1. The CI fact and probe 5 of spec 440 are both true, and the CI test draws the wrong conclusion

Identical in v0, v1 and v2:

| Step | Result |
|---|---|
| master `GET clients/{ap}/client-secret` | 200 `{"type":"secret"}`, **no value** (what the CI fact asserts) |
| `client_credentials` with an empty secret | 401 `unauthorized_client` |
| identity-admin `GET clients/{ap}` / `GET .../client-secret` | 200 / 200 `{"type":"secret"}` |
| identity-admin `POST clients/{ap}/client-secret` | **200 with a value** |
| `client_credentials` with that value | **200**, `scope:""`, no `aud`, no roles |
| that token: `GET clients/M`, `GET clients/M/client-secret`, `GET /clients`, `GET /users` | 403, 403, 403, 403 |

The import stores no secret, so no token can be minted **until someone rotates one**, and identity-admin can
(its FGAP all-clients `manage`). `The_admin_permissions_client_has_no_secret_so_no_token_can_be_minted_for_it`
checks only the first half. The credential is latent, one `POST` away, and the matrix never probes it, which is
what ADR-0187 Decision 3 requires. Under ADR-0188's two-policy deny the token is refused on migration-runner.

## R2. Keycloak ignores the client-level fields of `admin-permissions` on import

Stored state read back as master (`GET clients/{ap}`, `.../default-client-scopes`, `.../optional-client-scopes`,
`.../service-account-user`, `.../authz/resource-server`):

| Field | v0 (file) -> stored | v1 (file) -> stored | v2 (file) -> stored |
|---|---|---|---|
| `serviceAccountsEnabled` | true -> true | **false -> true** | **false -> true** |
| `standardFlowEnabled` | true -> true | **false -> true** | **false -> true** |
| `bearerOnly` | false -> false | false -> false | **true -> false** |
| default client scopes | **`sse-identity`,`sse-audience` -> `[]`** | `[]` -> `[]` | `[]` -> `[]` |
| optional client scopes | **`offline_access` -> `[]`** | `[]` -> `[]` | `[]` -> `[]` |
| service-account user | `service-account-admin-permissions` | same | same |
| `allowRemoteResourceManagement` (authz settings) | true -> true | **false -> false** | false -> false |

No error and no warning in the import log for any variant. So the realm file **cannot** disable the service
account, the standard flow or make the client bearer-only, and the scope defaults #1995 added are discarded today:
the committed file claims three scopes the realm does not hold. Only the `authorizationSettings` block is honoured.
The FGAP deny is unaffected in all variants (identity-admin: `GET M` 403, `GET M/client-secret` 403,
`?clientId=migration-runner` 200 `[]`, `GET /clients` 200 n=17 without M; enrolment `POST /clients` 201 and its
secret readable 200).

## R3. A runtime switch-off works, and identity-admin can undo it

| Caller | `PUT clients/{ap}` | Result |
|---|---|---|
| master, `serviceAccountsEnabled:false` | 204 | stored false; `service-account-user` 400; rotate 200; mint **401** "Client not enabled to retrieve service account" |
| identity-admin, `serviceAccountsEnabled:false` | 204 | same |
| identity-admin, `serviceAccountsEnabled:true` (after the switch-off) | 204 | rotate 200, mint **200** |

Disabling at runtime would need a new post-import step (MigrationRunner holds `manage-realm`) and is reversible by
identity-admin through the same all-clients grant ADR-0186 already records as rewriting system clients. Not built.

## R4. With remote resource management on, the rotated credential reads the FGAP resource set

Token from R1 (v0, committed `allowRemoteResourceManagement:true`) against `/realms/{r}/authz/protection/*`:

| Request | v0 (true) | v1 (false) |
|---|---|---|
| `GET resource_set` | **200**, five ids | 400 `not_supported` "Remote management is disabled." |
| `GET resource_set/{id}` for each | **200**: `M`'s UUID, `Clients`, `Groups`, `Roles`, `Users` with their scopes | (not reachable) |
| `GET resource_set?name=<M>` | **200** | 400 `not_supported` |
| `POST resource_set` | 400 `unknown_error` | 400 `not_supported` |
| `PUT resource_set/{M}` narrowing its scopes | 400 `unknown_error`; identity-admin `GET M` still 403 | — |
| `DELETE resource_set/{M}` | 400 `unknown_error`; identity-admin `GET M` and secret still 403; resource still present | — |
| `POST uma-policy/{M}` | 400 "Only resources with owner managed accessed can have policies" | — |

Writes are refused by the FGAP resource server either way; v0 discloses the FGAP model (resource names, the pinned
UUID, scope sets) to a principal identity-admin can create. `false` closes the endpoint and changes nothing else
measured (R2 last paragraph).

## R5. The committed fix, imported byte-identical (phase 4b)

Container `spike-3121-b-1` (`quay.io/keycloak/keycloak:26.6.4`, `start-dev --import-realm`, no volume, port 18195),
realm file mounted read-only; `sha256sum` inside the container equals the repo file
(`340b7081f112ff3a886768c4a76b826dbec0e86eefc2c61074222e2a6932eacf`). Log: `KC-SERVICES0032: Import finished
successfully`. Removed afterwards (only this container).

| Step (caller) | Status |
|---|---|
| identity-admin `client_credentials` mint | 200 |
| (a) identity-admin `POST clients/{ap}/client-secret` | 200 |
| (a) `client_credentials` as admin-permissions with that secret | 200 |
| (a) that token `GET /realms/smart-sentinel-eye/authz/protection/resource_set` | **400** `{"error":"not_supported","error_description":"Remote management is disabled."}` |
| (b) same token `GET clients/{migration-runner}` | 403 |
| (b) same token `PUT clients/{migration-runner}` | 403 |
| (b) same token `GET clients/{migration-runner}/client-secret` | 403 |
| (b) same token `POST clients/{migration-runner}/client-secret` | 403 |
| (c) identity-admin `POST /clients` (new service-account client) | 201 |
| (c) `GET .../client-secret` | 200 |
| (c) `POST .../client-secret` (regenerate) | 200 |
| (c) `PUT clients/{new}` | 204 |
| (c) `GET .../service-account-user` | 200 |

## R6. The flag gates `resource_set` only, not the rest of the protection API (phase 4b review)

Container `spike-3121-c-1` (`quay.io/keycloak/keycloak:26.6.4`, `start-dev --import-realm`, no volume, port 18196),
`src/AppHost/Realms` mounted read-only, the committed realm (`allowRemoteResourceManagement:false`);
`KC-SERVICES0032: Import finished successfully`. Same route as R5: identity-admin rotates `admin-permissions`, mints,
then the token calls the protection endpoints. Removed afterwards (only this container).

| Request (admin-permissions token) | Status | Body |
|---|---|---|
| `GET resource_set` | 400 | `{"error":"not_supported","error_description":"Remote management is disabled."}` |
| `POST permission` body `[]` | 400 | `{"error":"invalid_permission_request",...}` |
| `POST permission` body naming an unknown resource id | 400 | `{"error":"invalid_resource_id",...}` |
| `GET permission/ticket` | **200** | `[]` |
| `GET uma-policy` | **200** | `[]` |

`allowRemoteResourceManagement:false` is checked only by the Keycloak 26.6.4 `ResourceService` (the `resource_set`
endpoints). Permission tickets and UMA policies are reachable with the token; they answer empty or 400 here because
the FGAP resource server owns no tickets and no owner-managed resources (R4: `uma-policy` writes were refused for that
reason), not because the flag closes them. FR-2 is therefore narrowed to `resource_set`, and the probes pin these
answers so a Keycloak change (for instance the tickets listing starting to return rows) fails a test.
