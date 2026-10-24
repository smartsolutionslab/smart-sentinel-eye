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
