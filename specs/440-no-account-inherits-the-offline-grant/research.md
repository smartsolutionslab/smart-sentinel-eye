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
