# Tasks 440 — No account inherits the offline grant

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #1995 (feature-level, on Project #13)
**Colour:** **red** (behaviour-changing; ADR-0139). T-D 3/4/7 are characterisation and must be observed green
before and after — the PR says which is which.
**Engineer:** `infra-engineer` (ADR-0181 Implementation Notes), one slice: realm edit + port + migrator.
**Security:** `security-reviewer` **mandatory** at phase 6 (ADR-0181 §6).

Format: `[ID] [P?] [Story] description — files`

## Phase 4a — tests first (test-writer), observed red, output verbatim

- [ ] T001 [US1] Stubs so red is an assertion, not a compile error: interface member + `RealmDefaultPrivilegeMigrator`
      skeleton throwing `NotImplementedException`; member added to the nine test doubles — `IKeycloakAdminClient.cs`,
      `HttpKeycloakAdminClient.Realm.cs`, `src/MigrationRunner/RealmDefaultPrivilegeMigrator.cs`, doubles listed in spec §2
      (blocks T002-T005)
- [ ] T002 [P] [US1] T-A adapter tests (plan §5) — `tests/Identity.Infrastructure.Tests/KeycloakAdmin/RealmDefaultPrivilegeRemovalTests.cs`
- [ ] T003 [P] [US1] T-B migrator tests incl. run-twice and failure → `MigrationRun` `Failed` — `tests/MigrationRunner.Tests/RealmDefaultPrivilegeMigratorTests.cs`
- [ ] T004 [P] [US1/US2] T-C realm-file facts — `tests/Architecture.Tests/MigrationRunnerClientTests.cs`
- [ ] T005 [P] [US1/US2] T-D integration: invert `:63`, person-account case, wall Theory ×4, operator exactly `user`,
      anti-decoy, re-run as migration-runner, identity-admin 403 — `tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs`,
      `RealmProbe.cs` (user-delete helper only if needed). **CI-only** (Docker down locally): red is observed on the
      PR's first CI run and the log downloaded before any re-run
- [ ] T006 [US1] Run T002-T004 locally, quote output; push for T005's CI red (depends T001-T005)

## Phase 4b — implementation (infra-engineer; may not edit T002-T005)

- [ ] T010 [P] [US1] Adapter: GET role, DELETE composite, `EnsureSuccessStatusCode`, no early return, call-site *why* — `HttpKeycloakAdminClient.Realm.cs`, `IKeycloakAdminClient.cs` (doc) (depends T002)
- [ ] T011 [P] [US1/US2] Realm file: `manage-realm` on `service-account-migration-runner`; description rewritten ≤ 255; BOM kept; nothing else — `src/AppHost/Realms/smart-sentinel-eye-realm.json` (depends T004)
- [ ] T012 [US1] Migrator body + FR-004 verdict; register `AddScoped` above `AddCameraCatalogPersistence()` — `RealmDefaultPrivilegeMigrator.cs`, `Program.cs` (depends T003, T010)
- [ ] T013 [P] [US1] Prose only (FR-006): `WallDisplayAccountTests.cs:22-38`, `KioskInheritedPrivilegeIntegrationTests.cs:11-20` class comment, `HttpKeycloakAdminClient.cs:85-89`, `IKeycloakAdminClient.cs:133-143`; `auth.ts` only if it claims inheritance. Characterise: strip comments, hash code before/after (memory: comment-only change)
- [ ] T014 [US1] Counterfactuals (plan §6): plant, quote red, revert — local ones now, CI-only ones (fixture) noted with the run link (depends T010-T012)
- [ ] T015 Format/analyzers: `dotnet format --verify-no-changes`, Release build clean (depends T010-T013)

## Phase 5 — verify

- [ ] T020 [US1/US2] CI green incl. Docker integration + full-stack e2e (`wall-outlives-its-session` unmodified); manual
      procedure spec §6.3 if Docker is available; **name the realm verified against** — `specs/440-*/verification.md`

## Phase 6 — review

- [ ] T030 infra-reviewer (realm file, AppHost, fixture boot proves anything)
- [ ] T031 **security-reviewer (mandatory)**: `manage-realm` held only by `migration-runner` (file + running);
      `identity-admin` unchanged (403); secret has no production-reachable default beyond the existing overridable
      parameter; migrator never swallows; nothing logs tokens/secret; only `offline_access` leaves the composite;
      `StripInheritedRealmRolesAsync` + `KioskPrivilegeSweep` intact; inverted test argued; anti-decoy assertion present
- [ ] T032 backend-reviewer (port/adapter/migrator conventions: `Ensure.That`, `var`, no `_` fields, CT last)

## Phase 7 — PR

- [ ] T040 PR to `develop` (`--base develop`), body: red output, characterisation split, inversion argument (FR-007),
      unchanged list (spec §7), persistent-dev-stack consequence + #91, `Closes #1995`
- [ ] T041 Comment on #2878 (FR-008); after merge, comment on #91 about the volume drop

## Dependencies

T001 → T002-T005 [P] → T006 → T010 ∥ T011 ∥ T013 → T012 → T014 → T015 → T020 → T030-T032 → T040-T041.
The realm file is a contention file: no parallel PR may edit it while this one is open.
