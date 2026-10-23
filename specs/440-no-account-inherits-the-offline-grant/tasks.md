# Tasks 440 — No account inherits the offline grant

**Spec:** [spec.md](spec.md) (revision 2) · **Plan:** [plan.md](plan.md) · **Issue:** #1995 (feature-level, on Project #13)
**ADRs:** ADR-0181, **ADR-0186** (accepted on PR #3106, unmerged — the PR must not merge before it)
**Colour:** **red** overall (ADR-0139), split per slice in spec §8a — characterisation (AS-15, the secret path of
T-E), guards proven by counterfactual (T-C holder guard), comment-only (prose). The PR says which test is which.
**Engineers:** `test-writer` (4a, all test files) → `backend-engineer` ∥ `infra-engineer` (4b, disjoint files).
**Security:** `security-reviewer` **mandatory** at phase 6 (ADR-0181 §6, ADR-0186).
**New ADR needed:** **no** (plan §2.1, §2.3 record two placement/wording points for the owner).

Format: `[ID] [P?] [Story] description — files`

## A. Revision 1 — what is built, and what happens to it

| Task (rev 1) | Commit | Status in revision 2 |
|---|---|---|
| T001 stubs + 9 doubles | `8f2c4d32` | **stays** |
| T002 T-A adapter tests | `8f2c4d32` | **changes** → T061 (request sequence + `defaultRole`) |
| T003 T-B migrator tests | `8f2c4d32` | **changes** → T062 (verdicts) |
| T004 T-C realm-file facts | `8f2c4d32` | **changes** → T063 (client-jwt facts; holder guard widened) |
| T005 T-D integration | `8f2c4d32` | **stays**, except one fact re-pointed → T068 |
| T006 local red run | done for rev 1 | superseded by T070 |
| T010 adapter | `68c9a641` | **changes** → T082 (`defaultRole.name`) |
| T011 realm file `manage-realm` + description | `68c9a641` | grant **stays**; client + description **change** → T090 |
| T012 migrator + FR-004 verdict | `68c9a641` | migrator **stays**; verdict **changes** → T083 |
| T013 prose (four sites) | `96f5b3ad` | **stays**; more sites → T095 |
| T014 counterfactuals | partly (local) | superseded by T100 (rev 1's table + rev 2's) |
| T015 format/analyzers | done for rev 1 | superseded by T101 |
| T020, T030-T032, T040-T041 | not started | superseded by T110+, T041 reworded (FR-008) |

**The old secret-based pieces that go:** the realm file's `"secret": "dev-only-migration-runner-secret"` on
migration-runner; AppHost's `MigrationRunnerClientSecret` parameter and `Keycloak__AdminClientSecret` on `migrations`;
`KioskInheritedPrivilegeIntegrationTests:184`'s secret-built adapter; the `MigrationRunnerClientSecret` rows in
`RealmImportMirrorTests.ProductionDefaults`, `AppHostParameterOverrideTests`, `AppHostStackStatusTests`; five composition
snapshots. Nothing in revision 1's *narrowing* goes.

## B. Revision 2

### Phase 4 — foundational (blocks everything below)

- [ ] T049 Rebase `fix/1995-narrow-default-roles` onto `origin/develop` **after PR #3106 (ADR-0186) merges**; re-check
      that no other open PR edits `smart-sentinel-eye-realm.json` (contention file) and that `specs/440-*` is still
      unclaimed on `develop` and open branches. Each of the 4 existing commits must still build on its own.
- [ ] T050 [US3] **FGAP v2 export** (infra-engineer): throwaway `quay.io/keycloak/keycloak:26.6.4`, `start-dev
      --import-realm` of the current realm file, no volume, no Aspire stack running; apply Decision 5 by admin API;
      `kc.sh export` (or partial-export) the realm; record the exact `adminPermissionsEnabled` and `admin-permissions`
      `authorizationSettings` fragments (policy, all-clients permission, per-client NEGATIVE permission, scope names,
      how the resource names the pinned id) in `specs/440-no-account-inherits-the-offline-grant/research.md`;
      `docker rm -f` the container. Blocks T063/T064 (guard needs the shape) and T090.
- [ ] T051 [US3] **Dev key pair** (infra-engineer): RSA 2048; record in `research.md` the generation command, the
      public JWK (`n`, `e`, `kid` = RFC 7638 thumbprint) and the JWKS string. The dev private key (base64 PKCS#8) is
      handed to T091 and the tests via the AppHost parameter default only — **not** written into `research.md`, so
      the repo holds one copy that T-G can check, not an unguarded second one. Blocks T066/T068 and T090/T091.

### Phase 4a — tests first (test-writer), observed, output verbatim

Stubs first so red is an assertion, not a compile error:

- [ ] T060 [US3] Stubs: `SignedJwtClientCredentials` record, `ClientAssertion.Create`, the new
      `ClientCredentialsTokenProvider` constructor (all `throw new NotImplementedException()`),
      `KeycloakAdminOptions.AdminClientPrivateKey`, `KeycloakAdminTokenProvider.AssertionHttpClientName` const —
      `src/ServiceDefaults/Authentication/{SignedJwtClientCredentials,ClientAssertion,ClientCredentialsTokenProvider}.cs`,
      `src/Identity/Infrastructure/KeycloakAdmin/{KeycloakAdminOptions,KeycloakAdminTokenProvider}.cs` (depends T049; blocks T061-T069)

Then, disjoint test files, in parallel:

- [ ] T061 [P] [US1] T-A revised: three-request sequence, `custom-default` target (**red**), missing `defaultRole`
      throws with no DELETE — `tests/Identity.Infrastructure.Tests/KeycloakAdmin/RealmDefaultPrivilegeRemovalTests.cs`
- [ ] T062 [P] [US1] T-B revised: 403 verdict non-destructive-first, volume drop qualified dev-only (**red**); 400/401
      → key-pair verdict (**red**); 5xx unchanged — `tests/MigrationRunner.Tests/RealmDefaultPrivilegeMigratorTests.cs`
- [ ] T063 [P] [US3] T-C revised: client-jwt / no secret / pinned id / one RS256 JWK with kid / description (**red**);
      `manage-realm` holder guard over users + groups (recursive) + composites + `realm-admin` (**guard** — green,
      counterfactual in T100) — `tests/Architecture.Tests/MigrationRunnerClientTests.cs` (depends T050)
- [ ] T064 [P] [US3] T-F Decision 6 guard, AS-16 (**red**) — `tests/Architecture.Tests/IdentityAdminClientAuthorityTests.cs`
      (depends T050)
- [ ] T065 [P] [US3] T-E assertion + form + distinct `jti` + signature verifies (**red**) —
      `tests/ServiceDefaults.Tests/Authentication/ClientAssertionTests.cs`, `tests/ServiceDefaults.Tests/ClientCredentialsTokenProviderTests.cs`
- [ ] T066 [P] [US3] T-E selection: key → assertion on its own client (**red**); secret → today's form
      (**characterisation**, green before and after); both → throws (**red**); assertion client makes exactly one
      attempt on 503 (**red**) — `tests/Identity.Infrastructure.Tests/KeycloakAdmin/KeycloakAdminCredentialSelectionTests.cs` (depends T051)
- [ ] T067 [P] [US3] T-G key-pair mirror AS-17 (**red**); remove the `MigrationRunnerClientSecret` row; re-point
      `AppHostParameterOverrideTests` (`DeclaredDefaults`, example parameter) and `AppHostStackStatusTests:124` to
      `MigrationRunnerClientPrivateKey` (**red** until T091: `.Single` finds no such parameter); `AppHostMigrationGateTests:27`
      doc — `tests/Integration.Tests/Identity/RealmImportMirrorTests.ProductionDefaults.cs`,
      `tests/Integration.Tests/AppHostParameterOverrideTests.cs`, `tests/Integration.Tests/AppHostStackStatusTests.cs`,
      `tests/Integration.Tests/AppHostMigrationGateTests.cs` (depends T051)
- [ ] T068 [P] [US1/US3] T-D: `Narrowing_again_as_migration_runner…` over `AdminClientPrivateKey` (**red**, CI);
      key obtained per plan §6 (parameter, or a guarded test copy) —
      `tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs` (depends T051)
- [ ] T069 [P] [US3] T-H confinement: AS-10, AS-11, AS-13 (both breaks, in-test attacker key, state restored in
      `finally` via master-admin snapshot), AS-14 (**red**, CI-only); add the class to one
      `ci-shards/shard-N.filter` — `tests/Integration.Tests/Identity/MigrationRunnerConfinementIntegrationTests.cs`,
      `tests/Integration.Tests/ci-shards/shard-N.filter`
- [ ] T070 [US1/US3] Run T061-T067 locally (Docker-free + `FixtureLogic`), quote verbatim; run T068/T069 + the
      Identity integration subset locally if a stack slot is free, else on the PR's first CI run — **download the job
      log before any re-run**. Record the **AS-15 baseline**: the Identity integration subset green on the realm as it
      stands (depends T060-T069)

### Phase 4b — implementation (may not edit T061-T069)

Two engineers on disjoint files, in parallel after T070.

**backend-engineer** — `src/ServiceDefaults/Authentication/*`, `src/Identity/Infrastructure/*`, `src/MigrationRunner/*`

- [ ] T080 [P] [US3] `ClientAssertion` + `SignedJwtClientCredentials` + provider constructor; form per plan §2.2; no
      package change unless the build demands it (then say so) — `src/ServiceDefaults/Authentication/` (depends T065)
- [ ] T081 [US3] `KeycloakAdminTokenProvider` selection (FR-011); assertion named client registered **without**
      `RetryEveryMethod()` with the one-line why (FR-010); `IdentityInfrastructureModule.cs:260-262` comment —
      `KeycloakAdminTokenProvider.cs`, `IdentityInfrastructureModule.cs` (depends T066, T080)
- [ ] T082 [P] [US1] Adapter reads `defaultRole.name` (FR-001) — `HttpKeycloakAdminClient.Realm.cs` (depends T061)
- [ ] T083 [P] [US1] Migrator verdicts FR-004(a)/(b) — `RealmDefaultPrivilegeMigrator.cs` (depends T062)

**infra-engineer** — `src/AppHost/*`, `README.md`, snapshots

- [ ] T090 [P] [US3] Realm file: migration-runner pinned `id`, `client-jwt`, no `secret`, JWKS attributes,
      description ≤ 255; identity-admin loses `manage-clients`/`view-clients`; `adminPermissionsEnabled`;
      `admin-permissions` `authorizationSettings` from `research.md`; BOM kept — `src/AppHost/Realms/smart-sentinel-eye-realm.json`
      (depends T050, T051, T063, T064)
- [ ] T091 [P] [US3] AppHost: `MigrationRunnerClientPrivateKey` parameter (dev default from T051),
      `Keycloak__AdminClientPrivateKey`, drop `Keycloak__AdminClientSecret` on `migrations`; `AppHost.cs:30-34` comment;
      `OverridableParameters.cs:32-35` count — `src/AppHost/AppHost.cs`, `src/AppHost/OverridableParameters.cs` (depends T051, T067)
- [ ] T092 [US3] Regenerate the five composition snapshots through their own update path; diff limited to the
      parameter/env lines — `tests/Integration.Tests/AppHostComposition/*.snapshot.txt` (depends T091)
- [ ] T093 [P] [US1] README line (FR-018) — `README.md`

**either (comment-only; whoever finishes first; disjoint from both lists)**

- [ ] T095 [P] [US1] FR-017: `KioskInheritedPrivilegeIntegrationTests` class comment + `A_kiosk_enrolled_at_runtime…`
      message + strip-coverage note naming `KeycloakRequestSequenceCharacterisationTests`. Characterise: asserted
      expressions byte-identical; strip comments + hash before/after. **Sequenced after T068** (same file).

### Phase 4c — close-out

- [ ] T100 [US1/US3] Counterfactuals (plan §10 + revision 1's §6): plant, quote red, revert. Local for T-C/T-E/T-F/T-G;
      CI-only ones (T-H, fixture boot) noted with the run link (depends T080-T095)
- [ ] T101 Format/analyzers: `dotnet format --verify-no-changes`, Release build clean, coverage gate for
      ServiceDefaults ≥ 90 % (depends T080-T095)
- [ ] T102 Per-commit build check across the whole branch (rebase-merge lands commits individually) (depends T101)

### Phase 5 — verify

- [ ] T110 [US1/US2/US3] CI green incl. Docker integration (T-D, T-H, **AS-15 Identity subset unmodified, quoted
      against the T070 baseline**) and full-stack e2e (`wall-outlives-its-session` unmodified); manual procedure spec §6.4
      on a dropped `keycloak-data` volume; **name the realm verified against** —
      `specs/440-no-account-inherits-the-offline-grant/verification.md`

### Phase 6 — review

- [ ] T120 infra-reviewer (realm file incl. FGAP block, AppHost, snapshots, fixture boot proves the pinned id)
- [ ] T121 **security-reviewer (mandatory)**: no admin-readable credential authenticates migration-runner; both
      reconfigure breaks 403; `manage-realm` held only by migration-runner by any route; identity-admin holds neither
      `manage-clients` nor `view-clients` by any route; the deny is `NEGATIVE` and scoped to the pinned id only; assertion
      claims, lifetime, single use, no HTTP retry; private key never logged and reaches only `migrations`; no
      production-reachable default; ADR-0186 note 6's ADR-0160/enrolment question answered as a finding
- [ ] T122 backend-reviewer (ServiceDefaults mint, selection, adapter, verdicts: `Ensure.That`, `var`, no `_` fields,
      CT last, `Option`/`Result` where applicable, one parameter per line when wrapping)

### Phase 7 — PR

- [ ] T130 PR to `develop` (`--base develop`), after #3106 merged; body: red outputs, characterisation split
      (AS-15 baseline vs after; T-E secret path), counterfactuals, FR-007 inversion argument, plan §2.1/§2.3 notes,
      spec §7 consequences (dev-stack mint failure, Keycloak-bump re-run, `manage-clients` review rule, system clients),
      `Closes #1995`
- [ ] T131 Comment on #2878 (FR-008, revised: two coupled key items, "realm-takeover credential"); after merge,
      comment on #91 about the volume drop

## Dependencies

```
T049 → T050 ∥ T051 → T060 → T061..T069 [P] → T070
T070 → backend: T080 → T081 ; T082 ∥ T083            (all [P] w.r.t. infra)
     → infra:   T090 ∥ T091 → T092 ; T093
     → T095 (after T068)
→ T100 → T101 → T102 → T110 → T120 ∥ T121 ∥ T122 → T130 → T131
```

**Foundational blockers:** T049 (ADR on `develop`, rebase), T050 (FGAP shape), T051 (key pair) — nothing fans out
before them. **Contention:** the realm file and `AppHost.cs` — no parallel PR may edit either while this one is open;
within the branch, only infra-engineer touches them.
