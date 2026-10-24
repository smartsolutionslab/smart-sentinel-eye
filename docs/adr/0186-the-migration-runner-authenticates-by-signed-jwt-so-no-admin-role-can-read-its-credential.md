# ADR-0186: The migration runner authenticates by signed JWT, so no admin role can read its credential back

**Status:** **Accepted** by the owner on PR #3106, 2026-10-23, as written (Decisions 1-6; records the owner's two rulings of the same day on #1995 — option (b), the signed-JWT credential, and measure 1, FGAP v2 declared in the realm file, for the reconfigure hop; the open questions in *Implementation Notes* item 6 were not ruled by the acceptance and stay open)
**Date:** 2026-10-23
**Amended by:** ADR-0187 (accepted 2026-10-24) — Decision 5: the migration-runner deny is two policies under `UNANIMOUS` (the NEGATIVE `identity-admin` policy plus a POSITIVE policy naming one seeded, disabled dummy user), because the NEGATIVE policy alone permits every other principal; Decision 6: the realm-file guard also bans a NEGATIVE-only deny. Decisions 1-4, the FGAP all-clients grant and the narrowing of `identity-admin` are unchanged.
**Amends:** ADR-0181 (its Decision 2 — how `migration-runner` authenticates, and which other app-realm roles can reach that account; the permanent `manage-realm` grant is unchanged and stands on this basis)
**Relates to:** ADR-0067, ADR-0130, ADR-0132, ADR-0134, ADR-0143, ADR-0144, ADR-0160, spec 440 (branch `fix/1995-narrow-default-roles`), spec 080, issues 1995, 2878

## Context

ADR-0181 grants `manage-realm` to the `migration-runner` service account permanently, so a one-shot
worker can narrow the realm's default composite on every boot. It accepted that `manage-realm` is broader
than the privilege it removes **only because the holder is confined to a one-shot worker with no HTTP
surface**, and recorded (Decision 2) that the grant goes "to nothing else". `identity-admin` was rejected
as the holder precisely because the live, gateway-reachable Identity API uses it.

The mandatory phase-6 security review of the implementation (spec 440, branch
`fix/1995-narrow-default-roles`, local gates green, unpushed) found that the confinement does not hold as
built. `smart-sentinel-eye-realm.json:634-642` gives `identity-admin` the `manage-clients`
`realm-management` role. In Keycloak's admin model `manage-clients` is authority over *every* client in
the realm, migration-runner included. Both the migration-runner and identity-admin clients authenticate
with a **client secret** (`dev-only-migration-runner-secret`, `dev-only-identity-admin-secret`), and the
Identity adapter already reads client secrets through the admin API
(`HttpKeycloakAdminClient.cs:170`). So anyone holding the live Identity API credential reaches
`manage-realm` in two hops: read migration-runner's secret, mint a `client_credentials` token. That
makes `identity-admin` an *indirect* holder of exactly the realm-wide authority ADR-0181 confined to the
migration worker — and `manage-realm` covers realm **key providers** (forge tokens every service across
every fab accepts), realm settings, and role composites (a holder could put `manage-realm` *into* the
default composite — the inverse of this whole feature).

The owner ruled on 2026-10-23: **option (b)** — give `migration-runner` a credential Keycloak never
exposes. This ADR records that ruling and the facts bearing on it, measured against a throwaway Keycloak
26.6.4 container importing the actual realm file, no reused volume.

### What was measured (Keycloak 26.6.4, realm imported from `smart-sentinel-eye-realm.json`)

**The signed-JWT credential works and cannot be read back as a secret.** With migration-runner switched to
`clientAuthenticatorType: client-jwt` and a public key registered (`use.jwks.string: true`,
`jwks.string: {…}`), a `client_credentials` request carrying a `client_assertion` signed by the matching
private key mints a token with `manage-realm` (200). The **previously configured client secret is rejected**
(`400 invalid_client`) while the authenticator is `client-jwt`. A replayed assertion (same `jti`) is
rejected (`400 Token reuse detected`). So the private key never leaves the MigrationRunner process, and no
admin read of the client returns anything a caller can authenticate with.

**But `manage-clients` on `identity-admin` still defeats option (b) on its own**, because `manage-clients`
can **UPDATE** the client, not merely read it. Two independent, confirmed breaks, each ending in a working
`manage-realm` token:

| Attack by `identity-admin` (declared roles only) | Result |
|---|---|
| `PUT /clients/{migration-runner}` replacing `jwks.string` with an **attacker-controlled public key**, then sign an assertion with the attacker's private key | PUT **204**, token **200**, carries `manage-realm` |
| `PUT /clients/{migration-runner}` setting `clientAuthenticatorType: client-secret`, then `POST …/client-secret` to regenerate, then authenticate with that secret | PUT **204**, regenerate **200**, token **200**, carries `manage-realm` |
| control: `identity-admin` alone `GET …/roles/default-roles-…/composites` | **403** — it is not itself a `manage-realm` holder |
| hop token `GET …/composites`, `DELETE …/composites` (strip `offline_access`), `GET /keys` | **200 / 204 / 200** — the hop token exercises `manage-realm` for real |
| `identity-admin` maps `manage-realm` onto its **own** service account | **403** — mapping a `realm-management` role itself needs `manage-realm`; this route is closed |

So option (b) **removes the read-the-secret hop** (the stored secret becomes unusable, and even a read of
it is inert), but it does **not** close the escalation while `identity-admin` can *rewrite*
migration-runner's credential configuration. The role-mapping route is closed; the client-reconfiguration
route is open. Closing option (b)'s own residual requires, in addition, denying `identity-admin` the
ability to UPDATE that one client. The spike below compared two ways to do that; the owner chose measure 1
(Decision 5).

### The two measures that could close the reconfigure hop, spiked (2026-10-23)

The owner asked for both candidate measures to be spiked and compared. Method as above: throwaway
Keycloak 26.6.4 containers, `start-dev --import-realm`, importing **both** realm files from
`origin/develop` (`smart-sentinel-eye-realm.json` and spec 324's `master-realm.json`) with only the
spike's delta applied: migration-runner switched to `client-jwt` per Decision 1 and granted `manage-realm`
per ADR-0181 (spec 440). Five containers, each fresh, no reused volume. The "attack" is the two breaks
in the table above, run as `identity-admin`; the "runtime flows" are the 13 admin calls
`HttpKeycloakAdminClient` makes (list clients, `POST /clients`, lookup by `clientId`, service-account
user, `group-by-path`, join group, read and strip realm role mappings, read secret, rotate secret, read
client, disable `PUT`, `DELETE`), replayed over HTTP — not the C# adapter itself.

**Measure 1 — fine-grained admin permissions (FGAP).**

| Question | Measured on 26.6.4 |
|---|---|
| Available, enabled by default? | **Yes.** `ADMIN_FINE_GRAINED_AUTHZ_V2` is `enabled`, type `DEFAULT`; v1 (`ADMIN_FINE_GRAINED_AUTHZ`) is `DEPRECATED` and off. No `--features` flag needed. |
| Declarable in the realm import? | **Yes.** `"adminPermissionsEnabled": true` on the realm plus an `admin-permissions` client entry carrying `authorizationSettings` (user policies by username; the per-client permission names the client by **UUID**). That requires pinning migration-runner's `"id"` in the realm file — no client there has one today. Fresh import carried both permissions and the deny held. |
| Does a deny restrict `identity-admin` while it keeps `manage-clients`? | **No.** With `manage-clients` kept and a per-client deny on migration-runner, both breaks still succeed (PUT 204, token 200, hop 200). **A realm-management role is not restricted by FGAP v2**; the permission only *adds* access. |
| What works | Remove `manage-clients` **and** `view-clients` from `identity-admin`; grant FGAP *all clients* `view`+`manage` to its service-account user; add a migration-runner-only permission (`view`, `manage`, `map-roles*`) whose policy is that user with `NEGATIVE` logic. Then on migration-runner: read 403, secret read 403, both PUTs 403, regenerate 403, `DELETE` 403, and it is absent from `GET /clients` and from `?clientId=` lookup. |
| Runtime flows intact? | **All 13 unchanged** (create 201, rotate 200, disable 204, delete 204, group join 204, role strip 204, …). `create-client` role not needed. |
| Is the deny what blocks? (counterfactual) | Yes — the deny deleted, the all-clients grant alone lets both breaks through again (204/200/200). |
| Routes around the deny | Closed: read or delete the FGAP permissions (403), switch off authorization on `admin-permissions` (400), delete `admin-permissions` (400), map `manage-realm` onto a client it creates (403), impersonate migration-runner's service-account user (403), set its password (400; ROPC `invalid_grant`). **Open, not an escalation:** the all-clients grant lets `identity-admin` `PUT` the system clients — it turned `realm-management` into a confidential service-account client, regenerated its secret and minted a token (200), but that token holds no `manage-realm` (403). |
| Who applies it, and when | The realm file, at import. No runtime step. (The migrator *could* author FGAP — its `manage-realm` token created a policy, 201 — but nothing needs it to.) |
| Every-boot narrowing under FGAP | Unaffected: the migrator's signed-JWT token reads and `DELETE`s the default composite (200/204). |
| Re-import / persistence | It is realm-file content: a fresh import (CI, fixture, E2E) gets it; a persistent dev volume keeps whatever it was imported with (`IGNORE_EXISTING`, ADR-0181's dev-stack trap) — no different from any other realm-file edit. |
| How it regresses | Dropping the pinned UUID **fails loud**: Keycloak refuses to start (`Resource [<uuid>] does not exist for type [Clients]`). Re-adding `manage-clients` (or `view-clients`) to `identity-admin` **fails silent** — the deny is simply bypassed. Only a test catches that. |

**Measure 2 — host the grant in the master realm.**

| Question | Measured on 26.6.4 |
|---|---|
| Does it work? | **Yes.** A master client (`sse-realm-steward`, `client-jwt`) whose service account holds `manage-realm` of the master client `smart-sentinel-eye-realm` mints a master token that reads `realm.defaultRole.name`, reads the composite and `offline_access` (200), `DELETE`s it (204), re-runs as a no-op, and reads the app realm's keys (200 — the same authority migration-runner holds today). It cannot list master clients (403). |
| Can `identity-admin` see or edit it? | **No**, even with `manage-clients`: list/read/PUT/regenerate the master client and map roles on its service account all 403; the `<realm>-realm` client does not appear in the app realm's client list. |
| Declarable in `master-realm.json`? | **Only by leaning on undocumented import behaviour.** Keycloak imports `master` first, so a role mapping onto `smart-sentinel-eye-realm` fails the whole start (`Unable to find client role mappings for client: smart-sentinel-eye-realm`, measured). Pre-declaring that client in `master-realm.json` with a `manage-realm` role *did* work: the app-realm import adopted the existing client (one client, full 17-role set, master `admin` still composite over it) and the steward's mapping held. That adoption is not a documented contract. |
| Otherwise, who bootstraps it? | Something holding master-admin authority, after import, on every fresh realm — measured: the bootstrap admin's `admin-cli` token maps the role (204). In AppHost that is the `KeycloakPassword` parameter; giving it to the migrator hands a one-shot worker **master** admin — wider than what it holds today. The integration fixture already mints `admin-cli` master tokens with it (`BruteForceLockoutIntegrationTests`, `LockoutSessionSurvivalIntegrationTests`): precedent for tests, not for a runtime component. |
| Production | The steward's private key authenticates a `manage-realm` holder over the app realm — the same value migration-runner's key has under Decision 3, now in a second realm file whose production source is as undecided as the app realm's (#2878, #1015). If bootstrapped at runtime instead, the bootstrap admin credential joins the automation path, and that credential governs every realm. |

**Not spiked — hold `manage-realm` only briefly (option c).** It does not close the hop on its own:
`identity-admin` can rewrite migration-runner's key at any time and simply wait for the next grant window;
and whoever grants and revokes is itself a standing `manage-realm` (or master-admin) holder, which is
measure 2's bootstrap problem again. ADR-0181 already rejected it for the every-boot narrowing.

**Comparison.**

| | Measure 1: FGAP (+ narrowed `identity-admin`) | Measure 2: master-realm steward |
|---|---|---|
| Closes both measured breaks | Yes (measured) | Yes (measured) — no `manage-realm` holder left in the app realm |
| Protects against a *future* `manage-clients` holder | No — each new holder of `manage-clients` bypasses the deny silently | Yes — no app-realm role reaches the master realm |
| Declarative, no runtime bootstrap | Yes (documented realm-import fields) | Only via undocumented client adoption; otherwise a master-admin bootstrap step |
| Changes `identity-admin` | Yes: loses `manage-clients`, `view-clients`; gains FGAP grants | No |
| Changes the migrator | No (Decision 1 as written) | Authenticates against `master`, different client id; app-realm `migration-runner` keeps only `query-groups`/`view-users` |
| Realm-file surface | `adminPermissionsEnabled`, `admin-permissions` authz block, pinned migration-runner `id` | New master client + public key, and either a pre-declared `<realm>-realm` client or a bootstrap step |
| Regression mode | Pinned id dropped: loud. Role re-added: **silent** | Adoption behaviour changes in a Keycloak upgrade: **loud** (start fails) or mapping silently absent (the narrowing 403s, which ADR-0181's migrator already reports) |
| Residue | All-clients grant can rewrite system clients (`realm-management` measured; no escalation found) | Master-realm key is a second `manage-realm`-grade secret for #2878; upgrade risk on adoption |
| Proving test | Fixture test: `identity-admin` runs both breaks against migration-runner → all 403, **and** runs the 13 runtime flows → unchanged; plus a realm-file test that `identity-admin` holds neither `manage-clients` nor `view-clients` | Fixture test: steward narrows; `identity-admin` gets 403 on every master endpoint; app realm has no `manage-realm` holder (extends spec 440's architecture test) |

## Decision

**1. `migration-runner` authenticates by signed JWT (Keycloak "Signed Jwt", `clientAuthenticatorType:
client-jwt`), not by a client secret.** A key pair is generated outside Keycloak; only the **public** key
is registered on the client (realm file), and the **private** key is held solely by the MigrationRunner
process. Because Keycloak stores only the public key, no admin role — including `identity-admin`'s
`manage-clients` — can read back a credential that authenticates as migration-runner. This replaces
ADR-0181 Decision 2's implicit assumption that the account authenticates by the shared dev secret; every
other part of ADR-0181 (the permanent `manage-realm` grant, the on-every-boot narrowing, least privilege,
the enrolment strip, mandatory security review) stands unchanged and now stands on this credential.

**2. The assertion is minimal and replay-resistant.** It is signed RS256, `iss` and `sub` are the client
id, `aud` is the realm token endpoint, `jti` is a fresh GUID per request, and its lifetime is short (≤ 60 s).
Keycloak rejects a reused `jti` (measured). MigrationRunner mints one assertion per token request; it does
not cache or reuse assertions.

**3. The private key is a dev-only value in dev/CI/test, provisioned as a secret in production.** Like
every client secret in the realm file today, the dev key pair is a non-production value: the public key
lives in the realm file, and the private key is supplied to MigrationRunner through an AppHost parameter
with a dev-only default, never a production-reachable default. There is no production deployment yet
(ADR-0130); a production deployment provisions both the private key (to the worker) and the registered
public key (to the realm) as secrets. This is added to the #2878 templated-secret obligation **as two
coupled items — the migration-runner private key and its registered public key** — and #2878's description
of migration-runner's credential as a "realm-takeover credential" is adopted: it authenticates a
`manage-realm` holder.

**4. Key rotation is a realm-file edit plus a worker-config edit, done together.** Rotating the key means
registering a new public key on the client and supplying the new private key to the worker in the same
change; the realm file carries exactly one current public key. No grant-then-revoke, consistent with
ADR-0181.

**5. `identity-admin` administers clients through FGAP v2, with migration-runner denied — declared in the
realm file** (owner ruling, 2026-10-23: measure 1). In `smart-sentinel-eye-realm.json`:
- `identity-admin`'s service account **loses `manage-clients` and `view-clients`** (`realm-management`).
  Measured: while it holds either, a FGAP deny is bypassed — a realm-management role is not restricted by
  FGAP v2; permissions only add access.
- The realm sets `"adminPermissionsEnabled": true` and declares the `admin-permissions` client's
  `authorizationSettings`: a user policy naming `service-account-identity-admin`, and an **all-clients**
  permission granting it `view` and `manage`.
- A **migration-runner-only** permission (`view`, `manage`, `map-roles`, `map-roles-composite`,
  `map-roles-client-scope`) whose policy is that same user with **`NEGATIVE`** logic — a DENY scoped to
  that one client, and to no other.
- migration-runner's client **`"id"` is pinned** in the realm file, because the per-client permission names
  its resource by UUID. No client in the realm file carries a pinned id today.

Nothing applies this at runtime: it is realm-import content, so every fresh import (CI, Aspire fixture,
E2E) gets it, and no master-admin credential enters automation. The migrator and Decisions 1-4 are
unchanged. Option (b) and measure 1 compose: (b) makes the stored credential unreadable, measure 1 makes
the client unrewritable by `identity-admin`.

**What the spike verified (Keycloak 26.6.4, fresh import of both realm files, five containers).** FGAP v2
is on by default with no feature flag; the realm-import fields carry both permissions and the deny holds
after a fresh import. With the four measures above, on migration-runner `identity-admin` gets: read 403,
secret read 403, both breaking `PUT`s 403, regenerate 403, `DELETE` 403, and the client is absent from
`GET /clients` and from the `?clientId=` lookup. All 13 Identity runtime flows, replayed over HTTP, are
unchanged (create 201, rotate 200, disable 204, delete 204, group join 204, role strip 204, …), and
`create-client` is not needed. Counterfactual: with the deny removed, the all-clients grant alone lets both
breaks through again — the deny is what blocks. Routes around it are closed (read or delete the FGAP
permissions 403, disable authorization on or delete `admin-permissions` 400, map `manage-realm` onto a
client it creates 403, impersonate migration-runner's service-account user 403, set its password 400). The
migrator's every-boot narrowing is unaffected (200/204).

**What is explicitly NOT verified** — each is an obligation on the implementing PR or a standing caveat,
not an assumption this ADR may rely on:
- **The real C# adapter and the Identity integration suite under the narrowed role set.** Only the 13 admin
  HTTP calls `HttpKeycloakAdminClient` makes were replayed; the adapter itself, its error mapping, and the
  integration suite have not run against a realm where `identity-admin` lacks `manage-clients`/`view-clients`.
- **FGAP v2 semantics across Keycloak upgrades.** v2 is new since 26.2 and was measured only on 26.6.4; a
  Keycloak bump must re-run the deny tests before it merges.
- **Whether the deny should also cover the system clients** (`realm-management`, `admin-permissions`,
  `broker`, `admin-cli`, `security-admin-console`). The all-clients grant lets `identity-admin` rewrite them
  — measured on `realm-management`: converted to a confidential service-account client, secret regenerated,
  token minted (200) — but that token held no `manage-realm` (403), and no escalation through a system
  client was found. Not found is not ruled out.
- **Other `manage-users` routes over migration-runner's service-account user** beyond the three probed
  (impersonation, password set, role mapping onto a created client).

**6. The FGAP deny is guarded by a realm-file test, because its likeliest regression is silent.** Two
regressions were measured, and they fail differently:
- **Dropping migration-runner's pinned `id` fails loud** — Keycloak refuses to start (`Resource [<uuid>]
  does not exist for type [Clients]`), so the fixture and every stack boot catch it.
- **Re-adding `manage-clients` or `view-clients` to `identity-admin` fails silent** — the deny is simply
  bypassed and every flow still works. Only a test catches this, so a realm-file test asserting
  `identity-admin` holds **neither** role (directly, via a group, or via a composite) is part of this
  decision, not an optional extra. The same test asserts the migration-runner deny permission exists, names
  the pinned id, and carries `NEGATIVE` logic.

The trade-off accepted with measure 1: it protects migration-runner from `identity-admin` only. Any
*future* holder of `manage-clients` in the app realm bypasses the deny the same way, so granting
`manage-clients` to anything becomes a change that needs a security review against this ADR. Measure 2
(master-realm steward) would have been immune to that; it was declined (Alternatives Considered).

## Consequences

**Positive: the credential cannot be exfiltrated by reading it.** The private key is never in Keycloak,
never in the realm file (only the public key is), and no admin role returns it. The two-hop
read-secret-then-mint-token path that the security review found is closed (measured: the stored secret is
`invalid_client` under `client-jwt`).

**Positive: the reconfigure hop is closed as well (measured).** Option (b) alone left two routes:
`identity-admin` replacing migration-runner's registered public key with its own, or switching it back to
`client-secret` and regenerating. Under Decision 5 both `PUT`s are 403 and `identity-admin` can no longer
see the client at all. Neither those routes nor the read-back route reaches `manage-realm` from the live
Identity API credential.

**Negative: `identity-admin`'s client authority moves from a role to a policy.** Its client administration
is now an FGAP grant in the `admin-permissions` client's authorization settings — a less familiar place
than a `realm-management` role mapping, and one a reader of the realm file must know about. The realm file
grows an `authorizationSettings` block and its first pinned client `id`.

**Negative: the guarantee is specific to `identity-admin` and silently regressible.** A re-added
`manage-clients`/`view-clients`, or any new `manage-clients` holder, bypasses the deny with no symptom. The
realm-file test (Decision 6) is the only guard; it is required, not advisory. A dropped pinned id, by
contrast, fails the boot.

**Negative: `identity-admin` can still rewrite the system clients.** The all-clients grant covers
`realm-management`, `admin-permissions`, `broker`, `admin-cli` and `security-admin-console`. No escalation
was found (the measured `realm-management` token held no `manage-realm`), but whether to extend the deny to
them is open (Decision 5, not verified).

**Negative: FGAP v2 is young.** It became the default in Keycloak 26.2 and this decision was measured on
26.6.4 only. Every Keycloak version bump re-runs the deny tests before merging.

**Negative: a second credential kind to operate.** The system now provisions and rotates a key pair for one
account, distinct from the shared-secret pattern every other service account uses. The surface is one
account, and the worker is one-shot, so the operational cost is bounded; it is still a divergence a reader
must know about.

**Positive: verifiable.** That the signed-JWT client rejects its own former secret, that the reconfigure
routes are denied, and that the Identity runtime flows survive the narrowed roles, are integration-testable against the Aspire fixture's
freshly imported realm (see Implementation Notes).

**Neutral/carried from ADR-0181:** the permanent `manage-realm` grant, the on-every-boot idempotent
narrowing, the 30-day idle vendor default, and the dev-vs-persistent-realm trap are unchanged. The realm
file remains a contention file, one PR at a time.

## Alternatives Considered

**Keep the client secret, accept the two-hop risk in writing (option d).** Rejected by the owner: the live
Identity credential should not reach realm-wide authority, even indirectly.

**Grant `manage-realm` only for the boot step and revoke it after (option c).** A dedicated short-lived
bootstrap credential. Rejected earlier (ADR-0181 Decision 2 made the grant permanent because the narrowing
re-runs every boot); not reopened here.

**Fine-grained admin permissions as the whole fix, leaving the secret (option a alone).** Denies
`identity-admin` access to the migration-runner client without changing migration-runner's credential kind.
The owner chose to change the credential kind (b) *and* adopt FGAP (Decision 5); the two compose. FGAP
alone would leave a readable secret guarded by a single policy, and a per-client deny does nothing while
`identity-admin` holds `manage-clients` (measured), so (a) is never a fix on its own.

**Host the `manage-realm` grant on a master-realm client (measure 2).** Spiked and working (Context), and
immune to future app-realm `manage-clients` holders. Declined by the owner on 2026-10-23: declaring it in
`master-realm.json` relies on Keycloak adopting a pre-declared `<realm>-realm` client during import, which
is not a documented contract; the alternative is a runtime bootstrap with the master admin credential,
which puts master-wide authority into automation; and it adds a second `manage-realm`-grade secret to
#2878. Revisit if the app realm ever needs a second `manage-clients` holder.

**FGAP deny with `manage-clients` kept on `identity-admin`.** The smallest realm-file change. Measured not
to work: FGAP v2 does not restrict a `realm-management` role, so both breaks still succeed.

**A public key by `jwt.credential.certificate` (a self-signed X.509 cert attribute) instead of a JWKS.**
Both are accepted by Keycloak for `client-jwt`. `jwks.string` (or `use.jwks.url` pointing at a JWKS the
worker publishes) is preferred for a service account minting its own assertions because it carries a raw
public key with a `kid` and no certificate lifetime to track; the cert-attribute form is the fallback. This
is an Implementation-Notes choice, recorded so the realm-file author does not have to rediscover it.

## Implementation Notes

What the autonomous lane builds after approval — given on PR #3106, 2026-10-23 (#1995; spec 440 on `fix/1995-narrow-default-roles` is the
resumption point and is reused; behaviour-changing, phase 4a **red**; `infra-engineer` plus a backend slice
for the assertion; **`security-reviewer` mandatory** at phase 6).

1. **Realm file** (`src/AppHost/Realms/smart-sentinel-eye-realm.json`, contention file, one PR at a time).
   On the `migration-runner` client: pin `"id"` (a fixed UUID), set `"clientAuthenticatorType":
   "client-jwt"`, drop `"secret"`, and add attributes `"use.jwks.string": "true"`, `"jwks.string": "<JWKS with one RS256 public key, kid set>"`,
   `"use.jwks.url": "false"`, `"token.endpoint.auth.signing.alg": "RS256"`. Keep the `manage-realm` grant
   from ADR-0181 on `service-account-migration-runner`. The client `description` (already rewritten by spec
   440) stays **under 255 characters** (a longer one kills the import — Keycloak realm-description limit) and
   says it authenticates by signed JWT and holds `manage-realm`. On `identity-admin`'s service account:
   remove `manage-clients` and `view-clients`, keep its other roles. On the realm: `"adminPermissionsEnabled":
   true`, plus the `admin-permissions` client's `authorizationSettings` per Decision 5 (user policy for
   `service-account-identity-admin`; all-clients `view`+`manage` permission; migration-runner permission
   referencing the pinned UUID with a `NEGATIVE` policy). Take the exact JSON shape from a realm export of
   a working FGAP v2 configuration, not from memory.
2. **Assertion signing in `MigrationRunner`.** Build the `client_assertion` with
   `System.IdentityModel.Tokens.Jwt` (`JwtSecurityTokenHandler` + `SigningCredentials(RsaSecurityKey,
   SecurityAlgorithms.RsaSha256)`) — a maintained in-tree dependency, not a hand-rolled JWT. `iss`/`sub` =
   `migration-runner`, `aud` = the realm token endpoint, fresh `jti` per request, `exp` ≤ 60 s. Send
   `grant_type=client_credentials`,
   `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`, `client_assertion=<jwt>`.
   This replaces the client-secret token acquisition wherever MigrationRunner obtains its admin token; no
   `RetryEveryMethod()` is added (ADR-0143 — a token mint that opts in already states its reason; a signed
   assertion mint is likewise idempotent in fact and states why if it opts in).
3. **Key material and AppHost wiring.** A dev-only RSA key pair: the public key baked into the realm file
   (step 1), the private key supplied to MigrationRunner via an AppHost parameter with a dev-only default
   (never a production-reachable default), read by the worker from config. CI/E2E import the realm fresh and
   use the same dev key pair. #2878's templated-secret list gains both the private key and the registered
   public key as a coupled pair.
4. **Tests** (`tests/Integration.Tests/Identity/`, Aspire fixture, fresh import each run), extending spec
   440's suite:
   - MigrationRunner obtains a `manage-realm` token by signed JWT and narrows the composite (the ADR-0181
     behaviour, now over the new credential).
   - `identity-admin` **cannot reconfigure** the migration-runner client into a credential it can use: the
     test attempts the two measured breaks — `PUT` replacing `jwks.string`, and `PUT` switching to
     `client-secret` then regenerate — and asserts each is **403** and that no `manage-realm` token
     results. **Red today** (both PUTs return 204 against the realm as it stands); green once Decision 5
     lands. The lane must not skip, weaken or drop it (ADR-0144).
   - `identity-admin` cannot read migration-runner or its secret, cannot `DELETE` it, and does not find it
     via `GET /clients` or `?clientId=`.
   - **The real `HttpKeycloakAdminClient` and the existing Identity integration suite pass under the
     narrowed role set** — the first unverified item in Decision 5. A failure here is a finding against
     Decision 5, reported to the owner, not a test to adjust.
   - **Realm-file test (Decision 6):** `identity-admin` holds neither `manage-clients` nor `view-clients`,
     directly, through a group, or through a composite; the migration-runner deny permission exists,
     targets the pinned id, and its policy is `NEGATIVE`. Prove it by counterfactual — re-add
     `manage-clients` and observe it red.
   - The former client secret does not authenticate migration-runner (`invalid_client`).
   - The ADR-0181 assertions (new account lacks `offline_access`; each `wall-*` holds it;
     `realm.defaultRole.name` is `default-roles-smart-sentinel-eye` with no `-1` sibling; the re-run is a
     no-op) still pass.
5. **Review items carried from the phase-6 findings into this amendment's scope** (each to be addressed in
   the implementing PR):
   - The 403 message must **not** advise dropping the `keycloak-data` volume outside the dev lane; it offers
     the non-destructive fix first (re-run the migrator / check the grant) and names the volume drop only as
     the dev-stack remedy.
   - The stale "read-only" comments at `AppHost.cs:30-34` and `IdentityInfrastructureModule.cs:260-262` are
     corrected to describe the current behaviour.
   - The architecture test must cover **group and composite** grants of `manage-realm`, not only a direct
     user role mapping.
   - `KioskInheritedPrivilegeIntegrationTests`' class comment and assertion message are now false; correct
     them, and cover the runtime strip end-to-end or write a note saying where it is covered.
   - README: record the persistent-stack consequence (a realm-file change reaches the long-lived dev stack
     only after the `keycloak-data` volume is dropped; coordinate with #91).
   - Read the default role name from the realm (`realm.defaultRole.name`) rather than assuming
     `default-roles-{realm}`.
6. **Open questions for the owner** (unchanged behaviour until ruled on; accepting the ADR on 2026-10-23
   did not rule them, and none blocks the implementation):
   - Should the FGAP deny also cover the system clients (`realm-management`, `admin-permissions`,
     `broker`, `admin-cli`, `security-admin-console`)? Rewriting them is possible; no escalation was found
     (Decision 5).
   - Should the remaining `manage-users` routes over migration-runner's service-account user be probed
     before acceptance, or in the implementing PR's security review?
   - Should `integration-test-admin` / other `manage-clients` holders be examined for the same reconfigure
     path? (Not measured here; under Decision 6 any app-realm `manage-clients` holder is a security-review
     item.)
   - Does the signed-JWT credential or the narrowed `identity-admin` change anything for ADR-0160's
     revocation-list or the enrolment flow that also uses `identity-admin`? (Believed no; to confirm in
     review — the enrolment flow is among the C# paths not yet run under the narrowed roles.)
7. **Follow-up on #1995.** The issue stays open until the implementing PR lands and its security review
   passes. Its scope is now spec 440's narrowing (ADR-0181) + Decisions 1-4 (signed JWT) + Decisions 5-6
   (FGAP and its realm-file guard), delivered together on `fix/1995-narrow-default-roles`: the realm file is
   a contention file, and the reconfigure test is only green with all of them in. A Keycloak version bump
   becomes a re-run trigger for the deny tests.

   **#1995 may now resume** (accepted on PR #3106, 2026-10-23). It does not resume at phase 4: spec 440's
   `plan.md` and `tasks.md` must first be updated to carry Decisions 1-6 (the signed-JWT credential, the
   FGAP grant and deny, the pinned id, and the Decision 6 realm-file guard), and phase 4 starts from those
   updated artefacts.
