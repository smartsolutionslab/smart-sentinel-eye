# Tasks 333 — The lane the flag was never named for

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2297 · **Phase**: 3 (Tasks)

**Status**: **Approved — route B** (spec §4 Q1 = B, decided by the user 2026-10-10). Phase 1's gate
is passed; T001-T005 are implemented.

**Colour**: **behaviour-changing → red** for the e2e lane (T002 facts 1-3 and 8); **characterisation,
observed green** for the developer lane, the fixture lane and the e2e lane's retained resources
(T002 facts 4-7). Ambiguity resolves to red (ADR-0144).
**Engineer**: `test-writer` (4a) then `infra-engineer` (4b). **Reviewer**: `infra-reviewer`.
**Tracking**: feature-level issue #2297. No per-task issues.
**New ADR**: none for route B (plan §1).

Format: `[ID] [P?] [Story] Description`. No foundational work (no Shared.Kernel / Contracts). Every
file here is an AppHost / CI contention file, so nothing is `[P]` beyond T001 alongside T002's
reading; one engineer, one worktree (memory *one branch, one index*).

## Phase 4a — tests first (`test-writer`; return verbatim output)

- [x] **T001 [US1]** Read `tests/Integration.Tests/AppHostE2ESwitchTests.cs`,
  `AppHostReplicaCountTests.cs` and `AppHostGatewayRateBudgetTests.cs` for the argument-array
  convention, the env-annotation reader and the `ci.yml` boot-line reader (plan §2.3). No output.
- [x] **T002 [US1]** Create `tests/Integration.Tests/AppHostStackPersistenceTests.cs` with the eight
  facts in plan §2.3, and add the class to `tests/Integration.Tests/ci-shards/shard-4.filter`.
  Run `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AppHostStackPersistenceTests"`
  against unmodified `AppHost.cs`/`ci.yml`. **Expected: exactly facts 1, 2, 3, 8 fail**, each message
  naming its offenders (1: postgres, rabbitmq, keycloak, mosquitto, storage, mediamtx; 2: five
  volumes; 3: pgadmin; 8: missing `PersistentStack=false`); facts 4-7 pass. A fact arriving green
  that should be red, or vice versa, is a stop-and-report. Stop any running AppHost first (memory
  *stop the stack before building*). Commit 1 (plan §2.4). Depends on T001.

## Phase 4b — implement (`infra-engineer`; receives T002's output as its brief; may not edit T002's tests)

- [x] **T003 [US1]** `src/AppHost/AppHost.cs` per plan §2.1 steps 1-5: the switch, re-gating the five
  persistence blocks and pgAdmin, splitting mediamtx's block, camera-sim's lifetime, the three-lane
  header comment. Leave every item in plan §2.1 step 6 untouched. Depends on T002.
- [x] **T004 [US1]** `.github/workflows/ci.yml:793` — add `PersistentStack=false` per plan §2.2.
  Depends on T002 (fact 8). Commit 2 together with T003.
- [x] **T005 [US1]** Re-run T002's filter: all eight facts green, **test file unmodified since
  commit 1** (`git diff <commit1> -- tests/` empty). Run the existing `AppHostE2ESwitchTests`,
  `AppHostReplicaCountTests`, `AppHostGatewayRateBudgetTests`, `AppHostWebAppHostingTests`,
  `AppHostMediaMtxImageTests`, `AppHostContainerImagePinTests` — green, unmodified.
  `dotnet build -c Release` clean (analyzers). Depends on T003, T004.

## Phase 5 — observe (`/verify`)

- [ ] **T006 [US1]** Local boot per plan §3.1 with a before/after `docker ps -a` + `docker volume ls`
  listing; quote both in the verification note. Do not remove the developer's existing persistent
  containers or volumes. Depends on T005.
- [ ] **T007 [US1]** After the PR is open: read all four `e2e-shards` jobs of the PR's CI run per plan
  §3.2 (readiness gate passed, suite verdict vs `develop`, stack-status report has no `pgadmin`).
  Download job logs before any re-run (memory *a re-run erases the failure from CI history*).
  Depends on PR creation.

## Dependencies

T001 → T002 → {T003, T004} → T005 → T006 → (PR) → T007.

## Follow-up (not tasks here — for the orchestrator)

- If spec §4 Q2 chooses the follow-up: file an issue to test dropping the 8189 port map on a Linux
  runner (spec §2 B6's unverified guess).
- `tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs:150,277` says
  Postgres runs `ContainerLifetime.Persistent` in the integration lane; under `E2ETests=true` it does
  not. Stale comment, out of scope here; worth a one-line issue.
