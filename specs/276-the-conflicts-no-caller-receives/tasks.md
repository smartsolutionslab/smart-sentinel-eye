# Tasks 276 — The conflicts no caller receives

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2200 · **Phase**: 3 (Tasks)
**Colour**: **characterisation, observed green** — every task. No red task exists in this spec.
**Engineer**: `backend-engineer` (4b) after `test-writer` (4a) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2200 — **already on Project #13** (verified 2026-09-27). No per-task
issues.

Format: `[ID] [P?] [Story] description`. No foundational (Shared.Kernel / Shared.Contracts / AppHost)
work, so nothing blocks a fan-out — but the 4a → 4b ordering below is mandatory.

## Phase 4a — characterisation (test-writer)

- [ ] **T001 [P] [US1]** Add C1 `Setting_a_value_on_an_archived_variable_is_refused_as_not_found` to
  `tests/Integration.Tests/SystemVariables/SystemVariableLifecycleIntegrationTests.cs` per plan.md
  (post-archive version from the archived listing; assert 404 + `VARIABLE_NOT_FOUND`).
- [ ] **T002 [P] [US1]** Add C2 `Publishing_an_archived_rule_is_refused_as_not_found` to
  `tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs` per plan.md (post-archive
  version; assert 404 + `RULE_NOT_FOUND`).
- [ ] **T003 [US1]** Run C1, C2 against **unmodified** production code on the Aspire fixture. Both
  must be **green**; capture verbatim output. Red here means the premise is wrong — stop and report.
- [ ] **T004 [US1]** Counterfactual 1 (spec §5): remove the `State != Archived` predicate from
  `src/SystemVariables/Infrastructure/Persistence/VariableRepository.cs` → C1 red with 409
  `VARIABLE_ARCHIVED`; restore. Same for `src/Automation/Infrastructure/Persistence/RuleRepository.cs`
  → C2 red with 409 `RULE_ALREADY_ARCHIVED`; restore. After each: `git diff` empty,
  `dotnet build --no-incremental`. Capture both red outputs verbatim. A red that is **not** the named
  409 (e.g. 409 `*_STALE`, or 500) means the test is not on the archived branch — fix the test's
  version, not the assertion.
- [ ] **T005 [US1]** Commit T001–T002 only: `test(integration): pin 404 for mutating an archived
  variable or rule`. No shard-filter change needed (both classes already in `shard-1.filter`).

Depends: T001 ∥ T002 (disjoint files) → T003 → T004 → T005.

## Phase 4b — deletion and documentation (backend-engineer)

- [ ] **T006 [P] [US1]** SystemVariables: delete `SetVariableValueError.VariableArchived`
  (`src/SystemVariables/Application/Commands/SetVariableValueErrors.cs:15-19`) and
  `SetVariableValueFailures.VariableArchived` (`:50-51`); delete the
  `if (variable.State == VariableState.Archived)` block in
  `src/SystemVariables/Application/Commands/Handlers/SetVariableValueCommandHandler.cs:43-46`.
  Leave `VariableStale` and `Variable.SetValue` untouched.
- [ ] **T007 [P] [US1]** Automation: delete `PublishRuleError.RuleAlreadyArchived`
  (`src/Automation/Application/Commands/PublishRuleCommand.cs:20-24`) and
  `PublishRuleFailures.RuleAlreadyArchived` (`:49-50`); replace the `try/catch` in
  `src/Automation/Application/Commands/Handlers/PublishRuleCommandHandler.cs:39-46` with a bare
  `rule.Publish(clock);`. Leave `Rule.Publish` and `RuleStateMachineTests` untouched.
- [ ] **T008 [US1]** `tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs:301`: mechanism
  string `"refusal (PublishRuleFailures.RuleStale, RuleAlreadyArchived); lost update "` →
  `"refusal (PublishRuleFailures.RuleStale); lost update "`. **Keep the entry** — removing it fails
  `Every_mutating_route_sits_in_exactly_one_of_the_two_pinned_sets` (spec §1.2). Same commit as T007
  (it names the deleted type).
- [ ] **T009 [US1]** Counterfactual 2 + verification: `grep -rn "VariableArchived(\|RuleAlreadyArchived\|VARIABLE_ARCHIVED\|RULE_ALREADY_ARCHIVED" src tests apps --include=*.cs --include=*.ts --include=*.tsx`
  → none. `dotnet build -c Release` clean (analyzers, `TreatWarningsAsErrors`). Run
  `SystemVariables.*.Tests`, `Automation.*.Tests`, `Architecture.Tests` — all green, **test files
  unmodified** since T005; quote the counts (re-measure; the ~195 / ~573 figures are unverified). Re-run
  C1, C2 on the fixture — green, unmodified. Commit T006 as
  `refactor(system-variables): delete the unreachable VariableArchived conflict`; T007+T008 as
  `refactor(automation): delete the unreachable RuleAlreadyArchived conflict`. Each commit builds and
  its context's tests pass on its own.
- [ ] **T010 [P] [US1]** Contract docs, SystemVariables: `<summary>` on
  `IVariableRepository.GetByNameAsync` (`src/SystemVariables/Domain/Variable/IVariableRepository.cs`)
  per plan.md wording; point the inline comment at
  `src/SystemVariables/Infrastructure/Persistence/VariableRepository.cs:26` to that contract. Query
  untouched. No issue/task numbers in code.
- [ ] **T011 [P] [US1]** Contract docs, Automation: same for `IRuleRepository.GetByNameAsync`
  (`src/Automation/Domain/Rule/IRuleRepository.cs`) and the inline comment at
  `src/Automation/Infrastructure/Persistence/RuleRepository.cs:27`. Keep the fab-first comment.
- [ ] **T012 [US1]** Characterise T010–T011: strip comments from the four touched files, hash before
  and after → identical; counterfactual: change one code token, hash differs, revert. Quote both.
  Commit: `docs(system-variables,automation): state that name lookups treat an archived name as gone`.

Depends: T005 → T006 ∥ T007 → T008 → T009 → T010 ∥ T011 → T012.
T006/T007 and T010/T011 own disjoint files in different contexts and may be fanned out; T008 and
T009 serialise because T009 verifies both.

## Phase 5–7 (orchestrator)

- [ ] **T013** Phase 5 `/verify`: on the running stack, archive a variable and a rule and observe 404
  `VARIABLE_NOT_FOUND` / `RULE_NOT_FOUND` on the two routes (check the AppHost PID's start time is
  after the last commit). Latency: not on a measured leg beyond one removed comparison (spec §7).
- [ ] **T014** PR body: quote T003 (green), T004 (two reds), T009 (green counts), T012 (hashes). Use
  `Closes #2200`; check the issue state after merge.
- [ ] **T015** After merge, comment on **#458** and **#695**: unblocked; their archived case now asserts
  `VariableNotFound` / `RuleNotFound` (404), not the deleted 409 types; name this spec. Do **not**
  deliver them in this PR.

## Out of scope — do not do

- Any edit to `Rule.Publish`, `Variable.SetValue`, or their domain tests (spec §1.1).
- Any change to the `GetByNameAsync` queries, the in-memory fakes' filters, or
  `ExistsIncludingArchivedAsync` (except T004's temporary, reverted counterfactual).
- Handler-level unit tests for the archived case — that is #458 / #695.
- Editing `specs/007-automation/tasks.md` T045 (history).
- Any 409/410 for archived names — rejected by the product owner (spec §2, §9).
