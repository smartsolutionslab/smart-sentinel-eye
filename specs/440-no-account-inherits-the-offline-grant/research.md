# Research — spec 440 (T050 FGAP v2 export, T051 dev key pair)

Measured 2026-10-23 on `quay.io/keycloak/keycloak:26.6.4` (the AppHost's pinned tag), `start-dev --import-realm`,
no volume, throwaway containers `spike-t050-kc` / `spike-t050-kc2` (removed afterwards).

## T050 — FGAP v2 as Keycloak writes it

### Steps (admin REST, realm `smart-sentinel-eye`, bootstrap `admin`/`admin` in the master realm)

1. Copy `src/AppHost/Realms/smart-sentinel-eye-realm.json` and add `"id": "6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11"`
   to the `migration-runner` client (**the file starts with a UTF-8 BOM** — strip it before `JSON.parse`, and keep it
   when editing the real file). Mount that directory at `/opt/keycloak/data/import`, `start-dev --import-realm`.
   Confirmed: the pinned id is honoured (`GET /clients?clientId=migration-runner` returns it).
2. `PUT /admin/realms/smart-sentinel-eye` `{"adminPermissionsEnabled": true}` -> 204. This **creates** the
   `admin-permissions` client (`authorizationServicesEnabled: true`) with the four resource types
   (`Clients`, `Groups`, `Roles`, `Users`) and their scopes. No policies or permissions exist yet.
3. Remove `manage-clients` and `view-clients` (`realm-management`) from `service-account-identity-admin`:
   `DELETE /users/{id}/role-mappings/clients/{realm-management-uuid}` with the two role representations -> 204.
4. Under `/clients/{admin-permissions-uuid}/authz/resource-server`:
   - `POST policy/user` `{"name":"identity-admin-user","logic":"POSITIVE","users":["<identity-admin SA user id>"]}` -> 201
   - `POST policy/user` `{"name":"identity-admin-user-deny","logic":"NEGATIVE","users":["<same id>"]}` -> 201
   - `POST permission/scope` `{"name":"identity-admin-all-clients","resourceType":"Clients","scopes":["view","manage"],"policies":[<positive id>]}` -> 201
   - `POST permission/scope` `{"name":"deny-migration-runner","resourceType":"Clients","resources":["6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11"],"scopes":["view","manage","map-roles","map-roles-composite","map-roles-client-scope"],"policies":[<negative id>]}` -> 201.
     Posting a client UUID in `resources` makes Keycloak **create the per-client resource itself**, named by the UUID.
5. Behaviour check with an `identity-admin` `client_credentials` token: `GET /clients/{migration-runner}` 403,
   `.../client-secret` 403, `GET /clients?clientId=migration-runner` 200 with **0** results, `GET /clients` 200 with
   17 clients and none of them migration-runner.
6. Export: `POST /admin/realms/smart-sentinel-eye/partial-export?exportClients=true&exportGroupsAndRoles=false`
   (REST partial export; `kc.sh export` was not needed). `adminPermissionsEnabled` is `true` at realm level.

### Round trip (proves the fragment is importable and sufficient)

A second throwaway container imported: the original realm file + pinned migration-runner id +
`"adminPermissionsEnabled": true` + `identity-admin` without `manage-clients`/`view-clients` + the **whole exported
`admin-permissions` client below with its `id` removed**. Import finished with no error; the same step-5 checks gave
the same results (403/403/0/17); re-exporting gave an `authorizationSettings` identical except for ordering of
`scopes` arrays and of `resources[].scopes`.

### Facts the guard and the realm file depend on

- The realm needs `"adminPermissionsEnabled": true` (boolean, realm level) **and** a client entry
  `clientId: "admin-permissions"` in `clients` carrying `authorizationSettings`.
- Policies and permissions are **all in `policies`** (a permission is a policy with `type: "scope"`); there is no
  separate `permissions` array. They carry **no `id`** in the export, and cross-references are **by name**.
- Every `config` value is a **JSON-encoded string** holding an array (`"users"`, `"resources"`, `"scopes"`,
  `"applyPolicies"`), never a real array. User policies name users by **username**
  (`service-account-identity-admin`), although the REST API took user ids.
- The per-client resource is `{"name": "<client uuid>", ...}` with **no `type` and no `_id`**; the permission names it
  in `config.resources` by that UUID and sets `config.defaultResourceType: "Clients"`. The all-clients permission names
  the resource `"Clients"` in `config.resources`.
- A permission's own `logic` stays `POSITIVE`; the DENY is the **applied user policy's** `NEGATIVE` logic. One user
  policy cannot serve both the grant and the deny, so two user policies are required (a NEGATIVE policy applied to the
  all-clients permission would deny every client).
- The per-client UUID in the permission must equal the `id` pinned on the `migration-runner` client entry.
- `authorizationSchema` and `allowRemoteResourceManagement` are written by the export; both imported fine unchanged.

### Exported `admin-permissions` client — verbatim (`id` is the throwaway's; drop it for the realm file)

```json
{
  "id": "e8b35f71-68f8-4b30-a03b-32658196a9e2",
  "clientId": "admin-permissions",
  "surrogateAuthRequired": false,
  "enabled": true,
  "alwaysDisplayInConsole": false,
  "redirectUris": [],
  "webOrigins": [],
  "notBefore": 0,
  "bearerOnly": false,
  "consentRequired": false,
  "standardFlowEnabled": true,
  "implicitFlowEnabled": false,
  "directAccessGrantsEnabled": false,
  "serviceAccountsEnabled": true,
  "authorizationServicesEnabled": true,
  "publicClient": false,
  "frontchannelLogout": false,
  "protocol": "openid-connect",
  "attributes": {
    "realm_client": "false"
  },
  "authenticationFlowBindingOverrides": {},
  "fullScopeAllowed": false,
  "nodeReRegistrationTimeout": 0,
  "defaultClientScopes": [],
  "optionalClientScopes": [
    "offline_access"
  ],
  "authorizationSettings": {
    "allowRemoteResourceManagement": true,
    "policyEnforcementMode": "ENFORCING",
    "resources": [
      {
        "name": "Roles",
        "type": "Roles",
        "ownerManagedAccess": false,
        "attributes": {},
        "uris": [],
        "scopes": [
          {
            "name": "map-role-composite"
          },
          {
            "name": "map-role-client-scope"
          },
          {
            "name": "map-role"
          }
        ]
      },
      {
        "name": "Groups",
        "type": "Groups",
        "ownerManagedAccess": false,
        "attributes": {},
        "uris": [],
        "scopes": [
          {
            "name": "manage-members"
          },
          {
            "name": "view"
          },
          {
            "name": "manage-membership-of-members"
          },
          {
            "name": "manage-membership"
          },
          {
            "name": "view-members"
          },
          {
            "name": "manage"
          },
          {
            "name": "impersonate-members"
          }
        ]
      },
      {
        "name": "Users",
        "type": "Users",
        "ownerManagedAccess": false,
        "attributes": {},
        "uris": [],
        "scopes": [
          {
            "name": "view"
          },
          {
            "name": "manage-group-membership"
          },
          {
            "name": "impersonate"
          },
          {
            "name": "map-roles"
          },
          {
            "name": "reset-password"
          },
          {
            "name": "manage"
          }
        ]
      },
      {
        "name": "Clients",
        "type": "Clients",
        "ownerManagedAccess": false,
        "attributes": {},
        "uris": [],
        "scopes": [
          {
            "name": "view"
          },
          {
            "name": "map-roles-client-scope"
          },
          {
            "name": "map-roles"
          },
          {
            "name": "manage"
          },
          {
            "name": "map-roles-composite"
          }
        ]
      },
      {
        "name": "6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11",
        "ownerManagedAccess": false,
        "attributes": {},
        "uris": [],
        "scopes": [
          {
            "name": "view"
          },
          {
            "name": "map-roles-client-scope"
          },
          {
            "name": "map-roles"
          },
          {
            "name": "manage"
          },
          {
            "name": "map-roles-composite"
          }
        ]
      }
    ],
    "policies": [
      {
        "name": "identity-admin-user",
        "type": "user",
        "logic": "POSITIVE",
        "decisionStrategy": "UNANIMOUS",
        "config": {
          "users": "[\"service-account-identity-admin\"]"
        }
      },
      {
        "name": "identity-admin-user-deny",
        "type": "user",
        "logic": "NEGATIVE",
        "decisionStrategy": "UNANIMOUS",
        "config": {
          "users": "[\"service-account-identity-admin\"]"
        }
      },
      {
        "name": "identity-admin-all-clients",
        "type": "scope",
        "logic": "POSITIVE",
        "decisionStrategy": "UNANIMOUS",
        "config": {
          "defaultResourceType": "Clients",
          "resources": "[\"Clients\"]",
          "scopes": "[\"view\",\"manage\"]",
          "applyPolicies": "[\"identity-admin-user\"]"
        }
      },
      {
        "name": "deny-migration-runner",
        "type": "scope",
        "logic": "POSITIVE",
        "decisionStrategy": "UNANIMOUS",
        "config": {
          "defaultResourceType": "Clients",
          "resources": "[\"6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11\"]",
          "scopes": "[\"map-roles\",\"view\",\"map-roles-client-scope\",\"map-roles-composite\",\"manage\"]",
          "applyPolicies": "[\"identity-admin-user-deny\"]"
        }
      }
    ],
    "scopes": [
      {
        "name": "manage-group-membership"
      },
      {
        "name": "map-roles-client-scope"
      },
      {
        "name": "map-role"
      },
      {
        "name": "view-members"
      },
      {
        "name": "reset-password"
      },
      {
        "name": "manage"
      },
      {
        "name": "impersonate-members"
      },
      {
        "name": "manage-members"
      },
      {
        "name": "view"
      },
      {
        "name": "manage-membership-of-members"
      },
      {
        "name": "manage-membership"
      },
      {
        "name": "impersonate"
      },
      {
        "name": "map-role-composite"
      },
      {
        "name": "map-roles"
      },
      {
        "name": "map-role-client-scope"
      },
      {
        "name": "map-roles-composite"
      }
    ],
    "decisionStrategy": "UNANIMOUS",
    "authorizationSchema": {
      "resourceTypes": {
        "Roles": {
          "type": "Roles",
          "scopes": [
            "map-role",
            "map-role-composite",
            "map-role-client-scope"
          ],
          "scopeAliases": {}
        },
        "Groups": {
          "type": "Groups",
          "scopes": [
            "manage",
            "manage-membership",
            "view-members",
            "view",
            "impersonate-members",
            "manage-members",
            "manage-membership-of-members"
          ],
          "scopeAliases": {}
        },
        "Users": {
          "type": "Users",
          "scopes": [
            "view",
            "reset-password",
            "manage-group-membership",
            "manage",
            "map-roles",
            "impersonate"
          ],
          "scopeAliases": {
            "view": [
              "view-members"
            ],
            "manage-group-membership": [
              "manage-membership-of-members"
            ],
            "impersonate": [
              "impersonate-members"
            ],
            "manage": [
              "manage-members"
            ]
          },
          "groupType": "Groups"
        },
        "Clients": {
          "type": "Clients",
          "scopes": [
            "view",
            "map-roles-client-scope",
            "map-roles",
            "manage",
            "map-roles-composite"
          ],
          "scopeAliases": {}
        }
      }
    }
  }
}
```

### Exported `authorizationSettings` alone — verbatim

```json
{
  "allowRemoteResourceManagement": true,
  "policyEnforcementMode": "ENFORCING",
  "resources": [
    {
      "name": "Roles",
      "type": "Roles",
      "ownerManagedAccess": false,
      "attributes": {},
      "uris": [],
      "scopes": [
        {
          "name": "map-role-composite"
        },
        {
          "name": "map-role-client-scope"
        },
        {
          "name": "map-role"
        }
      ]
    },
    {
      "name": "Groups",
      "type": "Groups",
      "ownerManagedAccess": false,
      "attributes": {},
      "uris": [],
      "scopes": [
        {
          "name": "manage-members"
        },
        {
          "name": "view"
        },
        {
          "name": "manage-membership-of-members"
        },
        {
          "name": "manage-membership"
        },
        {
          "name": "view-members"
        },
        {
          "name": "manage"
        },
        {
          "name": "impersonate-members"
        }
      ]
    },
    {
      "name": "Users",
      "type": "Users",
      "ownerManagedAccess": false,
      "attributes": {},
      "uris": [],
      "scopes": [
        {
          "name": "view"
        },
        {
          "name": "manage-group-membership"
        },
        {
          "name": "impersonate"
        },
        {
          "name": "map-roles"
        },
        {
          "name": "reset-password"
        },
        {
          "name": "manage"
        }
      ]
    },
    {
      "name": "Clients",
      "type": "Clients",
      "ownerManagedAccess": false,
      "attributes": {},
      "uris": [],
      "scopes": [
        {
          "name": "view"
        },
        {
          "name": "map-roles-client-scope"
        },
        {
          "name": "map-roles"
        },
        {
          "name": "manage"
        },
        {
          "name": "map-roles-composite"
        }
      ]
    },
    {
      "name": "6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11",
      "ownerManagedAccess": false,
      "attributes": {},
      "uris": [],
      "scopes": [
        {
          "name": "view"
        },
        {
          "name": "map-roles-client-scope"
        },
        {
          "name": "map-roles"
        },
        {
          "name": "manage"
        },
        {
          "name": "map-roles-composite"
        }
      ]
    }
  ],
  "policies": [
    {
      "name": "identity-admin-user",
      "type": "user",
      "logic": "POSITIVE",
      "decisionStrategy": "UNANIMOUS",
      "config": {
        "users": "[\"service-account-identity-admin\"]"
      }
    },
    {
      "name": "identity-admin-user-deny",
      "type": "user",
      "logic": "NEGATIVE",
      "decisionStrategy": "UNANIMOUS",
      "config": {
        "users": "[\"service-account-identity-admin\"]"
      }
    },
    {
      "name": "identity-admin-all-clients",
      "type": "scope",
      "logic": "POSITIVE",
      "decisionStrategy": "UNANIMOUS",
      "config": {
        "defaultResourceType": "Clients",
        "resources": "[\"Clients\"]",
        "scopes": "[\"view\",\"manage\"]",
        "applyPolicies": "[\"identity-admin-user\"]"
      }
    },
    {
      "name": "deny-migration-runner",
      "type": "scope",
      "logic": "POSITIVE",
      "decisionStrategy": "UNANIMOUS",
      "config": {
        "defaultResourceType": "Clients",
        "resources": "[\"6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11\"]",
        "scopes": "[\"map-roles\",\"view\",\"map-roles-client-scope\",\"map-roles-composite\",\"manage\"]",
        "applyPolicies": "[\"identity-admin-user-deny\"]"
      }
    }
  ],
  "scopes": [
    {
      "name": "manage-group-membership"
    },
    {
      "name": "map-roles-client-scope"
    },
    {
      "name": "map-role"
    },
    {
      "name": "view-members"
    },
    {
      "name": "reset-password"
    },
    {
      "name": "manage"
    },
    {
      "name": "impersonate-members"
    },
    {
      "name": "manage-members"
    },
    {
      "name": "view"
    },
    {
      "name": "manage-membership-of-members"
    },
    {
      "name": "manage-membership"
    },
    {
      "name": "impersonate"
    },
    {
      "name": "map-role-composite"
    },
    {
      "name": "map-roles"
    },
    {
      "name": "map-role-client-scope"
    },
    {
      "name": "map-roles-composite"
    }
  ],
  "decisionStrategy": "UNANIMOUS",
  "authorizationSchema": {
    "resourceTypes": {
      "Roles": {
        "type": "Roles",
        "scopes": [
          "map-role",
          "map-role-composite",
          "map-role-client-scope"
        ],
        "scopeAliases": {}
      },
      "Groups": {
        "type": "Groups",
        "scopes": [
          "manage",
          "manage-membership",
          "view-members",
          "view",
          "impersonate-members",
          "manage-members",
          "manage-membership-of-members"
        ],
        "scopeAliases": {}
      },
      "Users": {
        "type": "Users",
        "scopes": [
          "view",
          "reset-password",
          "manage-group-membership",
          "manage",
          "map-roles",
          "impersonate"
        ],
        "scopeAliases": {
          "view": [
            "view-members"
          ],
          "manage-group-membership": [
            "manage-membership-of-members"
          ],
          "impersonate": [
            "impersonate-members"
          ],
          "manage": [
            "manage-members"
          ]
        },
        "groupType": "Groups"
      },
      "Clients": {
        "type": "Clients",
        "scopes": [
          "view",
          "map-roles-client-scope",
          "map-roles",
          "manage",
          "map-roles-composite"
        ],
        "scopeAliases": {}
      }
    }
  }
}
```

## T051 — dev key pair

RSA 2048, generated with Node `crypto.generateKeyPairSync('rsa', {modulusLength: 2048, publicExponent: 65537})`.
The private key is exported as DER PKCS#8, base64-encoded (one line, 1624 characters). `kid` is the RFC 7638
thumbprint: base64url(SHA-256(`{"e":"AQAB","kty":"RSA","n":"<n>"}`)). Sign/verify with the pair was checked.

Per T051 the **private key is deliberately not recorded here** (one copy, in the AppHost parameter default, so the
guard has one place to check). It was handed to the caller in the T051 report.

Public JWK:

```json
{
  "kty": "RSA",
  "kid": "xgkfYkP-mF8AecjLQYHbeU50QGZHwFXAkMhUyKBt_-c",
  "use": "sig",
  "alg": "RS256",
  "n": "izqTd0k5VJsfsbl00fiXQEpID7pdmdNrkykE1YUc8ZMXhZx1HGwoiyIF0jO7mjuF15I-9GWJCfHb2ep5mIm6ZIMCw_Vlno0HqPm8dpaeTstmpz_KLGbHKVFBcB38ngDH3apVlvBMZY1fwGdoFi1HwL7yMVaqqVUkGEfgGpSaMbdL9K6LVadGWt2RBT72eoKxPA9AOAz6Fj8ZxBADmuauwROwoPvTa3RBBFiYSx2I7BZwfboigeAKcvmEObMr90kMfXCJVtsiBtwaVlexNZCRtJGiwkZYGcCEad8Rpnu1YWAsH08TlXBMJNtD6sMIfPu6pFmFtU2VJGwl7gQNWSGgvw",
  "e": "AQAB"
}
```

JWKS string (single line):

```
{"keys":[{"kty":"RSA","kid":"xgkfYkP-mF8AecjLQYHbeU50QGZHwFXAkMhUyKBt_-c","use":"sig","alg":"RS256","n":"izqTd0k5VJsfsbl00fiXQEpID7pdmdNrkykE1YUc8ZMXhZx1HGwoiyIF0jO7mjuF15I-9GWJCfHb2ep5mIm6ZIMCw_Vlno0HqPm8dpaeTstmpz_KLGbHKVFBcB38ngDH3apVlvBMZY1fwGdoFi1HwL7yMVaqqVUkGEfgGpSaMbdL9K6LVadGWt2RBT72eoKxPA9AOAz6Fj8ZxBADmuauwROwoPvTa3RBBFiYSx2I7BZwfboigeAKcvmEObMr90kMfXCJVtsiBtwaVlexNZCRtJGiwkZYGcCEad8Rpnu1YWAsH08TlXBMJNtD6sMIfPu6pFmFtU2VJGwl7gQNWSGgvw","e":"AQAB"}]}
```

`kid`: `xgkfYkP-mF8AecjLQYHbeU50QGZHwFXAkMhUyKBt_-c`

## Probes (live, 2026-10-24) — review claims against the committed FGAP design

Throwaway `spike-probe-kc` = `quay.io/keycloak/keycloak:26.6.4`, `start-dev --import-realm`, no volume, the committed
`src/AppHost/Realms/smart-sentinel-eye-realm.json` mounted byte-identical (cmp-checked). Tokens: `client_credentials` for
service accounts (`identity-admin`, `scenario-simulator`, `event-ingestion`, `revocation-list-reader`), password grant
through `integration-test-persona` for the humans `operator` and `wall-munich` (a kiosk wall user). All requests
`/admin/realms/smart-sentinel-eye/...`. Migration-runner client id `M` = `6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11`
(`clientAuthenticatorType: client-jwt`, so its power is the `jwks.string` attribute a PUT can replace).
Control: the same non-admin tokens get 403 on `GET /clients/{management-web}` and `GET /users`.

### Probe 1 — NEGATIVE user policy permits everyone else: CONFIRMED (BLOCKER 1)

| Principal | GET M | PUT M | GET M/client-secret | POST M/client-secret | GET /clients?clientId=migration-runner |
|---|---|---|---|---|---|
| identity-admin | 403 | 403 | 403 | 403 | 200 (`[]`) |
| scenario-simulator | **200** | **204** | **200** | **200** | 403 |
| event-ingestion | **200** | **204** | **200** | **200** | 403 |
| revocation-list-reader | **200** | **204** | **200** | **200** | 403 |
| operator (human, no realm-management role) | **200** | **204** | **200** | **200** | 403 |
| wall-munich (kiosk wall user) | **200** | **204** | **200** | **200** | 403 |

Any authenticated principal, with no admin role at all, reads and rewrites the migration-runner client (including its
`jwks.string`, i.e. takeover of the credential that carries the schema-owner database role) and regenerates its secret.
identity-admin is the only principal denied — the policy does exactly the inverse of its intent.

### Probe 2 — identity-admin self-grants manage-clients: NOT REPRODUCED (BLOCKER 2 does not hold)

identity-admin token, realm-management client uuid `502d2b04-2073-44af-aa7c-41db8ad4f2c3`, role bodies
`[{manage-clients},{view-clients}]`:

| Attempt | Status |
|---|---|
| POST users/{identity-admin SA}/role-mappings/clients/{realm-management} | 403 |
| POST groups (create `probe-g`) | 201 |
| POST groups/{g}/role-mappings/clients/{realm-management} | 403 |
| PUT users/{identity-admin SA}/groups/{g} (join) | 204 (harmless: group has no roles) |
| POST roles (create realm role) | 403 |
| POST roles/{r}/composites with manage-clients | 403 |
| POST users/{SA of a client it just created}/role-mappings/clients/{realm-management} | 403 |

Its current realm-management roles are `query-users, manage-users, query-clients, view-users, query-groups`; it cannot
map roles it does not hold. No re-mint was needed.

### Probe 3 — service-account user of migration-runner (should-fix 4): CONFIRMED for disable and delete

| Attempt (identity-admin) | Status |
|---|---|
| GET users?username=service-account-migration-runner | 200 |
| PUT users/{id} `{"enabled":false}` | **204** |
| DELETE users/{id} | **204** (then GET 404) |
| PUT users/{id}/reset-password (valid-length value on a different SA user) | 400 `You can not manage credentials for this user` |
| password grant as service-account-probe-new | 400 `invalid_grant` |

Service-account users cannot hold credentials and cannot password-login on 26.6.4; the exposure is disable/delete
(denial of the migration gate), not takeover.

### Probe 4 — identity-admin edits its own deny: NOT possible

| Attempt (identity-admin) | Status |
|---|---|
| GET authz/resource-server, /policy, /permission, /policy/{id} | 403 |
| PUT permission/scope/{deny-migration-runner} (policies []) | 403 |
| DELETE permission/scope/{deny-migration-runner} | 403 |
| POST permission/scope (new) | 403 |
| PUT/DELETE policy/{deny user policy} (wrong path form) | 405 |
| POST authz/resource-server/import (empty-valid body) | 400 `unknown_error` (nothing changed) |
| PUT clients/{admin-permissions} `authorizationServicesEnabled:false` | 400 `Not supported for this client.` |
| PUT realm `adminPermissionsEnabled:false` | 403 |
| DELETE clients/{admin-permissions} | 400 `unknown_error` |

Policies verified intact afterwards (read with the master admin).

### Probe 5 — admin-permissions service account and secret

The imported `admin-permissions` client has a service-account user (`service-account-admin-permissions`) and a
`client-secret`. As identity-admin: GET client = 200, GET client-secret = 200 with **no value** (masked),
POST client-secret = **200 with a new value** (identity-admin can rotate it). That secret mints a token with
`client_credentials` (200, empty scope, no roles). The token alone grants nothing (GET /users 403, GET /clients 403,
authz policies 403) but it is a principal that passes the NEGATIVE policy: GET M = 200 (same defect as probe 1).

### Enrolment still works (identity-admin, committed realm, no manage-clients/view-clients)

POST /clients (confidential, `serviceAccountsEnabled`) 201 -> GET 200 -> GET client-secret 200 (value) -> POST
client-secret 200 (regenerated) -> PUT 204 -> GET service-account-user 200. Repeated with a second new client: 201/200/204.
Per-client ownership of clients it creates is therefore real; the type-level `identity-admin-all-clients` permission
is what gives it.

### Fix attempts on the throwaway (master admin, `PUT authz/resource-server/permission/scope/{deny-migration-runner id}`)

Permission body (base, `policies` varies):
`{"id":"<perm id>","name":"deny-migration-runner","type":"scope","logic":"POSITIVE","decisionStrategy":"UNANIMOUS","resourceType":"Clients","resources":["6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11"],"scopes":["view","manage","map-roles","map-roles-composite","map-roles-client-scope"],"policies":[...]}`

| Variant of `policies` | GET/PUT/secret on M, scenario-simulator / wall-munich / identity-admin | identity-admin `GET /clients` and `?search=true&clientId=migration-runner` |
|---|---|---|
| committed: `[identity-admin-user-deny]` (NEGATIVE) | 200/204/200 (see probe 1) | M absent |
| A: reviewer's `[nobody]`, new `POST policy/user {"name":"nobody","logic":"POSITIVE","users":[]}` | 403 everywhere, incl. identity-admin | **M listed, with its `secret` field** (direct GET still 403) |
| B: `[deny-dummy]`, `POST policy/user {"name":"deny-dummy","logic":"POSITIVE","users":["<id of a disabled user deny-nobody>"]}` | 403 everywhere | **M listed** again |
| C: `[identity-admin-user-deny, deny-dummy]` (both, UNANIMOUS) | 403 everywhere (GET/PUT/secret GET/secret POST) | M absent (19 clients, 0 hits on search); `?clientId=` exact `[]` |

Conclusion: the reviewer's always-deny positive policy closes read/write/secret but regresses the list filter (client
representations of migration-runner become enumerable by identity-admin; here the secret field is vestigial because the
client is client-jwt, but the jwks and attributes are exposed). Variant C — keep the NEGATIVE identity-admin policy
**and** add a policy no one satisfies, unanimous — keeps the list filter and denies everyone else.
Other probes run before the variants and were not repeated afterwards: 2, 3, 4, 5, enrolment.

## T150 / T151 — ADR-0187 two-policy variant probed (live, 2026-10-24)

Throwaway `spike-t150-kc` (and `spike-t150-kc2` for the round trip) = `quay.io/keycloak/keycloak:26.6.4`,
`start-dev --import-realm`, no volume, bootstrap `admin`/`admin`, ports 18180/18181, both removed afterwards
(`docker rm -f`, only these two). The realm file was the committed `smart-sentinel-eye-realm.json` (BOM kept) plus the
variant below, imported through `/opt/keycloak/data/import` (so the import round trip is part of every result).
Tokens as in "Probes": `client_credentials` for service accounts, password grant through `integration-test-persona`
(`directAccessGrantsEnabled`) for `operator` / `wall-munich`. `M` = `6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11`.

### Variant 1 (ADR-0187 as written): two user policies, UNANIMOUS

Added to the realm file: user `{"username":"migration-runner-deny-target","enabled":false,"emailVerified":false}`
(no credentials, no email/names, no groups, no roles) and policy
`{"name":"migration-runner-deny-target-user","type":"user","logic":"POSITIVE","decisionStrategy":"UNANIMOUS","config":{"users":"[\"migration-runner-deny-target\"]"}}`;
`deny-migration-runner.config.applyPolicies` =
`"[\"identity-admin-user-deny\",\"migration-runner-deny-target-user\"]"`.
Import log: `Importing from directory /opt/keycloak/bin/../data/import` -> `KC-SERVICES0030: Full model import
requested. Strategy: IGNORE_EXISTING` -> `Realm 'smart-sentinel-eye' imported` -> `KC-SERVICES0032: Import finished
successfully` (no error). Keycloak resolved the username to the user id (`GET policy/user/{id}` showed `users:["<uuid>"]`).

Baseline matrix (verbatim):

| Principal | GET M | PUT M | GET M/client-secret | `GET /clients?clientId=migration-runner` | `GET /clients` |
|---|---|---|---|---|---|
| identity-admin | 403 | 403 | 403 | 200 `[]` | 200, n=17, M absent |
| scenario-simulator | 403 | 403 | 403 | 403 | 403 |
| event-ingestion | 403 | 403 | 403 | 403 | 403 |
| operator | 403 | 403 | 403 | 403 | 403 |
| wall-munich | 403 | 403 | 403 | 403 | 403 |

The two-policy variant closes probe 1 and keeps the list filter, as variant C above predicted.

### H-1 (identity-admin enables the dummy, sets a password, signs in as it): CONFIRMED

All calls with the `identity-admin` token (committed roles: `manage-users`, `view-users`, `query-*`):

| Step | Status |
|---|---|
| GET users?username=migration-runner-deny-target&exact=true | 200 |
| PUT users/{id} `enabled:true` (nothing else) | 204 |
| PUT users/{id}/reset-password `{"type":"password","value":"...","temporary":false}` | 204 |
| password grant as the dummy (first try) | 400 `invalid_grant` "Account is not fully set up" (user profile wants email/firstName/lastName) |
| PUT users/{id} `enabled:true,email,firstName,lastName,emailVerified:true,requiredActions:[]` | 204 |
| PUT reset-password again | 204 |
| password grant as the dummy | **200** |
| dummy: GET clients/M | **200** |
| dummy: PUT clients/M (full representation) | **204** |
| dummy: GET clients/M/client-secret | **200** |
| dummy: POST clients/M/client-secret | **200** |
| dummy: GET /users (control, holds no role) | 403 |

The dummy satisfies both policies (it is the one user the positive policy names and it is not identity-admin), so it
gets view/manage on migration-runner, including replacing `jwks.string`. The disabled/no-credential state is only as
strong as identity-admin's inability to change it, and `manage-users` is exactly that ability.

Other edits identity-admin can make on the dummy (same token): `PUT users/{id}/groups/{fab}` 204 (DELETE 204);
`POST users/{id}/role-mappings/realm` `user` 204 and `offline_access` 204; `PUT users/{id}` with `attributes` 204;
`GET users/{id}/role-mappings` 200. `realm-management` client roles: POST `manage-users` / `manage-clients` 403 (it
cannot grant admin roles to the dummy). `GET /roles` 403.

### H-2 (identity-admin deletes the dummy): REFUTED

| Step | Result |
|---|---|
| identity-admin DELETE users/{id} (dummy) | 204; GET afterwards 404 |
| master `GET policy` | `migration-runner-deny-target-user` **still exists** (`user`, `POSITIVE`) |
| master `GET policy/user/{policy id}` | `"users":[]` (membership emptied); `GET policy/{id}` still shows the stale `config.users` `["<deleted uuid>"]` |
| master `GET policy/{deny-migration-runner id}/associatedPolicies` | both policies still associated |
| the five principals | identical to the baseline table (403 across the board, identity-admin `?clientId` 200 `[]`, `GET /clients` n=17, M absent) |

Keycloak does not remove a user policy when its last user is deleted, and a user policy with no users never matches,
so the unanimous deny stays closed. The dummy's absence is harmless; its presence is the vulnerability.

### Variant 2 (alternative a): a time policy that never matches, instead of the dummy

`POST policy/time {"name":"deny-migration-runner-never","type":"time","logic":"POSITIVE","decisionStrategy":"UNANIMOUS","notOnOrAfter":"2000-01-01 00:00:00"}`
-> 201; `GET` shows `"config":{"noa":"2000-01-01 00:00:00"}`. `deny-migration-runner` policies =
`[identity-admin-user-deny, deny-migration-runner-never]` (`PUT permission/scope/{id}` -> 201). Matrix: identical to the
baseline table. Enrolment sequence (below): identical to baseline. The migration-runner's own token (client assertion
signed with a throwaway key, see variant 3) gets GET M **403**. No user, no credential and nothing identity-admin can
edit (probe 4: the authz endpoints are 403).

Exported fragment (`POST partial-export?exportClients=true&exportGroupsAndRoles=false`, the three policies):

```json
[
  {"name":"identity-admin-user-deny","type":"user","logic":"NEGATIVE","decisionStrategy":"UNANIMOUS",
   "config":{"users":"[\"service-account-identity-admin\"]"}},
  {"name":"deny-migration-runner-never","type":"time","logic":"POSITIVE","decisionStrategy":"UNANIMOUS",
   "config":{"noa":"2000-01-01 00:00:00"}},
  {"name":"deny-migration-runner","type":"scope","logic":"POSITIVE","decisionStrategy":"UNANIMOUS",
   "config":{"defaultResourceType":"Clients","resources":"[\"6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11\"]",
             "scopes":"[\"manage\",\"map-roles-composite\",\"view\",\"map-roles-client-scope\",\"map-roles\"]",
             "applyPolicies":"[\"identity-admin-user-deny\",\"deny-migration-runner-never\"]"}}
]
```

Round trip: a second throwaway (`spike-t150-kc2`) imported the committed realm file with exactly that policy added
(no extra user) and `applyPolicies` as above. Log: `Realm 'smart-sentinel-eye' imported` /
`KC-SERVICES0032: Import finished successfully`. `associatedPolicies` of `deny-migration-runner` listed both
(`deny-migration-runner-never` `time`, `identity-admin-user-deny` `user`). Matrix: baseline table again. After
`docker restart spike-t150-kc2` the matrix and the enrolment sequence were unchanged.
Counterfactual (kc1): the time policy edited to `notOnOrAfter 2099-01-01 00:00:00` (i.e. it matches) -> scenario-simulator
GET M 200 / PUT 204 / secret 200 again, so the time policy is what closes the deny. Caveat: it depends on the clock
being after 2000-01-01 only.

### Variant 3 (alternative b): a client policy naming migration-runner itself

`POST policy/client {"name":"deny-migration-runner-self","type":"client","logic":"POSITIVE","clients":["6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11"]}`
-> 201 (stored as `config.clients:"[\"<uuid>\"]"`). Deny = `[identity-admin-user-deny, deny-migration-runner-self]`.
Matrix for the five principals: baseline table; enrolment unchanged. But the policy also matches the migration-runner's
own token: with the client's `jwks.string` replaced by a throwaway key (master PUT, throwaway only) and a signed client
assertion, `client_credentials` -> 200 and GET clients/M **200** (variant 2: 403). The deny turns into a self-grant
on the schema-owner credential. Not recommended.

### Variant 4 (alternative c): drop manage-users/view-users from identity-admin, FGAP v2 Users permissions

State: dummy recreated by master, deny = `[identity-admin-user-deny, migration-runner-deny-target-user]` (variant 1),
`manage-users` and `view-users` removed from `service-account-identity-admin` (DELETE role-mappings 204; remaining
`query-clients`, `query-users`, `query-groups`).

Enrolment sequence (identity-admin): POST /clients confidential SA; GET and POST client-secret; PUT client; GET
service-account-user; GET group-by-path; GET user role-mappings/realm; POST then DELETE realm role-mapping
`offline_access`; PUT user group; DELETE client. (`GET roles/user` is 403 in the baseline too.)

| Configuration | POST /clients | secret GET/POST | PUT client | GET SA user | group-by-path | GET role-mappings | POST role | PUT group | DELETE role |
|---|---|---|---|---|---|---|---|---|---|
| baseline (committed roles) | 201 | 200/200 | 204 | 200 | 200 | 200 | 204 | 204 | 204 |
| roles removed, no Users permission | 201 | 200/200 | 204 | 200 | 403 | 403 | 403 | 403 | 403 |
| + Users permission `view,manage,manage-group-membership,map-roles` | 201 | 200/200 | 204 | 200 | 403 | 200 | 403 | 404 | 403 |
| + Groups `view,manage-membership` + Roles `map-role` (type-level, policy `identity-admin-user`) | 201 | 200/200 | 204 | 200 | 200 | 200 | 204 | 204 | 204 |
| same, Users scopes `view,manage-group-membership,map-roles` (no `manage`) | 201 | 200/200 | 204 | 200 | 200 | 200 | 204 | 204 | 204 |

With `manage` on Users, **H-1 is reproduced**: PUT users/{id} enabled+profile 204, reset-password 204 (the separate
`reset-password` scope is not needed when `manage` is held; `PUT users/{id}` with a `credentials` array also 204),
password grant as the dummy 200, GET M 200. A user permission with `manage` cannot exclude the dummy.
Without `manage`, against the dummy: PUT enabled+profile **403**, reset-password **403**, PUT attributes **403**,
DELETE **403**, POST /users **403**, password grant `invalid_grant`; still allowed: GET 200, PUT/DELETE group 204 and
realm role-mappings (harmless: the dummy cannot sign in). Self-escalation: POST `realm-management` `manage-users` /
`manage-clients` / `realm-admin` onto its own service-account user -> 403 x3; `PUT users/{self}` 403. Matrix of the
five principals in that state: baseline table.
Cost: the identity-admin role set changes, three scope permissions join the FGAP fragment, and the closure rests on
identity-admin lacking `manage` on Users: granting it later (the obvious fix for any user-edit need) silently reopens H-1.

### Findings and recommendation

| Hypothesis / option | Result |
|---|---|
| H-1 enable + set password + sign in as the dummy | **CONFIRMED** (committed roles; also with a Users `manage` permission) |
| H-2 delete the dummy so the deny degrades to NEGATIVE-only | **REFUTED** (policy kept with `users:[]`, deny holds) |
| identity-admin can edit the dummy's groups / realm roles / attributes | yes under committed roles; attributes 403 without `manage` |
| (a) never-matching time policy as the second policy | **closes H-1 and H-2**; matrix and enrolment = baseline; survives import and restart |
| (b) client policy naming migration-runner | matrix = baseline, but migration-runner's own token reaches M (200): a self-grant |
| (c) no `manage-users`, FGAP Users view/manage-group-membership/map-roles | closes H-1 only while `manage` stays absent; more moving parts |

Recommendation: ADR-0187 cannot rest on the dummy user as written, since H-1 is confirmed. Keep the NEGATIVE
`identity-admin-user-deny` and replace the second policy with the never-matching time policy (variant 2): nothing for
identity-admin to enable, repoint or delete, no extra user in the realm, the same block shape in the file. The T160
guard would then require the permission to apply the NEGATIVE identity-admin policy **and** a policy no principal can
satisfy (a `time` policy with only a past `noa`), UNANIMOUS. The ADR needs amending (human decision) before T160,
T161, T165 and T171. T151 is observed for the variant-2 file shape only; it must be repeated on the byte-identical file
T171 writes.

## T151 / T171 — import round trip on the byte-identical committed realm file (observed 2026-10-24)

Throwaway `spike-t171-kc` = `quay.io/keycloak/keycloak:26.6.4`, `start-dev --import-realm`, no volume, port 18190, the
committed `src/AppHost/Realms/smart-sentinel-eye-realm.json` (BOM kept, two-policy deny: `identity-admin-user-deny` +
`deny-migration-runner-never` `time` `noa 2000-01-01 00:00:00`, no seeded user) bind-mounted read-only; removed afterwards.
Log: `Realm 'smart-sentinel-eye' imported` / `KC-SERVICES0032: Import finished successfully`. Tokens: password grant via
`integration-test-persona` for operator / wall-munich, `client_credentials` for the service accounts.
Columns are the status codes on migration-runner (M): GET, PUT, client-secret GET, client-secret POST,
`GET /clients?clientId=migration-runner`, `GET /clients`.

| Principal | GET M | PUT M | secret GET | secret POST | search | list |
|---|---|---|---|---|---|---|
| operator | 403 | 403 | 403 | 403 | 403 | 403 |
| wall-munich | 403 | 403 | 403 | 403 | 403 | 403 |
| scenario-simulator | 403 | 403 | 403 | 403 | 403 | 403 |
| event-ingestion | 403 | 403 | 403 | 403 | 403 | 403 |
| identity-admin | 403 | 403 | 403 | 403 | 200 `[]` | 200, n=17, `migration-runner` absent |

identity-admin enrolment baseline (create a client, read its secret, regenerate it, PUT it, read its service-account
user, delete it): POST /clients 201, secret GET 200, secret POST 200, PUT 204, service-account-user GET 200, DELETE 204.
After `docker restart spike-t171-kc` the whole table and the enrolment sequence were identical (same codes, list n=17).
