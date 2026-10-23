# ADR-0186: The migration runner authenticates by signed JWT, so no admin role can read its credential back

**Status:** **Proposed** (awaiting owner approval; records the owner ruling of 2026-10-23 on #1995, option (b))
**Date:** 2026-10-23
**Amends:** ADR-0181 (its Decision 2 — how `migration-runner` authenticates; the permanent `manage-realm` grant is unchanged and stands on this basis)
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
ability to UPDATE that one client. This is the key open question below, not a decision this ADR makes.

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

**5. This ADR does not, by itself, remove `manage-clients` from `identity-admin` or add fine-grained admin
permissions.** It records that option (b) closes the *read-back* hop and leaves the *reconfigure* hop open
(measured), and it carries that as the headline open question for the owner (below). The owner ruled option
(b); options (a) and the `manage-clients` narrowing are presented as consequences/open questions, not
decided here.

## Consequences

**Positive: the credential cannot be exfiltrated by reading it.** The private key is never in Keycloak,
never in the realm file (only the public key is), and no admin role returns it. The two-hop
read-secret-then-mint-token path that the security review found is closed (measured: the stored secret is
`invalid_client` under `client-jwt`).

**Negative — the key open risk, measured, for the owner: `manage-clients` on `identity-admin` can still
reach `manage-realm` by *reconfiguring* the migration-runner client**, in two ways (table above): replacing
the registered public key with its own, or switching the authenticator back to `client-secret` and
regenerating. Option (b) narrows the exposure (an attacker must now actively rewrite a client's credential
config, a conspicuous admin write, rather than silently read a secret) but does **not** eliminate it on its
own. What additionally closes it — to be decided by the owner, not here:
- **(a) fine-grained admin permissions** denying `identity-admin` UPDATE on the single migration-runner
  client (Keycloak's per-client admin permissions), leaving its UPDATE rights on every other client intact; or
- **removing `manage-clients` from `identity-admin`'s role set** and replacing it with per-client or
  narrower roles for the clients the Identity API legitimately manages (enrolment, device registration,
  webhook rotation), so no single declared role can UPDATE migration-runner.
  Either path is itself an amendment to this ADR and ADR-0181 and wants its own security review.

**Negative: a second credential kind to operate.** The system now provisions and rotates a key pair for one
account, distinct from the shared-secret pattern every other service account uses. The surface is one
account, and the worker is one-shot, so the operational cost is bounded; it is still a divergence a reader
must know about.

**Positive: verifiable.** That the signed-JWT client rejects its own former secret, and that the
reconfigure routes behave as the table records, are integration-testable against the Aspire fixture's
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
The owner instead chose to change the credential kind (b); (a) remains available as the additional measure
the open question above describes, and would compose with (b).

**A public key by `jwt.credential.certificate` (a self-signed X.509 cert attribute) instead of a JWKS.**
Both are accepted by Keycloak for `client-jwt`. `jwks.string` (or `use.jwks.url` pointing at a JWKS the
worker publishes) is preferred for a service account minting its own assertions because it carries a raw
public key with a `kid` and no certificate lifetime to track; the cert-attribute form is the fallback. This
is an Implementation-Notes choice, recorded so the realm-file author does not have to rediscover it.

## Implementation Notes

What the autonomous lane builds after approval (#1995; spec 440 on `fix/1995-narrow-default-roles` is the
resumption point and is reused; behaviour-changing, phase 4a **red**; `infra-engineer` plus a backend slice
for the assertion; **`security-reviewer` mandatory** at phase 6).

1. **Realm file** (`src/AppHost/Realms/smart-sentinel-eye-realm.json`, contention file, one PR at a time).
   On the `migration-runner` client: set `"clientAuthenticatorType": "client-jwt"`, drop `"secret"`, and
   add attributes `"use.jwks.string": "true"`, `"jwks.string": "<JWKS with one RS256 public key, kid set>"`,
   `"use.jwks.url": "false"`, `"token.endpoint.auth.signing.alg": "RS256"`. Keep the `manage-realm` grant
   from ADR-0181 on `service-account-migration-runner`. The client `description` (already rewritten by spec
   440) stays **under 255 characters** (a longer one kills the import — Keycloak realm-description limit) and
   says it authenticates by signed JWT and holds `manage-realm`.
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
     `client-secret` then regenerate — and asserts the escalation is **denied**. **This test is red today**
     against the realm as it stands (both PUTs return 204), and it stays red until the owner's choice on the
     open question (fine-grained permission (a), or removing `manage-clients`) is implemented. If that choice
     is deferred, this test is the standing record that the hole is open: ship it **skipped with the reason
     quoted**, or as an asserting-the-current-(insecure)-behaviour characterisation that must invert when the
     fix lands — the PR body states which, and the security-reviewer confirms (ADR-0144 forbids weakening a
     gate to reach green). The lane must not silently drop it.
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
6. **Open questions for the owner** (unchanged behaviour until ruled on):
   - **Does `manage-clients` on `identity-admin` still defeat option (b)? Yes, measured** — via client
     *reconfiguration* (replace the public key, or revert to a secret). Which additional measure closes it:
     fine-grained admin permission (a) on the one client, or removing/narrowing `manage-clients` on
     `identity-admin`? Either is an amendment to this ADR.
   - Should `integration-test-admin` / other `manage-clients` holders be examined for the same reconfigure
     path? (Not measured here.)
   - Does the signed-JWT credential change anything for ADR-0160's revocation-list or the enrolment flow that
     also uses `identity-admin`? (Believed no; to confirm in review.)
