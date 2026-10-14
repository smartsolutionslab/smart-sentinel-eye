# Tasks 352: The audit observability context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2894 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T006 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. Each also has its named mutations (plan §4) applied locally, observed red,
and reverted. An assertion that has to be edited after a refactor commit is evidence the behaviour
moved: block, do not adjust.

**Engineer:** backend-engineer. **Test-writer:** T001-T006. **Phase 6:** backend-reviewer **and
security-reviewer** (T013).

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each commit
must build **and pass** on its own: test commits add green tests against unchanged production code;
each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/`, `Domain/` or `Migrations/`; any existing test
file or shared test fake; both `Log.cs`; `AuditingMessageHandler`; `AuditRetentionHostedService`;
`AuditCursor`; DI modules; `.slnx`/`.csproj`; `Shared.Contracts`; the MD5 `#pragma`; suppressions,
thresholds, `.editorconfig`, `SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (11 lines on `8b1840bd`), the three
  `dotnet test` runs (Domain 95, Application 84, Architecture 759), `has-pending-model-changes`
  ("No changes ..."), and the `NFR002_AuditSearchLatencyTests` line from the last green `develop`
  CI run's TRX.

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [P] [US1] C1, `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditRefusalAndScopeCharacterisationTests.cs`.
  Commit `test(audit-observability): pin search refusal order, throw order and fab scope`.
- [ ] **T002** [P] [US1] C2, `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditPagingCharacterisationTests.cs`.
  Commit `test(audit-observability): pin search paging, cursor boundaries and filters`.
- [ ] **T003** [P] [US1] C3, `tests/AuditObservability.Application.Tests/Queries/Handlers/GetResourceTimelineCharacterisationTests.cs`.
  Commit `test(audit-observability): pin timeline refusal order, scope and paging`.
- [ ] **T004** [P] [US1] C4, `tests/Architecture.Tests/Persistence/AuditObservabilityModelCharacterisationTests.cs`.
  Commit `test(architecture): pin the audit EF model's logical shape`.
- [ ] **T005** [P] [US1] C5, `tests/Architecture.Tests/Persistence/AuditEventInsertCharacterisationTests.cs`.
  Commit `test(architecture): pin the audit insert's statement, values and batch semantics`.
  If the interceptor seam fails: no commit; return the verbatim failure (spec J3).
- [ ] **T006** [P] [US1] C6, `tests/Architecture.Tests/Persistence/AuditChunkArchiverCharacterisationTests.cs`.
  Commit `test(architecture): pin audit chunk archiving branches, key, body and hash`.
  If the interceptor seam fails: no commit; return the verbatim failure (spec J3).

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code, plus the verbatim red output of **each**
named mutation (plan §4) and confirmation it was reverted (`git diff --stat -- src/` empty).

Gate: the orchestrator holds the green outputs and every mutation's red output. A failed T005/T006
moves hit 9/8 to "Leave" and drops T010/T011.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T007** [P] [US1] `Application/Queries/Handlers/SearchAuditQueryHandler.cs`: plan §3.1. Hits 6, 7.
  Commit `refactor(audit-observability): split search into scope, filter and page steps`.
- [ ] **T008** [P] [US1] `Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs`: plan §3.2. Hits 4, 5.
  Commit `refactor(audit-observability): extract the timeline's cursor, window and page steps`.
- [ ] **T009** [P] [US1] `Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs`: plan §3.3. Hit 10.
  Also run `has-pending-model-changes`.
  Commit `refactor(audit-observability): split audit event EF configuration`.
- [ ] **T010** [P] [US1] `Infrastructure/Persistence/AuditEventRepository.cs`: plan §3.4. Hit 9. Needs T005.
  Per-event path: report that the helper is not `async` and no allocation, I/O, lock or `await` was added.
  Commit `refactor(audit-observability): extract the audit row insert`.
- [ ] **T011** [P] [US1] `Infrastructure/Archive/AzureBlobAuditChunkArchiver.cs`: plan §3.5. Hit 8. Needs T006.
  Commit `refactor(audit-observability): extract archive body writing and upload`.

Per task: run plan §5's grep (the task's hits gone, no new hit), `AuditObservability.Application.Tests`
and `Architecture.Tests`.

## Phase 4: Verify, review, PR (orchestrator)

- [ ] **T012** [US1] After evidence, verbatim: the §5 grep (exactly the "Leave" rows), all three test
  runs, `has-pending-model-changes`, `dotnet build -c Release` (0 errors), `dotnet format
  --verify-no-changes` on both projects, coverage (Domain >= 90 %, Application >= 80 %). Confirm
  `git diff <last 4a commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Log\.cs$|/Domain/|Shared\.Contracts|\.slnx$|\.csproj$'` is empty.
- [ ] **T013** Phase 6: backend-reviewer, plus **security-reviewer**, which must check: (1)
  `ScopeToFabs` is byte-equivalent to today's branch — named fab excludes null-fab rows; claimed fabs
  parse per entry and include null-fab rows; no usable claim sees only null-fab rows; no helper
  widens a predicate; (2) every query helper is `IQueryable` in and out (no client-side filtering);
  (3) cursor handling: `AuditCursor` untouched, `IsUndecodableCursor` refuses exactly what today
  refuses (incl. `""`), comparisons stay strict; (4) the insert still passes the interpolated literal
  directly to `ExecuteSqlInterpolatedAsync` (parameterised; no `ExecuteSqlRaw*`, no hoisted `string`);
  (5) the archiver awaits the upload before returning and propagates every failure, so the retention
  sweep never drops an unarchived chunk; (6) the spec §6 oddities (b)-(d) are recorded, not changed.
- [ ] **T014** PR to `develop` (`--base develop`). Body: T000 and T012 outputs, the left hits each
  with its reason, Phase 4a colour "characterisation" with green and mutation-red outputs, J1-J5,
  the oddities left unchanged, the analyzer-forced shapes (plan §3.7), latency: §IV none, NFR-002
  before/after from CI TRX (PR run + one re-run), NFR-001 "no CI figure, held by construction",
  `Closes #2894`. Re-check spec number 352 is still free.

## Dependency graph

```
T000
T001..T006 (all [P]) ──> T007..T011 (all [P]; T010 needs T005, T011 needs T006) ──> T012 ──> T013 ──> T014
```

Phase 3 tasks own disjoint files and need no order among themselves. Nothing here is foundational
to another context: no `Shared.Kernel`, `Shared.Contracts`, `AppHost` or Aspire resource change.
