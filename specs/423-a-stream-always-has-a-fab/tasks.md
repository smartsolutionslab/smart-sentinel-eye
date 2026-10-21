# Tasks 423: A stream always has a fab

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #1467 (feature-level, on
Project #13, autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, `fix/1467-streams-fab-not-null`, `--base develop`. Phase 4a colour: red** (behaviour-changing:
a migration, a removed aggregate method, a removed hosted service, a retired credential), preceded by a
**characterisation** step for the three integration classes whose arrangement must change (plan §5.1).
**Security-sensitive:** `security-reviewer` + `/security-review` are mandatory at phase 6.

**Roles.** Tests: `test-writer` (T010, T020). Implementation: `backend-engineer` (T030),
`infra-engineer` (T040). Review: `backend-reviewer`, `infra-reviewer`, `security-reviewer` (T050). The
engineers receive the verbatim red output as their brief and may not edit R1-R6.

**Where tests run.** Local, no Docker: `StreamDistribution.{Domain,Application,Infrastructure}.Tests`,
`Architecture.Tests`. CI-only (Aspire): `Integration.Tests`. Docker was down on the authoring machine.

**No `[P]` markers.** Every task commits to the one branch and index (ADR-0109; one branch, one index),
and C/D are ordered for per-commit builds (plan §6). Nothing foundational in Shared.Kernel/Contracts.

Format: `[ID] [P?] [Story] description, files`.

## Phase 0: Setup

- [ ] **T001** Re-verify spec §Problem P1-P4 and P8 on the branch tip; re-run the grep in plan §7 and
  record its hit list. Confirm `423` is still free on `origin/develop`, every remote branch and open PR
  branches. Confirm the newest StreamDistribution migration is still
  `20261018083817_RecordStreamSourceCameraVersion` (else update R4's target). If any live
  `stream-distribution-db` is reachable, run `SELECT count(*), count(*) FILTER (WHERE fab IS NULL) FROM
  streams;` read-only and record the figure in the PR; otherwise record "not measured". No file change.

## Phase 1: Characterisation (US2, US3) — observed green before any production change

- [ ] **T010 [US2][US3]** (test-writer, characterisation) C1, C2: mint the unscoped token from a
  `RealmProbe`-planted password-grant client (user `operator`, scopes `sse-identity, sse-audience,
  sse-groups, sse.cameras.read`), deleted in `finally`; assertions unchanged. C3: re-arrange
  `UnresolvedFabAuditRowIntegrationTests` per plan §5.1 (real Healthy control row; null-fab
  `StreamHealthChangedV1` through the in-process `IntegrationEventAuditHandler`); the nine facts'
  assertions unchanged. Remove `RealmImportMirrorTests.IntegrationTestCopies` rows T13/T14. If C3 cannot
  compose the handler without internals: **block** (spec A2). Build; push; quote the CI green for the
  three classes. Commit `test(1467): the borrowed principal and the manufactured null fab are arranged
  without the attribution account`. Files:
  `tests/Integration.Tests/SystemVariables/{VariableReadScopeIntegrationTests,ResolveOverlayTextTests}.cs`,
  `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs`,
  `tests/Integration.Tests/Identity/RealmImportMirrorTests.IntegrationTestCopies.cs`.

## Phase 2: Red (US1, US2)

- [ ] **T020 [US1][US2]** (test-writer, red) Add R1-R6 per plan §5.2; add shard-filter entries for the
  two new Integration classes. Run the local projects and return the **verbatim** failures (expected red:
  R1, R2, R3, R6). R4/R5 are CI-only: build them, push, and quote their failures from the CI job log of
  this commit (download the log before any re-run). Commit `test(1467): a stream's fab is required and the
  attribution credential is refused (red)`. Files:
  `tests/StreamDistribution.Domain.Tests/Stream/StreamTests.cs`,
  `tests/StreamDistribution.Infrastructure.Tests/Persistence/StreamFabModelTests.cs` (new),
  `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs`,
  `tests/Integration.Tests/StreamDistribution/StreamFabNotNullMigrationIntegrationTests.cs` (new),
  `tests/Integration.Tests/Identity/RetiredAttributionClientIntegrationTests.cs` (new),
  `tests/Integration.Tests/ci-shards/shard-*.filter`. Depends on T010.

## Phase 3: Implementation

- [ ] **T030 [US1]** (backend-engineer) Plan §2-§4 and §7, and the §5.3 rows that are not AppHost/realm:
  `Stream.Fab` non-nullable, `AttributeToFab` removed; domain event, handlers, `Log.RefusedWhepFab`;
  `StreamConfiguration.IsRequired()`; `dotnet ef migrations add StreamFabNotNull` then insert the guard
  first in `Up` (plan §4); delete `Infrastructure/Attribution/` (6 files), `BindStreamFabAttribution`, the
  hosted-service registration and the attribution `[LoggerMessage]`s; delete/edit the §5.3 tests
  (including `shard-1.filter`'s `StreamFabAttributionIntegrationTests.` entry and mirror row T16);
  `OutboxCommitTests.PermittedDirectCommits = []`; reword the §7 comments. R1, R2, R3, R6 green; every
  local project green; Release build clean (`dotnet format` + analyzers); `git diff --stat` shows the
  migration, Designer and snapshot files. Commit `fix(1467): a stream always has a fab, and nothing
  attributes one after provisioning`. Files: `src/StreamDistribution/{Domain,Application,Infrastructure}/**`,
  the §5.3/§7 test and comment files. Depends on T020.
- [ ] **T040 [US2]** (infra-engineer) Remove the `stream-distribution-attribution` client and its
  service-account user from `src/AppHost/Realms/smart-sentinel-eye-realm.json`; remove the
  `StreamDistributionAttributionClientSecret` parameter, `StreamFabAttribution__ClientSecret` env and
  `.WithReference(cameraCatalog)` (with its comment) from stream-distribution in `src/AppHost/AppHost.cs`,
  and correct the api-gateway comment that cites that `WithReference`; regenerate the five
  `tests/Integration.Tests/AppHostComposition/*.snapshot.txt` by their documented procedure; delete the
  `RealmImportMirrorTests.ProductionDefaults` row, `AppHostParameterOverrideTests:56` and
  `AppHostStackStatusTests:127`. Locally: stop the stack, delete the Keycloak volume, boot once if Docker
  is available. R5 green in CI. Commit `fix(1467): retire the stream-distribution-attribution credential`.
  Depends on T030.

## Phase 4: Verify and review

- [ ] **T041** (engineer/orchestrator, phase 5) Spec §Independent end-to-end steps 2-6 where a stack can be
  booted; otherwise the CI Integration shards (R4, R5, C1-C3) are the evidence and the PR says so. Latency:
  N/A (spec). Depends on T040.
- [ ] **T050** (backend-reviewer, infra-reviewer, security-reviewer, `/code-review`, `/security-review`)
  Focus: plan §9; every deleted test in plan §5.3 is justified in the PR body; no `RetryEveryMethod()`
  left without a client; the migration refuses before it alters. Depends on T041.

## PR body must carry

Red output (R1-R3, R6 local; R4/R5 CI), characterisation green (C1-C3, CI), the plan §5.3 deletion table,
the plan §8 operator note (pre-deploy count; persistent Keycloak volume), "null population not
re-measured" unless T001 measured it, and a recommendation that a human add the expiry note to ADR-0116.
