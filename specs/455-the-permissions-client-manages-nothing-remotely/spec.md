# Spec 455: The permissions client manages nothing remotely, and its credential is probed, not presumed

**Issue:** #3121 (feature-level, on Project #13) · **Branch:** `fix/3121-admin-permissions-client`
**ADRs:** ADR-0186 (Decision 5: the realm declares `admin-permissions`' `authorizationSettings`; accepted residual: the
all-clients grant rewrites system clients), ADR-0187 (Decision 3: every confinement probe covers "any credential the
mechanism itself creates (here `admin-permissions`' service account)"; residual "kept for now ... ticketed #3121"),
ADR-0188 (the never-matching time policy; residuals unchanged), ADR-0103 (Aspire fixture), ADR-0139 / ADR-0144
(red first, autonomous lane).
**Lane:** autonomous · **Phase-4a colour:** **red** for FR-1 and FR-2; FR-3 is a coverage repair expected green
(see Testing) · **Engineer:** infra-engineer.
**Security-sensitive:** **yes** — Keycloak realm, FGAP. `infra-reviewer` and `security-reviewer` mandatory at phase 6.
**New ADR needed:** **no** for what is built here (a hardening inside ADR-0186 Decision 5's block, and ADR-0187
Decision 3 applied as written). The residual's wording in ADR-0187/0188 ("removal or disabling ticketed as #3121")
becomes stale: the lane cannot amend an ADR — owner question Q1.
**Written 2026-10-25 against `develop` at `fc48ed75`.** Evidence: [research.md](research.md).

---

## Premise, checked

1. **Holds, narrower than the issue says.** The credential is not stored by the import (CI fact: master reads
   `{"type":"secret"}` without a value) but identity-admin can rotate one and mint with it (R1). It is real, latent,
   and absent from the confinement matrix — the CI fact's conclusion "no token can be minted" is false.
2. **The issue's remedy cannot be built in the realm file.** Keycloak 26.6.4 silently replaces
   `serviceAccountsEnabled:false`, `standardFlowEnabled:false` and `bearerOnly:true` on this system client with its own
   values and discards its client scopes (R2). Writing them would make the file claim what the realm does not hold.
3. **One hardening is honoured.** `authorizationSettings.allowRemoteResourceManagement:false` survives the import and
   closes the protection API, which today lets the rotated credential read the whole FGAP resource set (R4).
4. **The two realm tests** (`RealmIdentityTests.Every_client_holds_the_identity_scope`,
   `RealmAudienceTests.Every_client_holds_the_audience_scope`) are satisfied by declarations Keycloak discards (R2).
   Changing either is a guard narrowing; left untouched here (owner question Q2).
5. **Spec number 455**: 450 (#3118), 451, 453 (#3133), 454 (#3096) taken on `origin/develop`, open PRs, branches and
   worktrees; 452 unclaimed by any branch found but held back on the orchestrator's instruction. Re-check before push.

## User story (P1, the only one)

**As** a security officer, **I want** the credential Keycloak attaches to `admin-permissions` probed exactly like every
other principal and unable to read the permission model, **so that** the migration-runner confinement rests on a
measured matrix, not on the absence of a secret anyone holding identity-admin can create.

**Independent test:** boot the Aspire stack; as identity-admin rotate `admin-permissions`' secret and mint a
`client_credentials` token; observe 403 on migration-runner's client and secret (GET/PUT/GET secret/POST secret) and
400 `not_supported` on `/authz/protection/resource_set`.

## Acceptance scenarios

```gherkin
Scenario: The realm file disables remote resource management (FR-1)
  Given the committed realm file
  Then the admin-permissions client's authorizationSettings.allowRemoteResourceManagement is false

Scenario: The rotated credential cannot read the permission model (FR-2, bad request)
  Given identity-admin has rotated admin-permissions' secret and minted a client_credentials token with it
  When that token calls GET /realms/smart-sentinel-eye/authz/protection/resource_set
  Then the answer is 400 with error "not_supported"

Scenario: The rotated credential is refused migration-runner (FR-3, auth)
  Given the same token
  When it GETs, PUTs, GETs the secret of and POSTs a secret to the migration-runner client
  Then each answer is 403

Scenario: Hardening does not break the deny or enrolment (regression)
  Given the realm imported with FR-1
  Then identity-admin is refused migration-runner and does not find it by list or search
  And identity-admin can still create a confidential client and read its secret
```

(No conflict scenario: nothing here is versioned or concurrent.)

## Functional requirements

- **FR-1** `src/AppHost/Realms/smart-sentinel-eye-realm.json`: `admin-permissions` →
  `authorizationSettings.allowRemoteResourceManagement: false`. No other field of that entry changes (R2: they are
  ignored or forced; editing them would be a false declaration).
- **FR-2** A credential minted for `admin-permissions` is refused the protection API.
- **FR-3** `admin-permissions` joins `Every_principal_is_refused_the_migration_runner_client_and_its_secret`, minted by
  the route an attacker would use (identity-admin rotates, then `client_credentials`). The fact
  `The_admin_permissions_client_has_no_secret_so_no_token_can_be_minted_for_it` and its comment are removed: its
  premise is false (R1), and after FR-3 rotates a secret it would also fail on order. This replaces a presumption with a
  measurement — the stronger check, not a weakened gate.
- **FR-4** No change to `serviceAccountsEnabled`, `standardFlowEnabled`, `bearerOnly`, client scopes, the two scope
  guards, or any runtime switch-off (R3; owner questions Q2, Q3).

## Testing (ADR-0139)

- **Red:** the Architecture.Tests guard for FR-1 (fails on the committed `true`) and its counterfactual; the
  integration fact for FR-2 (committed realm answers 200 — R4 v0).
- **Expected green, by design:** the FR-3 matrix row. ADR-0188's deny already refuses this token (R1); the row repairs
  ADR-0187 Decision 3 coverage. The test-writer reports it green with this sentence quoted; that is not a 4a failure.

## Latency budget

N/A — realm configuration and tests only; no leg of constitution §IV is touched.

## Out of scope

Removing or disabling the service account (not achievable by import, R2; reversible at runtime, R3); exempting
`admin-permissions` from the two scope guards; amending ADR-0187/0188; the identity-admin residuals #3120 and the
system-client rewrite ADR-0186 records.
