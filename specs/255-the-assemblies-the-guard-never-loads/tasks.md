# Tasks 255 — The assemblies the guard never loads

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2586 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: the guard's scanned scope grows).
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b — test-file edit only, no
`src/`) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2586 (on Project #13, Todo). No per-task issues.

Format: `[ID] [P?] [Story] description`. No foundational work. No `[P]`: every task
edits `OutboxCommitTests.cs`; the slice is two commits, one agent per phase.

Build note: if an AppHost from this worktree holds `src/*/Api/bin`, build with
`--artifacts-path <scratch>`; do not stop a stack you did not start.

## Phase 4a — red (test-writer)

- [ ] **T001 [US2]** In `tests/Architecture.Tests/OutboxCommitTests.cs` add the fact
  `Every_assembly_that_can_reach_a_DbContext_is_scanned` exactly as plan §2
  (candidates from `AppContext.BaseDirectory`, test assembly excluded; criterion =
  references `Microsoft.EntityFrameworkCore` or a `SmartSentinelEye.*.Infrastructure`;
  vacuity check on `SmartSentinelEye.CameraCatalog.Infrastructure`; message names each
  missing assembly). Do not touch the scanned list or any other member.
- [ ] **T002** Run `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`
  and capture verbatim. **Required**: only T001's fact red, naming exactly the 20
  assemblies in plan §2 (9 `.Api`, 9 `.Application`, `ServiceDefaults`,
  `MigrationRunner`); the 9 theory cases, the probe fact and the stale-entry fact
  green. Any other pattern → stop and report. Run the full `Architecture.Tests`
  project: only T001's fact may fail. Commit
  `test(architecture): require the outbox guard to scan every assembly that can reach a DbContext`.

Depends: T001 → T002.

## Phase 4b — widen (backend-engineer; may not edit T001's fact)

- [ ] **T003 [US1]** Add the 20 assemblies to the scanned list; rename
  `PersistenceAssemblies` → `ScannedAssemblies` including the stale-entry message
  text; one class-doc sentence (plan §3).
- [ ] **T004** Filtered run all green (29 theory cases; record wall time), T001's fact
  unmodified since its commit (`git diff HEAD~1 -- tests/Architecture.Tests/OutboxCommitTests.cs`
  shows no change inside that method), full `Architecture.Tests`, Release build of
  `Architecture.Tests`. Commit
  `fix(architecture): scan every assembly that can reach a DbContext for direct commits`.

Depends: T002 → T003 → T004.

## Phase 5 — verify

- [ ] **T005 [US1]** Counterfactual A: add a throwaway class in `src/CameraCatalog/Api/`
  taking `CameraCatalogDbContext` and awaiting `SaveChangesAsync`. On the branch tip
  → `Nothing_commits_without_its_announcements("SmartSentinelEye.CameraCatalog.Api")`
  red naming it. On `origin/develop` (or with the list reverted) → green: the gap.
  Revert; `git status` clean; touch restored files before re-running.
- [ ] **T006 [US2]** Counterfactual B: remove `SmartSentinelEye.Identity.Api` from the
  list → T001's fact red naming only it. Restore. All outputs verbatim into
  `specs/255-the-assemblies-the-guard-never-loads/verification.md`.

## Bookkeeping (orchestrator)

- [ ] **T007** PR body: `Closes #2586`; quote T002's red and T005–T006 verbatim;
  state the measurement (spec §1.1: 0 offenders in 21 added assemblies) and the
  rejected list-only option. Re-check spec number 255 against unmerged branches and
  worktrees before merge (254 is already triple-claimed). Confirm #2586 closed after
  merge.

## Out of scope — do not do

- Any file under `src/` (except T005's throwaway, reverted).
- `PermittedDirectCommits` — no new entry; none is needed.
- IL-scan blind spots (spec 252) · `ITransactionalCommit.cs` doc (#2588).
