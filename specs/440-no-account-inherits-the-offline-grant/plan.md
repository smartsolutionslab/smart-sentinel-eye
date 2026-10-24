# Plan 440 — No account inherits the offline grant

**Spec:** [spec.md](spec.md) (revision 2) · **Issue:** #1995 · **ADRs:** ADR-0181 (Accepted), **ADR-0186** (Accepted on
PR #3106, 2026-10-23), **ADR-0187** (Accepted on PR #3119, 2026-10-24; amends ADR-0186 Decisions 5-6) — both on
`develop` · **Phase:** 2 (revision 3: §13)
**Constitution check:** no new architecture. ADR-0181 decides the narrowing; ADR-0186 decides the credential kind, the
assertion, the key custody, rotation, the FGAP grant/deny and its guard. This plan places them. No
`[NEEDS CLARIFICATION]`. **No new ADR needed** — the three placement choices below that the ADRs do not literally
spell out (§2.1 where the signing lives, §2.3 the retry stance, §2.4 the key encoding and `kid`) are implementation
detail inside a decided boundary, recorded here for the owner to see at the gate.

## 0. What revision 1 built, and what this revision does to it

| Revision-1 piece (commit) | Fate |
|---|---|
| `RemoveRealmDefaultPrivilegeAsync` port + 9 doubles (`8f2c4d32`, `68c9a641`) | **kept** (signature unchanged) |
| Adapter: GET role, DELETE `default-roles-{realm}` composites (`68c9a641`) | **revised** — reads `defaultRole.name` first (§3) |
| `RealmDefaultPrivilegeMigrator`, registered first, scoped, no swallow (`68c9a641`) | **kept**; its 403 verdict **revised**, a mint-refusal verdict **added** (§4) |
| Realm file: `manage-realm` on the service account; description (`68c9a641`) | **kept** grant; client **revised** to `client-jwt` + pinned id; description **revised** (§5) |
| Prose corrections, four sites (`96f5b3ad`) | **kept**; four more sites added (§8) |
| T-A adapter tests (`8f2c4d32`) | **revised** — request sequence grows by one `GET` |
| T-B migrator tests (`8f2c4d32`) | **revised** — 403 message assertions; mint-refusal case added |
| T-C `MigrationRunnerClientTests` (`8f2c4d32`) | **revised** — role-holder guard widened to groups/composites; description fact |
| T-D `KioskInheritedPrivilegeIntegrationTests` (`8f2c4d32`) | **kept**, except `Narrowing_again_as_migration_runner…` re-pointed to the signed-JWT credential, and the class comment / first message (§8) |
| Secret-based authentication of migration-runner (realm file `secret`, AppHost `MigrationRunnerClientSecret`, `Keycloak__AdminClientSecret` on `migrations`) | **replaced** by the signed JWT (§2, §6) |

No revision-1 commit is reverted or rewritten; revision 2 lands as further commits on the same branch (the branch is
unpushed, but rewriting history that a review already read buys nothing). **Revision 3 makes one exception** (§13.6):
the realm commit and the AppHost commit are squashed, because the first alone leaves the stack unbootable.

## 1. Shape: no domain; one credential mint, one adapter read, realm-file authorisation

Still no aggregate, value object or event — credential mechanics and provider authorisation config are
infrastructure, and constitution §II binds domain models only. Every primitive below sits at a wire/config boundary
(the exemption `ClientCredentials` already documents).

| Layer / project | Change |
|---|---|
| `Identity/Domain`, `Identity/Api`, `Shared.Contracts` | none |
| `ServiceDefaults/Authentication/` | + `SignedJwtClientCredentials` record; + `ClientAssertion` (builds the RS256 assertion); `ClientCredentialsTokenProvider` gains a second constructor taking `Func<SignedJwtClientCredentials>` — both paths reduce to (token URL, form fields); the five secret callers are **untouched** |
| `Identity/Infrastructure/KeycloakAdmin/KeycloakAdminOptions.cs` | + `AdminClientPrivateKey` (string, empty default) |
| `Identity/Infrastructure/KeycloakAdmin/KeycloakAdminTokenProvider.cs` | chooses the mint from configuration (FR-011); + `AssertionHttpClientName` |
| `Identity/Infrastructure/IdentityInfrastructureModule.cs` | registers the assertion named client **without** `RetryEveryMethod()` and says why; L260-262 comment corrected |
| `Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.Realm.cs` | reads `defaultRole.name` before the DELETE |
| `MigrationRunner/RealmDefaultPrivilegeMigrator.cs` | verdicts per FR-004(a)/(b) |
| `src/AppHost/AppHost.cs` | `MigrationRunnerClientSecret` → `MigrationRunnerClientPrivateKey`; env var; L30-34 comment |
| `src/AppHost/OverridableParameters.cs` | parameter-count comment (L32-35) |
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | migration-runner `client-jwt` + pinned id + JWKS; identity-admin narrowed; FGAP v2 block (contention file — one PR at a time) |
| `README.md` | one line (FR-018) |

**Boundary:** no new cross-context reference. MigrationRunner already composes `AddKeycloakAdminClient` (spec 019);
ServiceDefaults is referenced by every service. `BoundaryTests` unaffected.

## 2. The signed-JWT mint (ADR-0186 Decisions 1-4; FR-009..012)

### 2.1 Where the signing lives — deviation from the ADR's wording, flagged

ADR-0186 note 2 says "Assertion signing in `MigrationRunner`". MigrationRunner does not mint its own token: it composes
Identity's `AddKeycloakAdminClient`, whose `KeycloakAdminAuthorizationHandler` depends on the concrete, sealed
`KeycloakAdminTokenProvider`, which delegates to ServiceDefaults' shared `ClientCredentialsTokenProvider` — the type that
exists precisely so the mint is written once (its own doc: four copies had drifted three ways). Signing inside the
MigrationRunner project would mean replacing that provider chain there, i.e. a fifth mint.

**So the signing primitive lives beside the shared mint in ServiceDefaults, and the private key reaches only the
MigrationRunner process** (only `migrations` receives `Keycloak__AdminClientPrivateKey`). That is the property the
ADR's note protects ("the private key never leaves the MigrationRunner process"). The owner should confirm this
reading at the gate; if the note was meant literally, the alternative is a MigrationRunner-local
`KeycloakAdminTokenProvider` replacement and is a plan change, not an ADR change.

### 2.2 Shapes

```csharp
// ServiceDefaults/Authentication — wire-boundary record, primitives on purpose (same exemption as ClientCredentials)
public sealed record SignedJwtClientCredentials(
    string Authority, string Realm, string ClientIdentifier, string PrivateKeyPkcs8Base64);

// internal; one fresh assertion per call
internal static class ClientAssertion
{
    // RS256; header kid = RFC 7638 thumbprint of the public key
    // (JsonWebKeyConverter.ConvertFromRSASecurityKey(...).ComputeJwkThumbprint());
    // iss = sub = clientIdentifier; aud = tokenEndpoint; jti = Guid.CreateVersion7(); iat = now; exp = now + 60 s.
    public static string Create(string clientIdentifier, string tokenEndpoint, RSA key, TimeProvider clock);
}
```

`ClientCredentialsTokenProvider` keeps its cache, gate and 80 % refresh unchanged; only the form differs:
`grant_type=client_credentials`, `client_id`, `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`,
`client_assertion=<jwt>`. `client_id` is sent too (Keycloak accepts it alongside the assertion; it keeps the log line's
`ClientIdentifier` honest). The `RSA` instance is created per mint from the base64 and disposed — mints are rare (one
per ~5 min per process at most) and the worker is one-shot, so caching the key object is not worth a field.

`JwtSecurityTokenHandler` per ADR-0186 note 2. `System.IdentityModel.Tokens.Jwt` is already in the build transitively
via `Microsoft.AspNetCore.Authentication.JwtBearer` (spec §2b); **no `Directory.Packages.props` change**. If the analyzer
or the build demands a direct reference, add it to ServiceDefaults' csproj with the version the transitive graph already
resolves (central package management), and say so in the PR.

### 2.3 Retry stance (FR-010) — ADR-0186 note 2's parenthesis is inaccurate, flagged

ADR-0186 note 2: "no `RetryEveryMethod()` is added (… a signed assertion mint is likewise idempotent in fact and states
why if it opts in)". The first half is followed exactly. The parenthesis is not true at the HTTP layer: the resilience
handler resends the **same request content**, i.e. the same `jti`, and Keycloak rejects a reused `jti`
(`400 Token reuse detected`, ADR-0186's own measurement). So the assertion mint gets its own named client,
`KeycloakAdminTokenProvider.AssertionHttpClientName`, registered **without** `RetryEveryMethod()`, with a one-line *why*
at the registration. Effect: one attempt per mint. A transient Keycloak failure at boot fails the migrator — the same
loud outcome as any other refused narrowing (ADR-0181 §3), and `migrations` already `WaitFor(keycloak)`.
This needs no ADR (ADR-0143's default is what applies); the owner may want ADR-0186's parenthesis corrected by a
docs-only follow-up — **the lane may not edit the ADR**.

### 2.4 Key encoding, `kid`, and the dev pair (Decisions 3-4; FR-011, FR-012)

- Private key: **base64 of PKCS#8 DER, one line** — survives `Parameters:`, environment variables and user-secrets
  without newline handling (a PEM would not; spec A-5). Parsed with `RSA.ImportPkcs8PrivateKey`.
- `kid`: RFC 7638 thumbprint, computed at runtime from the key — so configuration carries one value, and the realm
  file's `kid` is checkable against it (AS-17). Keycloak's `client-jwt` with `jwks.string` matches the assertion's
  `kid` against the JWKS.
- Dev pair: RSA 2048, generated once by the infra slice; the private half is the `AddOverridableParameter` default in
  `AppHost.cs` (dev-only, like every `dev-only-*` secret), the public half is the JWKS in the realm file. The PR
  states the generation command so rotation (Decision 4) is reproducible; both halves change in one commit.
- `KeycloakAdminTokenProvider` selection (FR-011): `AdminClientPrivateKey` non-empty → assertion over
  `AssertionHttpClientName`; else → today's secret path over `HttpClientName`, byte-for-byte as now; both non-empty
  → `InvalidOperationException` on first mint naming `Keycloak:AdminClientSecret` and `Keycloak:AdminClientPrivateKey`.

## 3. The adapter (FR-001, revised)

```
GET    admin/realms/{realm}                                → 200, read defaultRole.name  (new)
GET    admin/realms/{realm}/roles/{privilege}              → 200, role representation
DELETE admin/realms/{realm}/roles/{defaultRoleName}/composites   body [ representation ] → 204
```

Escape `defaultRoleName` like `privilege`. No pre-read of the composite, no early return (kept). A missing
`defaultRole`/`name` throws `InvalidOperationException` naming the realm. `EnsureSuccessStatusCode` on all three.

## 4. The migrator verdicts (FR-004, revised)

Kept: no catch-and-continue; exception filters that **rethrow wrapped** (`InvalidOperationException(verdict, inner)`).

- (a) `HttpRequestException { StatusCode: Forbidden }` — the narrowing refused: checks first (the service-account role
  mapping; that the realm was imported from this file; re-running is safe), then "on the local persistent dev stack
  only: drop `keycloak-data`".
- (b) `HttpRequestException { StatusCode: BadRequest or Unauthorized }` — on this migrator's call path only the token
  mint can produce these (the admin API answers a bad bearer with 401 too, but the bearer here *is* the fresh mint),
  so the verdict names the credential: realm predates ADR-0186, or the registered public key and the configured private
  key are not one pair (Decision 4); then the dev-stack-only volume note.

The adapter stays a translator; the verdicts live in the migrator (unchanged split from revision 1).

## 5. Realm file (FR-005, FR-013, FR-014)

**Contention file.** Take the FGAP v2 JSON shape from an export, not from memory (ADR-0186 note 1): task T050 boots a
throwaway `quay.io/keycloak/keycloak:26.6.4` (`start-dev --import-realm`, no volume, not an Aspire stack), applies
Decision 5 through the admin console/API, exports the realm, and records the relevant fragments in
`specs/440-*/research.md` — the shape the guard (FR-015a) and the edit both use. The keycloak container must be removed
afterwards (`docker rm -f`), and no shared Aspire stack may be running at the time (one machine, one stack).

- `migration-runner` client: `"id": "<fixed UUID v4>"` — the first pinned id in the file; `clientAuthenticatorType`
  `client-jwt`; `secret` removed; JWKS attributes (spec FR-005); description ≤ 255, e.g. *"Reads /fabs (spec 019) and
  holds manage-realm solely to remove offline_access from the default roles on every boot (ADR-0181). Authenticates by
  signed JWT; no secret (ADR-0186)."* — length checked by T-C.
- `service-account-identity-admin`: drop `manage-clients`, `view-clients`.
- Realm root: `"adminPermissionsEnabled": true`; `admin-permissions` client with `authorizationSettings` per FR-014.
- A dropped pinned id fails Keycloak's start (loud, ADR-0186 Decision 6) — the fixture boot catches it.

## 6. AppHost and the surfaces that named the secret (FR-012)

| File | Change | Kind |
|---|---|---|
| `AppHost.cs:30-35` | comment rewritten (reads `/fabs`, holds `manage-realm`, authenticates by signed JWT, ADR-0181/0186); `migrationRunnerClientPrivateKey = AddOverridableParameter("MigrationRunnerClientPrivateKey", "<dev base64>", secret: true)` | behaviour |
| `AppHost.cs:334-335` | `Keycloak__AdminClientPrivateKey` replaces `Keycloak__AdminClientSecret` | behaviour |
| `OverridableParameters.cs:32-35` | "the six `*ClientSecret` values" → five + `MigrationRunnerClientPrivateKey` (count stays a sum) | comment |
| `AppHostComposition/*.snapshot.txt` (5) | regenerated by the snapshot tests' own update path; diff limited to the parameter name and env var | snapshot |
| `AppHostParameterOverrideTests.cs` | `DeclaredDefaults` row and the example parameter re-pointed to `MigrationRunnerClientPrivateKey`; what the tests assert about the override helper is unchanged | test subject renamed — PR says why (the parameter it named no longer exists) |
| `AppHostStackStatusTests.cs:124` | name re-pointed | same |
| `AppHostMigrationGateTests.cs:27` | doc: the spec-128 refusal is now reproduced with a wrong private key | comment |
| `RealmImportMirrorTests.ProductionDefaults.cs:33` | the `MigrationRunnerClientSecret` row **removed** (the client has no secret; `SecretForClient` would throw); replaced by the AS-17 key-pair mirror fact in the same partial class | test |

**Test access to the dev private key** (integration tests that mint as migration-runner): read the AppHost parameter
the way `RealmImportMirrorTests.ValueAsync` does, or keep a test-side copy **with** a row in
`RealmImportMirrorTests.IntegrationTestCopies` — never an unguarded third copy.

## 7. Invariants (system, not domain)

| Invariant | Held by | Asserted by |
|---|---|---|
| The default composite in force carries no `offline_access` after boot | the migrator, every boot | AS-1, AS-4 |
| `realm.defaultRole` is `default-roles-smart-sentinel-eye`, no `-1` sibling | the file not declaring it | AS-4 |
| Exactly the four `wall-*` accounts hold `offline_access` | four `realmRoles` entries | AS-2 + `WallDisplayAccountTests` |
| `manage-realm` is held by `migration-runner` alone, by any route | realm file | FR-015b (file) + AS-7 (running) |
| No credential that authenticates migration-runner is readable through the admin API | `client-jwt`, public key only | AS-11, AS-14 |
| `identity-admin` cannot see, read, rewrite or delete migration-runner | narrowed roles + FGAP deny | AS-13, AS-14 (running) + AS-16 (file) |
| The registered public key and the configured private key are one pair | one commit changes both | AS-17 |
| An assertion is never presented twice | fresh `jti` per mint, no HTTP retry | T-E (unit) |
| A failed narrowing or mint never yields a booted system | no swallow; `MigrationRun` → `Failed` | T-B |

## 8. Prose (FR-006, FR-017, FR-018) — comment-only characterisation

`AppHost.cs:30-34`, `IdentityInfrastructureModule.cs:260-262`, `OverridableParameters.cs:32-35`,
`KioskInheritedPrivilegeIntegrationTests` class comment (L9-27) and the `A_kiosk_enrolled_at_runtime…` assertion
message (a message is not an assertion's subject; the asserted condition `ShouldNotContain(offline_access)` is
unchanged), the strip-coverage note naming `KeycloakRequestSequenceCharacterisationTests`, and the README line.
Characterise by stripping comments, hashing the code, and proving by counterfactual (memory: comment-only change);
for the message string, show the asserted expression is byte-identical.

## 9. Tests (phase 4a — written by test-writer, observed, output quoted verbatim in the PR)

Stubs so red is an assertion, not a compile error: `SignedJwtClientCredentials`, `ClientAssertion.Create` and the new
`ClientCredentialsTokenProvider` constructor throwing `NotImplementedException`; `KeycloakAdminOptions.AdminClientPrivateKey`
property. The engineer may not edit these tests.

**T-A (revised) adapter** — `RealmDefaultPrivilegeRemovalTests.cs`: sequence is `GET realm`, `GET role`, `DELETE
roles/{defaultRole.name}/composites`; a stub realm whose `defaultRole.name` is `custom-default` gets its DELETE on
`custom-default` (**red**: today it targets `default-roles-…`); missing `defaultRole` throws and sends no DELETE;
existing idempotency/403 facts re-expressed over three requests.

**T-B (revised) migrator** — `RealmDefaultPrivilegeMigratorTests.cs`: 403 verdict contains the non-destructive checks
**and** places the volume drop after them, qualified as dev-stack-only (**red**: today's message leads with the drop);
400/401 from the port → wrapped verdict naming the key pair and ADR-0186 (**red**: today it propagates unwrapped);
5xx unchanged.

**T-C (revised) realm-file facts** — `MigrationRunnerClientTests.cs`: description ≤ 255, no "Read-only", mentions
signed JWT; migration-runner has `clientAuthenticatorType` `client-jwt`, no `secret`, a pinned `id`, `use.jwks.string`
true, exactly one RS256 key with a `kid`; `manage-realm` holder guard over users + groups (recursive) + composites +
`realm-admin` (FR-015b). Red: the client facts (today `secret`, no `id`). The widened holder guard is green against
the file — proven by counterfactual (plant `manage-realm` on a group, and in a composite realm role, observe red).

**T-F (new) Decision 6 guard** — `tests/Architecture.Tests/IdentityAdminClientAuthorityTests.cs`: AS-16 in full,
same three "holds" routes + `realm-admin`. **Red today** (identity-admin holds both roles; no FGAP). Counterfactual
after green: re-add `manage-clients` → red, quoted.

**T-E (new) assertion, Docker-free** — `tests/ServiceDefaults.Tests/Authentication/ClientAssertionTests.cs` and
additions to `ClientCredentialsTokenProviderTests.cs`: header `alg` RS256 and `kid` = thumbprint; `iss`=`sub`=client
id; `aud` = token endpoint; `exp - iat` ≤ 60 s (fake `TimeProvider`); two mints → two distinct `jti`; the form has
`client_assertion_type`, `client_assertion`, `client_id`, and **no** `client_secret`; the signature verifies with the
public half. And in `tests/Identity.Infrastructure.Tests/KeycloakAdmin/` (new class `KeycloakAdminCredentialSelectionTests.cs`):
private key set → assertion form posted on `AssertionHttpClientName`; secret set → today's form on `HttpClientName`
(**characterisation**, green before and after); both set → throws naming both keys; the assertion client has no
`RetryEveryMethod` marker (a 503 then 200 stub → exactly one request, the mint fails).

**T-G (new) key-pair mirror** — `RealmImportMirrorTests.ProductionDefaults.cs` (`FixtureLogic`, no containers): AS-17.
**Red today** (no parameter, no JWKS).

**T-H (new) integration confinement, CI-only** — new class
`tests/Integration.Tests/Identity/MigrationRunnerConfinementIntegrationTests.cs` (**needs a `ci-shards/shard-N.filter`
entry** — a missing entry fails deterministically; put it in the shard with the lightest Identity load, as the
engineer measures from the latest green run's timings): AS-10 (signed JWT → token with `manage-realm`), AS-11 (former
secret → `invalid_client`), AS-13 both breaks as `identity-admin` with an in-test attacker RSA key → each 403 and no
token, AS-14 read/secret/delete 403, absent from list and `?clientId=`. **Red today**: AS-10 (client is
client-secret), AS-11 (secret works), AS-13 (204), AS-14 (200). Every test that mutates state restores it in `finally`
(a 204 on today's realm would otherwise leave migration-runner rewritten for the rest of the fixture's run — the test
must snapshot the client representation as master admin first and `PUT` it back).

**T-D (revised)** — `KioskInheritedPrivilegeIntegrationTests.Narrowing_again_as_migration_runner…` builds the adapter
with `AdminClientPrivateKey` instead of the secret (red today: the realm has no JWKS). Everything else in T-D kept.

**AS-15 characterisation** — no new test: the Identity integration subset (`FullyQualifiedName~Integration.Tests.Identity`)
runs **unmodified**; its CI result on the branch *before* the realm edit and *after* is quoted. ADR-0186 lists the
enrolment flow among the unverified C# paths — it is in that subset.

**E2E**: `e2e/wall-outlives-its-session.spec.ts` unmodified.

## 10. Planted-violation counterfactuals (phase 4, quote each red, then revert)

Revision 1's table stands, with its "Add `manage-realm` to `service-account-identity-admin`" row now also catching
via groups/composites. Added:

| Plant | Must turn red |
|---|---|
| Re-add `manage-clients` to `service-account-identity-admin` | T-F (and, CI, T-H AS-13) |
| Re-add `view-clients` only | T-F |
| Grant `manage-clients` to identity-admin via a new group it joins | T-F (group route) |
| Put `manage-realm` into a composite realm role held by `integration-test-admin` | T-C holder guard (composite route) |
| Flip the migration-runner permission's policy to `POSITIVE` | T-F, and CI T-H AS-13 |
| Change the pinned `id` in the client but not in the permission | fixture boot fails (loud) — record the Keycloak error |
| Restore `"secret"` / `client-secret` on migration-runner | T-C client facts; CI T-H AS-11 |
| Replace the realm JWKS with another key | T-G; fixture boot (`migrations` Failed with verdict FR-004(b)) |
| Reuse one assertion across mints (cache it) | T-E distinct-`jti` fact |
| Add `RetryEveryMethod()` to the assertion client | T-E single-attempt fact |
| Hard-code `default-roles-{realm}` again | T-A `custom-default` fact |

## 11. Boundary, messaging, latency

- No messaging, no integration event, no `Shared.Contracts` change. No cross-context reference.
- Latency: N/A — boot-time, before any API starts; one extra admin `GET` per boot.
- Coverage (ADR-0065): ServiceDefaults is a "Shared" gate (≥ 90 %) — `ClientAssertion` and the new provider path must
  be covered by T-E. Infrastructure/MigrationRunner/AppHost ungated.

## 12. Risks

| Risk | Mitigation |
|---|---|
| FGAP JSON written from memory imports wrongly or silently grants nothing | T050 export → `research.md`; T-F asserts the shape; T-H asserts the effect; fixture boot |
| The real adapter breaks under the narrowed roles (ADR-0186's first unverified item) | AS-15 characterisation; a failure is reported to the owner as a finding against Decision 5, not adjusted |
| A re-added `manage-clients` silently bypasses the deny | T-F (Decision 6), counterfactual-proven |
| Persistent dev stacks fail at the mint after merge | by design; FR-004(b) names it; README; #91 |
| A transient Keycloak blip at boot fails the migrator (no mint retry) | accepted (§2.3); `WaitFor(keycloak)`; the run is loud and re-runnable |
| `identity-admin` can rewrite system clients | open question ADR-0186 note 6; PR states it; security-reviewer looks |
| FGAP v2 changes semantics in a Keycloak bump | PR states the re-run obligation (spec §7) |
| ADR-0186 not on `develop` when the PR opens | rebase after #3106 merges; never stack on it |
| A committed dev private key mistaken for a production secret | `dev-only` naming in the parameter's comment; overridable; #2878 carries the production pair (FR-008) |

## 13. Revision 3 — ADR-0187 and the review findings (spec §0b)

**Constitution / ADR check.** ADR-0187 decides the two-policy deny, the guard's extension and the probe rule; this
section places them. Findings 5-8 and 11 are implementation detail inside decided boundaries (ADR-0105, ADR-0143,
ADR-0186 Decision 2). **One item is not placeable without a decision: B-1** (spec §0b) — if T150 confirms H-1 or H-2,
the deny target changes ADR-0187 Decision 1's mechanism and needs an amending ADR; S5 waits for it. Everything else
below may proceed.

### 13.1 Realm file (FR-019) — S5, gated on B-1

`admin-permissions.authorizationSettings.policies` gains a `type: user`, `logic: POSITIVE` policy whose
`config.users` names the seeded user by username (the import form the existing policies use — `research.md` T050);
`deny-migration-runner.config.applyPolicies` becomes `["identity-admin-user-deny", "<new policy>"]`, permission
`decisionStrategy` stays `UNANIMOUS`. `users[]` gains the target: `enabled: false`, nothing else but `username`
(suggested `migration-runner-deny-target`; ADR-0187 leaves the name to the implementation). Whether Keycloak's import
resolves a username in a policy before or after it imports `users[]` is exactly what AS-20 measures — on a throwaway
26.6.4 first (T151, infra-engineer, the T050 procedure: `start-dev --import-realm`, no volume, no Aspire stack
running, `docker rm -f` after), then by the fixture boot. Contention file: only infra-engineer edits it.

### 13.2 Guard (FR-015 rev. 3, AS-16 rev. 3, AS-22) — `tests/Architecture.Tests/`

- `IdentityAdminClientAuthorityTests`: the deny is the migration-runner-scoped permission; it must be `UNANIMOUS` and
  apply (directly or via an aggregate) the NEGATIVE identity-admin policy **and** exactly one POSITIVE user policy
  naming exactly one user; that user must exist in `users[]`, `enabled: false`, and carry no `credentials`,
  `realmRoles`, `clientRoles`, `groups`, `serviceAccountClientId`. A separate fact: **no** permission in the file whose
  applied policies are all NEGATIVE. Counterfactuals: drop the POSITIVE policy; enable the target; give it a realm
  role; a client role; a group; a credential; switch `UNANIMOUS` → `AFFIRMATIVE` — each red. (Shape subject to B-1: if
  the ruling picks a non-user target, the target checks are rewritten to that shape; the "no NEGATIVE-only" fact stays.)
- `RealmGrantGraph`: inherited roles = legacy `defaultRoles` ∪ the recursive composites of the realm role named by
  `defaultRole.name` and of any declared `default-roles-*` role. Green on today's file (ADR-0181 Decision 4 keeps them
  out); counterfactual: declare `defaultRole` + a `default-roles-smart-sentinel-eye` role composing `manage-clients` →
  red in both the Decision 6 guard and the `manage-realm` holder guard (FR-015b).

### 13.3 Integration matrix (FR-020) — added to `MigrationRunnerConfinementIntegrationTests`

Added to the existing `MigrationRunnerConfinementIntegrationTests.cs`, one file (a second part of the class would repeat `[Collection]`, CS0579); the existing `shard-4.filter`
entry covers it; **a renamed or new class needs its own entry**). Token acquisition: `client_credentials` for service
accounts with their realm-file secrets (read through the existing mirror pattern, never a fresh unguarded copy), the
password grant through `integration-test-persona` for `operator` and `wall-munich` (as `research.md` probes did), a
client planted with `RealmProbe.PlantAsync` for the device token, and `admin-permissions`' secret obtained by having
identity-admin `POST …/client-secret` (probe 5) — restored afterwards. A `[Theory]` over the principal list asserts
AS-21 (four requests, each 403, client unchanged per a master-admin read) and AS-23's listing/lookup clean; facts for
AS-24 (positive control) and AS-25 (probe 4's nine requests; then H-1 and H-2, each followed by the matrix re-run as the
target and as `scenario-simulator`). Every mutation snapshots and restores in `finally` (master admin), including
re-creating the target and re-binding its policy after H-2 so later tests in the fixture see the committed realm.

### 13.4 Credential selection and the mint verdict (FR-011, FR-004 rev. 3) — backend

- `KeycloakAdminTokenProvider.GetAccessTokenAsync`: both set → throw (kept); **neither set → throw**, same exception
  type, message names both keys; nothing posted. Then `try { return await …; } catch (HttpRequestException exception)
  { throw new KeycloakAdminTokenRefusedException(…, exception, exception.StatusCode); }` — a wrap, never a swallow; the
  method becomes `async`. The type lives in `src/Identity/Infrastructure/KeycloakAdmin/` (MigrationRunner already
  references Identity.Infrastructure through `AddKeycloakAdminClient`).
- The mint runs inside the admin client's resilience pipeline (`KeycloakAdminAuthorizationHandler`); the standard
  handler treats any `HttpRequestException` as transient and rethrows the last one unwrapped. The engineer confirms
  by test that the typed exception reaches `HttpKeycloakAdminClient`'s caller as itself (a stub factory whose token
  client answers 401).
- `RealmDefaultPrivilegeMigrator`: verdict (b) catches `KeycloakAdminTokenRefusedException` only; the existing
  `BadRequest or Unauthorized` filter on plain `HttpRequestException` is removed, so an admin-API 400/401 propagates.
  Verdict (a) (403) unchanged — a mint answers 400/401, not 403, so the order of the filters is not load-bearing; put
  the typed catch first anyway.

### 13.5 Shared mint shape (FR-022) — behaviour-preserving, ServiceDefaults

One field `Func<TokenRequest> request` (a private record `TokenRequest(string Url, string ClientIdentifier,
IReadOnlyDictionary<string, string> Form)`) built in each public constructor from its credential delegate; `MintAsync`
calls it per mint (the per-call read of credentials is a documented property — keep it). The signed builder imports the
key from a byte array it zeroes in `finally` (`CryptographicOperations.ZeroMemory`). Characterisation: the three named
test classes captured green **before** the change and passing **unmodified** after; an edited assertion blocks. Coverage
gate (ServiceDefaults ≥ 90 %) re-measured.

### 13.6 History (finding 4)

Squash `356a65f8` (realm: client-jwt + FGAP) and `72e792bb` (AppHost: private-key parameter) into one
`feat(1995): …` commit — non-interactively (`GIT_SEQUENCE_EDITOR` rewriting the second `pick` to `fixup`, message
re-worded with `git commit --amend` at that stop), then verify **each** commit on the branch builds and the squashed one
boots the fixture (T102). Done before any revision-3 commit lands, so later SHAs are not rewritten twice.

### 13.7 Engineers and ownership (disjoint files)

| Agent | Files |
|---|---|
| test-writer (4a) | `tests/Architecture.Tests/{IdentityAdminClientAuthorityTests,RealmGrantGraph,MigrationRunnerClientTests}.cs`, `tests/ServiceDefaults.Tests/Authentication/*`, `tests/Identity.Infrastructure.Tests/KeycloakAdmin/{KeycloakAdminCredentialSelectionTests,RealmDefaultPrivilegeRemovalTests}.cs`, `tests/MigrationRunner.Tests/RealmDefaultPrivilegeMigratorTests.cs`, `tests/Integration.Tests/Identity/MigrationRunnerConfinementIntegrationTests*.cs` |
| backend-engineer (4b) | `src/ServiceDefaults/Authentication/ClientCredentialsTokenProvider.cs`, `src/Identity/Infrastructure/KeycloakAdmin/*`, `src/MigrationRunner/RealmDefaultPrivilegeMigrator.cs` |
| infra-engineer (4b) | `src/AppHost/Realms/smart-sentinel-eye-realm.json`, throwaway-Keycloak measurements (T150, T151), the squash (T140), `research.md` |

`RealmGrantGraph.cs` is test infrastructure: test-writer owns it. The S5542 suppression on the branch is
**owner-accepted** — kept as is, not "fixed", not widened. `security-reviewer` at phase 6 stays mandatory.
