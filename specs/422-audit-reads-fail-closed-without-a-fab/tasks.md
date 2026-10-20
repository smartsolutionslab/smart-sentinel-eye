# Tasks 422: Audit reads fail closed for a caller without a fab, and a by-id refusal is a 404

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3035 items 1-3 (feature-level,
already on Project #13 and in the autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, `fix/3035-unscoped-audit-reads`, `--base develop`. Phase 4a colour: red** for all three items
(plan §5), with the characterisation tests of plan §5.2 observed green before and passing unmodified after.
Security-sensitive: `security-reviewer` + `/security-review` are required.

**Roles.** Tests: `test-writer` (T010, T020, T030). Implementation: `backend-engineer` (T011, T021, T031).
Review: `backend-reviewer` + `security-reviewer` (T040). The engineer receives the verbatim red output as
its brief and may not edit the tests.

**Where tests run.** In-process (local, no Docker): `tests/AuditObservability.Application.Tests`,
`tests/Architecture.Tests`. CI-only (Aspire): `tests/Integration.Tests` (shards 2 and 4). Docker is down on
the authoring machine.

Format: `[ID] [P?] [Story] description, files`. No `[P]` markers: US1 and US2 edit the same production and
test files, and US3, though file-disjoint, shares the one branch and index (ADR-0109; one branch, one
index). Nothing foundational (no Shared.Kernel/Contracts/AppHost task).

## Phase 0: Setup

- [ ] **T001** On the branch tip, re-verify the premise lines of spec §Problem
  (`AuditReadScope.cs:52-62`, `GetAuditEventQueryHandler.cs:23`, `AuditEndpoints.cs:219-222`,
  `AuditEventConfiguration.cs:155-180`) and that `AuditReadScope` still has exactly three callers. Re-check
  that 422 is free on `origin/develop`, every remote branch and open PRs. Run the plan §5.2 characterisation
  set and quote its green summary. No file change.

## Phase 1: US1 (P1) — no usable fab claim sees only fab-neutral rows (item 1)

- [ ] **T010 [US1]** (test-writer, red) Retarget R1-R9 and add N1 (red) and N3 (characterisation) per plan
  §5.1/§5.3; correct the class/section comments that state the old rule. Run both in-process projects;
  return the **verbatim** failures (expected red: R1-R4, R6-R9, N1; green: R5, N3, all of §5.2).
  Commit `test(3035): an audit read with no usable fab claim sees only fab-neutral rows (red)`. Files:
  `tests/AuditObservability.Application.Tests/Queries/Handlers/{GetAuditEventScopeTests,AuditReadScopeAdversarialTests,SearchAuditQueryHandlerTests,SearchAuditRefusalAndScopeCharacterisationTests}.cs`,
  `tests/Architecture.Tests/AuditReadEndpointAdversarialTests.cs`,
  `tests/Architecture.Tests/Persistence/AuditReadScopeTranslationAdversarialTests.cs`.
- [ ] **T011 [US1]** (backend-engineer) `AuditReadScope.Apply` unscoped branch → `Fab == null &&
  FabAttribution == NotApplicable`; `Hides` drops `IsScoped &&` (plan §2, item 1). Correct the doc comments
  on `AuditReadScope`, `GetAuditEventQuery`, `SearchAuditQuery`. Both in-process projects green; Release
  build clean. Capture the green unscoped `WHERE` for R6's `ShouldStartWith` if T010 left it as
  `ShouldContain` — that edit is the test-writer's, not the engineer's. Commit
  `fix(3035): an audit read with no usable fab claim sees only fab-neutral rows`. Files:
  `src/AuditObservability/Application/Queries/Handlers/AuditReadScope.cs`,
  `src/AuditObservability/Application/Queries/{GetAuditEventQuery,SearchAuditQuery}.cs`. Depends on T010.

## Phase 2: US2 (P1) — by-id answers 404 for every row the caller may not read (item 2)

- [ ] **T020 [US2]** (test-writer, red) Retarget R10-R15 and add N2 (red), N4, N5 (red), N6 per plan
  §5.1/§5.3. R12 is arrangement-only. R13/R14 are CI-only: build them, they cannot be run locally. Return the
  verbatim in-process failures (expected red: R10, R11, R15, N2, N5). Commit
  `test(3035): a by-id audit read answers 404 for every row the caller may not read (red)`. Files: the
  Application and Architecture test files above, plus
  `tests/AuditObservability.Application.Tests/Queries/Handlers/GetAuditEventQueryHandlerTests.cs`,
  `tests/Architecture.Tests/AuditObservabilityRouteTableCharacterisationTests.cs`,
  `tests/Integration.Tests/AuditObservability/{CrossFabReadGuardIntegrationTests,UnresolvedFabAuditRowIntegrationTests}.cs`.
  Depends on T011.
- [ ] **T021 [US2]** (backend-engineer) Replace `Hides` with `Admits` (R2 in full, `Ensure.That(row)`); the
  handler answers `AuditEventNotFound` when `row is null || !scope.Admits(row)`; `AuditEndpoints.GetSingle`
  drops the `IFabAuthorizationGuard` parameter and the post-read guard block, keeps `.ProducesProblem(403)`
  (plan §2, item 2). In-process green; `dotnet build -c Release` of `tests/Integration.Tests` clean. Commit
  `fix(3035): a by-id audit read answers 404 for every row the caller may not read`. Files:
  `src/AuditObservability/Application/Queries/Handlers/{AuditReadScope,GetAuditEventQueryHandler}.cs`,
  `src/AuditObservability/Api/AuditEndpoints.cs`. Depends on T020.

## Phase 3: US3 (P2) — the fab-neutral partial index (item 3)

- [ ] **T030 [US3]** (test-writer, red) R16 (model characterisation line) and N7 (`pg_indexes` fact in
  `NeutralFabRetentionRowIntegrationTests`, CI-only). Return R16's verbatim failure. Commit
  `test(3035): the audit model carries the not-applicable partial index (red)`. Files:
  `tests/Architecture.Tests/Persistence/AuditObservabilityModelCharacterisationTests.cs`,
  `tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs`. Depends on T021
  (ordering only; files disjoint).
- [ ] **T031 [US3]** (backend-engineer) `HasIndex(OccurredAt).HasDatabaseName("ix_audit_not_applicable_occurred")
  .HasFilter("fab_attribution = 'NotApplicable'")`; `dotnet ef migrations add AuditNotApplicableOccurredIndex`;
  strip any churn so `Up` is one `CreateIndex` and `Down` one `DropIndex` (no `CONCURRENTLY`, no `CHECK`);
  migration comment per plan §3. Configuration, migration and snapshot in **one** commit. R16 and
  `The_committed_model_snapshot_still_describes_the_model` green. Quote `git diff --stat` on
  `Migrations/` (SC-4). If Docker is reachable, boot once against an existing dev volume with compressed
  chunks and report the MigrationRunner outcome (spec A1); a failure there **blocks**. Commit
  `perf(3035): index the fab-neutral audit rows by occurred_at`. Files:
  `src/AuditObservability/Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs`,
  `src/AuditObservability/Infrastructure/Persistence/Migrations/<ts>_AuditNotApplicableOccurredIndex{,.Designer}.cs`,
  `src/AuditObservability/Infrastructure/Persistence/Migrations/AuditObservabilityDbContextModelSnapshot.cs`.
  Depends on T030.

## Phase 4: Verify, review, PR

- [ ] **T040** `backend-reviewer` and `security-reviewer` on the full diff, `/security-review`. Security
  checks: no read path still admits `Unresolved` or a foreign `Resolved` row for any claim spelling; the
  by-id 404 is byte-identical for unknown, `Unresolved`, foreign-`Resolved` and unscoped-`Resolved`; no fab
  id in any by-id body; spec A3 (the removed guard was dead); named-fab 403s untouched. Backend: one index
  and nothing else in the migration; snapshot matches. Depends on T031.
- [ ] **T041** Phase 5 (`/verify`): in-process evidence quoted; if Docker is available, the spec §E2E step 3.
  Otherwise record that CI is the only end-to-end observation. Depends on T031.
- [ ] **T050** PR to `develop` (`--base develop`): body quotes T010/T020/T030 red output and the T001 green
  characterisation, lists R1-R16 with before/after, states **"unmeasured: no live EXPLAIN; CI integration
  is the only evidence"** and spec A1. `Closes #3035` (items 1-4 then all delivered). After CI: download the
  integration trx and quote `Passed` for R13, R14, N7 (SC-5); not satisfied by a skipped or cancelled
  bucket. Then `docs(3035): tick the completed tasks of spec 422`. Depends on T040, T041.

## Dependencies

T001 -> T010 -> T011 -> T020 -> T021 -> T030 -> T031 -> {T040, T041} -> T050.

> **Note (2026-10-20, follow-up to the merged work):** the by-id scope no longer lives in an `Admits` member. `GetAuditEventQueryHandler` applies it in the query via `AuditReadScope.Apply`, so a hidden row costs what an unknown id costs, and `Admits` was removed. The `Admits` references above (R2, T021, commit 5) describe the original plan and are kept as history.
