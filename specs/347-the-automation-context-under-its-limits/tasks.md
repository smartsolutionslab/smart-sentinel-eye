# Tasks 347: The automation context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2889 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T010 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T010.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/` or `Migrations/`; any existing test file;
`Log.cs`; `Rule.cs`; `Shared.Contracts`; suppressions, thresholds, `.editorconfig`,
`SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (19 lines on `d259668e`) and the
  four `dotnet test` runs (Domain 132, Application 174, Infrastructure 16, Architecture) plus the
  benchmark filter.

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [P] [US1] C1, `tests/Automation.Application.Tests/Ael/AelLexerCorpusCharacterisationTests.cs`.
  Commit `test(automation): pin the AEL lexer's token stream and refusals over a corpus`.
- [ ] **T002** [P] [US1] C2, `tests/Automation.Application.Tests/Ael/AelParserCorpusCharacterisationTests.cs`.
  Commit `test(automation): pin AEL parse trees and parse-error messages`.
- [ ] **T003** [P] [US1] C3, `tests/Automation.Application.Tests/Ael/AelBinaryOperatorCharacterisationTests.cs`.
  Commit `test(automation): pin AEL binary operator results and failures`.
- [ ] **T004** [P] [US1] C4, `tests/Automation.Application.Tests/Evaluation/CompiledRuleCharacterisationTests.cs`.
  Commit `test(automation): pin compiled rule mapping and parse order`.
- [ ] **T005** [P] [US1] C5, `tests/Automation.Application.Tests/Evaluation/RuleEvaluatorEffectOrderCharacterisationTests.cs`.
  Commit `test(automation): pin rule evaluator effect order and skipped-rule logs`.
- [ ] **T006** [P] [US1] C6, `tests/Automation.Application.Tests/EventHandlers/FabEventFanOutCharacterisationTests.cs`.
  Commit `test(automation): pin fan-out order, contract types and causation fields`.
- [ ] **T007** [P] [US1] C7, `tests/Automation.Application.Tests/Commands/CreateRuleRefusalOrderCharacterisationTests.cs`.
  Commit `test(automation): pin create-rule refusal order and log text`.
- [ ] **T008** [P] [US1] C7b, `tests/Automation.Application.Tests/Queries/RuleQueryRefusalOrderCharacterisationTests.cs`.
  Commit `test(automation): pin list and dry-run refusal order`.
- [ ] **T009** [P] [US1] C8, `tests/Architecture.Tests/Persistence/AutomationRegistrationCharacterisationTests.cs`.
  Commit `test(architecture): pin automation infrastructure registrations`.
- [ ] **T010** [P] [US1] C9, `tests/Automation.Infrastructure.Tests/AutomationModelCharacterisationTests.cs`.
  Commit `test(automation): pin the EF model's logical shape`.

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code.

Gate: the orchestrator holds ten verbatim green outputs.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T011** [P] [US1] `Application/Ael/AelInterpreter.cs`: plan §3.1. Hit 1. Also run the
  benchmark filter. Commit `refactor(automation): route ordered binary operators through one helper`.
- [ ] **T012** [P] [US1] `Application/Ael/AelLexer.cs`: plan §3.2. Hits 2, 3, 4. C1 and C2 must be
  green unmodified. Commit `refactor(automation): split the AEL lexer by token family`.
- [ ] **T013** [P] [US1] `Application/Ael/AelParser.cs`: plan §3.3. Hit 5.
  Commit `refactor(automation): extract parenthesised expression parsing`.
- [ ] **T014** [P] [US1] `Application/Commands/Handlers/CreateRuleCommandHandler.cs`: plan §3.4. Hit 6.
  Commit `refactor(automation): extract create-rule expression refusals`.
- [ ] **T015** [P] [US1] `Application/Evaluation/CompiledRule.cs`: plan §3.5. Hit 7.
  Commit `refactor(automation): build a compiled rule from the rule it compiles`.
- [ ] **T016** [P] [US1] `Application/Evaluation/RuleEvaluator.cs`: plan §3.6. Hit 8. Also run the
  benchmark filter. Commit `refactor(automation): extract per-rule effect mapping`.
- [ ] **T017** [P] [US1] `Application/EventHandlers/FabEventIngestedV1Handler.cs`: plan §3.7. Hit 9.
  Commit `refactor(automation): split the ingested-event handler by phase`.
- [ ] **T018** [P] [US1] `Application/Queries/Handlers/DryRunRuleQueryHandler.cs`: plan §3.8. Hits 12, 13.
  Commit `refactor(automation): extract dry-run lookup and evaluation`.
- [ ] **T019** [P] [US1] `Application/Queries/Handlers/ListRulesQueryHandler.cs`: plan §3.9. Hit 14.
  Commit `refactor(automation): extract rule list filters`.
- [ ] **T020** [P] [US1] `Infrastructure/AutomationInfrastructureModule.cs`: plan §3.10. Hit 16.
  Commit `refactor(automation): group infrastructure registrations`.
- [ ] **T021** [P] [US1] `Infrastructure/Persistence/Configurations/RuleConfiguration.cs`: plan §3.11. Hit 17.
  Commit `refactor(automation): split rule EF configuration`.

Per task: run plan §5's grep (the task's hits gone, no new hit) and the test suites of the
touched layer plus `Architecture.Tests` (T020-T021: also `Automation.Infrastructure.Tests`).
T011, T016, T017 touch the per-event path: confirm in the commit's report that no allocation,
I/O, lock or `await` was added (spec FR-004).

## Phase 4: Verify (orchestrator)

- [ ] **T022** [US1] After evidence, verbatim: the §5 grep (exactly the 5 "Leave" rows), all four
  test runs plus the benchmark, `dotnet build -c Release` (0 errors), `dotnet format
  --verify-no-changes` on the touched projects, coverage (Domain >= 90 %, Application >= 80 %).
  Confirm `git diff <T010 commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Log\.cs$|/Domain/'` is empty.
- [ ] **T023** PR to `develop` (`--base develop`). Body: T000 and T022 outputs, the 5 left hits
  each with its reason (spec §2), Phase 4a colour "characterisation", the J1-J3 judgement calls,
  the two oddities left unchanged (spec §6), latency leg *Event → overlay state* unchanged by
  construction, `Closes #2889`. Re-check spec number 347 is still free. Integration.Tests
  (incl. `AcceptToDecideLatencyTests`) observed in CI.

## Dependency graph

```
T000
T001..T010 (all [P]) ──> T011..T021 (all [P]) ──> T022 ──> T023
```

Phase 3 tasks own disjoint files and need no order among themselves. Nothing here is foundational
to another context: no `Shared.Kernel`, `Shared.Contracts`, `AppHost` or Aspire resource change.
