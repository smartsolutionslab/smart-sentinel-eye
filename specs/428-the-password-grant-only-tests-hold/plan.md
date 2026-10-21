# Plan 428 — The password grant only tests hold

**Spec:** `specs/428-the-password-grant-only-tests-hold/spec.md` · **Issue:** #2809
**Constitution check:** §VIII. One client fewer accepts a password grant without a secret, and
the only remaining password-grant client is confidential and guarded. §Testing: red first (§5).
§II and the other domain rules do not apply because no `src/` C# changes. **No ADR**, as the
owner ruled. The client copies `integration-test-admin`'s established shape (spec 327) and
changes only the grant it permits.
**Latency:** N/A.

## 1. Where the change lives

There is no bounded context, no Domain, Application, Infrastructure or Api code, no
`Shared.Contracts` change and no messaging. **The only production file is the realm JSON.**
Everything else is test harness code.

| File | Change |
|---|---|
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | add client `integration-test-persona`; set `management-web.directAccessGrantsEnabled` to `false` |
| `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs` | rename `ClientId` to `ConsoleClientId`; add `PersonaClientId`/`PersonaClientSecret`; `FetchAccessTokenAsync(user, pw)` posts to the persona client with `client_secret`; `GetAccessTokenForClientAsync` gains an optional `clientSecret` |
| 8 integration files + `RunModeStackAddress.cs` | the 20 hand-edit sites (spec, site table) |
| `e2e/wall-survives-a-lockout.spec.ts` | `MANAGEMENT_CLIENT_ID` becomes `PERSONA_CLIENT_ID` plus `PERSONA_CLIENT_SECRET` in both forms |
| `tests/Architecture.Tests/PasswordGrantClientTests.cs` (new) | the static guard (§4) |
| `tests/Integration.Tests/Identity/RealmImportMirrorTests.cs` | two mirror facts for the persona consts (§4 G-M) |
| `tests/Integration.Tests/Identity/PersonaClientIntegrationTests.cs` (new) | AS-1, AS-2, AS-3, AS-5 live |
| `tests/Integration.Tests/ci-shards/shard-N.filter` | entry for the new integration class. A missing entry fails deterministically (repo memory). Put it in the shard holding `AdminHarnessTokenIntegrationTests` (shard-3). |

## 2. The client (realm JSON, after `integration-test-admin`)

```json
{
  "clientId": "integration-test-persona",
  "name": "Smart Sentinel Eye - Integration test persona (dev/CI-only password grant)",
  "description": "Spec 428 (#2809). Test-only password grant for persona tokens and lockout/policy probes, replacing ROPC through management-web. Scopes equal management-web's (guarded). Never deployed.",
  "enabled": true,
  "publicClient": false,
  "serviceAccountsEnabled": false,
  "standardFlowEnabled": false,
  "implicitFlowEnabled": false,
  "directAccessGrantsEnabled": true,
  "secret": "dev-only-integration-test-persona-secret",
  "protocol": "openid-connect",
  "defaultClientScopes": [ <exactly management-web's 24 entries> ]
}
```

The description is under 255 characters (**a longer one kills the realm import and hangs the
whole fixture**), and a guard fact enforces it. The client has no redirect URIs and no web
origins. It has no service-account user, so it needs no `users` entry, unlike
`integration-test-admin`.

## 3. Invariants

- **I1.** In the application realm import, exactly one client has
  `directAccessGrantsEnabled: true`, and it is `integration-test-persona`.
- **I2.** That client is confidential (`publicClient: false`), has no service account, no
  standard flow, no implicit flow, no `redirectUris`, and its `secret` starts with `dev-only-`.
- **I3.** Its `defaultClientScopes` are set-equal, in both directions, to `management-web`'s.
- **I4.** Its description is no longer than 255 characters.
- **I5.** Every test-side copy of its id and secret equals the realm file (`AspireFixture` consts
  by mirror facts; the e2e const by a source-scan fact).
- **I6 (live).** `management-web` refuses a password grant with `unauthorized_client`, and a
  persona token minted by the new client carries that user's `sub` and fab groups.

## 4. The guard: what protects the new client from becoming a production leak

**The honest baseline (#2878):** nothing excludes *any* dev-only client from a production import,
because no production realm import exists. The protection the dev-only clients actually carry is
three layers, and this client gets all three. It also gets one the others do not have.

| Guard | Fact(s) | Planted-violation counterfactual (observed in 4b, then reverted) |
|---|---|---|
| **G-S, shape** (`Architecture.Tests/PasswordGrantClientTests.cs`, modelled on `IntegrationTestAdminClientTests`) | I2, I3, I4. Each asserts that the client exists first, with a message that names what is missing. | flip `publicClient` to `true` → red; rename the secret to `integration-test-persona-secret` (no `dev-only-` prefix) → red; add `"sse.extra"` to its scopes → red; pad the description to 256 chars → red |
| **G-R, ROPC census** (same class, new) | I1: the set of clients with `directAccessGrantsEnabled == true` equals `{integration-test-persona}` | set `management-web.directAccessGrantsEnabled` back to `true` → red, naming `management-web`; set `kiosk-web`'s to `true` → red. **This is the fact that keeps the console's password grant from coming back, and that keeps a second ROPC client from appearing unguarded.** |
| **G-M, mirror** (`RealmImportMirrorTests.cs`, same shape as the `AspireFixture_HarnessClient*` pair) | I5: `AspireFixture.PersonaClientId` names exactly one client, and `PersonaClientSecret` equals its secret. `ConstantOf` reads only `NonPublic` fields, so these are two dedicated facts, not `IntegrationTestCopySites` rows. | change one character of `PersonaClientSecret` → red |
| **G-E, e2e copy** (in `PasswordGrantClientTests`) | I5 for `e2e/wall-survives-a-lockout.spec.ts`: regex-extract `PERSONA_CLIENT_ID` and `PERSONA_CLIENT_SECRET`, compare to the realm. Build the path with `Path.Combine` from split segments, so Windows and Linux CI resolve it the same way (repo memory on slash normalising). | change the e2e secret literal → red |
| **No production-reachable default** | The secret is **not** an `AddOverridableParameter` and no `src/` project reads it. Only tests hold it. | grep `src/` for `integration-test-persona` returns only the realm JSON. 4b runs this check and quotes it. |

`RunModeStackAddress` and the other integration sites use `AspireFixture.PersonaClientId` and
`PersonaClientSecret` directly rather than holding copies, so G-M covers them without more rows.

## 5. Test colour (phase 4a) and order

**Colour: red. This is behaviour-changing.** The product change is that `management-web` stops
accepting a password grant. The harness's minting principal also changes (`azp` moves from
`management-web` to `integration-test-persona`).

The call-site migration is **not a separate refactor**. It changes test plumbing only, it fixes no
bug that a characterisation test could encode, and its safety net is the migrated suites going
green unchanged in their assertions once 4b lands. The one exception is SC-9's positive control in
ConsoleScopeGrant, which moves to the persona client (spec A3). So this is one issue, not two.
If strict slicing is preferred, US1 and US2 can ship as two consecutive PRs. Both are red.

4a (test-writer; return the output verbatim; tests are read-only to 4b):

- `PasswordGrantClientTests`: G-S facts **red** (client absent); G-R **red** (census finds
  `management-web`); G-E **red** (consts absent in the e2e file).
- `PersonaClientIntegrationTests`: AS-1 **red** (`invalid_client`: no such client); AS-2/AS-3
  **red** for the same reason; **AS-5 red** (`management-web` answers 200 today). AS-5 is the
  product behaviour's red.
- The mirror facts (G-M) **do not compile** until the consts exist. 4a adds the consts as
  `PersonaClientId = "integration-test-persona"` and `PersonaClientSecret =
  "dev-only-integration-test-persona-secret"` in the fixture, with nothing yet consuming them. The
  facts then run red on "no such client". This keeps every commit building on its own.

4b (infra-engineer): realm JSON, the fixture switch, then the hand sites, then the e2e file, then
the flag flip. Then observe every counterfactual in §4 and quote them in the PR.

## 6. Operational notes (put them in the PR body)

- **The realm import skips an existing realm.** A developer's persistent Keycloak
  (`WithKeycloakDeveloperLanePersistence`, `AppHost.cs:148-151`) keeps the old realm. It has no
  persona client and `management-web` still allows ROPC, and the stack looks healthy. **Delete
  the Keycloak data volume.** `RunModeStackAddress` targets exactly such a long-lived run-mode
  stack, so its token-failure message should name this cause (`invalid_client` on
  `integration-test-persona` means a stale volume).
- The facts that inspected *management-web's own* minted token now inspect the persona token. The
  live check of the console token is `e2e/management-identity.spec.ts`, which is unchanged (spec
  A3). Rename ConsoleScopeGrant SC-3/SC-4 from `A_management_web_token_…` to
  `A_console_scoped_token_…`. A fact name that claims a client it no longer mints from is the kind
  of drift this repo keeps having to correct.
- Comments that say "azp=management-web" (WebhookBearerValidation `:34/:106`,
  WebhookRevocationRefusesDelivery `:62`, AdminHarnessToken `:75`, `AspireFixture.Auth.cs:13-19`)
  are updated in the same commits as the code they describe.

## 7. Boundary rules

No `src/` C# changes, so NetArchTest boundaries are unaffected. `Architecture.Tests` keeps its own
local copies of client ids, as `IntegrationTestAdminClientTests` does, because it does not
reference `Integration.Tests`.
