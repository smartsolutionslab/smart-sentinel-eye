# Tasks 368: The ServiceDefaults header parsers, backlog check and claim under thirty lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2951 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). Each group's test
task must be seen **passing on unrefactored production code (`2b895f82`)** before its refactor
task starts. After every refactor commit it must pass **byte-unmodified**. An assertion that has to
be edited after a refactor is evidence the behaviour moved: block, do not adjust. A
characterisation that is red before any production edit is a test defect, and the fix goes in the
test.

**Engineer:** backend-engineer (T020-T023). **Test-writer:** T001-T004. **Security-sensitive:**
T002, T004, T021, T023, so Phase 6 runs `/security-review`.

**Forbidden in every task:** any production file other than plan §2's four; any existing test file;
`CompleteAsync`, `ReleaseAsync`, `ReadBacklogAsync`, the `catch` clauses; suppressions,
thresholds, `.editorconfig`, `SonarLint.xml`, csproj files; methods outside spec §1.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). On one
branch the commits still land one after another.

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: `dotnet build src/ServiceDefaults -c Release
  --no-incremental` on `2b895f82` (7 S138 + 4 S107), and the `dotnet test
  tests/ServiceDefaults.Tests -c Release` summary line.

## Phase 2: Characterisation (test-writer; no foundational task, all four are disjoint)

- [ ] **T001** [P] [US1] C1 `tests/ServiceDefaults.Tests/UpsertPreconditionProblemCharacterisationTests.cs`
  (spec §4.1).
- [ ] **T002** [P] [US1] C2 `tests/ServiceDefaults.Tests/Idempotency/IdempotencyHeadersTests.cs`
  (spec §4.2; J3 for the zero-values case).
  Commit T001+T002: `test(service-defaults): pin the upsert precondition and idempotency header refusals`.
- [ ] **T003** [P] [US1] C3 `tests/ServiceDefaults.Tests/OutboxBacklogAssessmentCharacterisationTests.cs`
  (spec §4.3).
  Commit `test(service-defaults): pin the outbox backlog's four read outcomes`.
- [ ] **T004** [P] [US1] C4 `tests/ServiceDefaults.Tests/Idempotency/IdempotencyStoreCharacterisationTests.cs`
  (spec §4.4). **Start with O1 alone.** If EF cannot materialise the canned reader, stop and
  report: G3 is deferred (spec J1). Committed **after** G2's refactor (T022), as the start of G3.
  Commit `test(service-defaults): pin the idempotency claim's SQL, parameters and outcomes`.
- [ ] **T010** Gate: T001-T004 green on production `2b895f82`, Release. Quote verbatim. Run plan
  §5.2's counterfactuals, quote each red, and revert them (`git status` clean in `src/`).

## Phase 3: G1, header parsers (backend-engineer; after T010)

- [ ] **T020** [US1] `ConcurrencyHeaders.TryReadUpsertPrecondition` → `TryReadCreateAssertion`
  (plan §3). Commit `refactor(service-defaults): read the If-None-Match assertion in its own method`.
- [ ] **T021** [US1] `IdempotencyHeaders.TryRead` → `TryParse`, its comment moves with it (plan §3).
  Commit `refactor(service-defaults): parse the idempotency key in its own method`.

## Phase 4: G2, health (backend-engineer; after T021)

- [ ] **T022** [US1] `OutboxBacklogHealthCheck.CheckHealthAsync` → `Assess`, still inside the `try`
  (J4). Commit `refactor(service-defaults): assess the outbox backlog in its own method`.

## Phase 5: G3, the claim (last; deferrable per J1)

- [ ] **T004 commit** (see above), then:
- [ ] **T023** [US1] `IdempotencyStore<T>.BeginAsync` → `ClaimSql`/`ReadBindingSql` consts,
  `ClaimAsync`, `ReadBindingAsync`, `Classify` (plan §3, J2). Every comment verbatim.
  Commit `refactor(service-defaults): split the idempotency claim from the binding read`.

## Phase 6: Verify (orchestrator)

- [ ] **T030** After each refactor commit: `dotnet test tests/ServiceDefaults.Tests -c Release`
  green and `git diff <T010-sha> -- tests/` empty (the T004 file is compared against its own commit).
- [ ] **T031** Plan §5.4 build: 3 S138 (`AddBearerAuthentication`, `AddRevocationCheck`,
  `AddWolverineForContext`) + 4 S107. `dotnet format --verify-no-changes`. `tests/Architecture.Tests`
  green.
- [ ] **T032** Each commit builds and passes on its own (`git rebase -x` over the branch).
- [ ] **T033** CI integration shards green (spec §4.5) before merge.

## Dependencies

T000 → T001/T002/T003/T004 (parallel) → T010 → T020 → T021 → T022 → T004 commit → T023 → T030-T033.
None of this is foundational for other work. A deferred G3 removes T004 and T023 and nothing else.
