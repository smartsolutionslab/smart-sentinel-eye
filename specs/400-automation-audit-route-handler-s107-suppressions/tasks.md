# Tasks 400: the Automation and AuditObservability route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2974 (feature-level; already on
Project #13; no per-task issues).

**Gate before T001: D-1 and D-2 (spec §6) confirmed.** Written for D-1a and D-2a. If **D-1b** is chosen:
drop T003, and in T012 use the D-1b justification on `Search`/`GetTimeline` only. If **D-2b** is chosen:
drop T010 and amend SC-2's expectation to include the one S104 the owner accepted.

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 runs the existing
covering tests on unchanged code (`f950cb7c`); T003 captures the new AuditObservability baseline on the same
unchanged code. Both outputs are quoted verbatim in the PR. The same commands must pass after T012 with no
pre-existing `tests/` file modified. An assertion that would have to change is a **block**, not an edit
(ADR-0144).

**Roles:** test-writer T001-T003 (T001/T002 run and measure only). backend-engineer T010-T012. Not
security-sensitive: Phase 6 is `/code-review`.

**Forbidden in every task:** editing any pre-existing file under `tests/`; any handler signature,
`*Services` record, `Map*` call or metadata; `GetSingle` (its body belongs to #3036); attributes on any
helper; any edit inside a moved method body; `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`,
csproj files; ADRs, the constitution, CLAUDE.md; any suppression other than the eight in spec §1.

Legend: `[P]` = owns disjoint files (ADR-0109). No foundational task. One branch, one index: the `[P]`
markers say the files are disjoint, not that two agents should commit concurrently.

## Phase 4a — characterisation (before, on `f950cb7c`)

- [ ] **T001 [US1]** Run the existing covering tests on unchanged code, capture verbatim:
  ```sh
  dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~AutomationRouteTableCharacterisationTests|FullyQualifiedName~RuleActionBuildCharacterisationTests|FullyQualifiedName~RuleCreateEndpointCharacterisationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests"
  ```
  All green expected. Red on unchanged code is a pre-existing defect: stop and report.
- [ ] **T002 [US1]** Before-measurement (spec §1 commands, one closure at a time, Windows-form logfile path
  in the scratchpad): 6 S107 under `src/Automation/Api`, 2 under `src/AuditObservability/Api`, and the two
  six-line remainders. Save the four sorted lists for T020.
- [ ] **T003 [P] [US1]** (test-writer, D-1a) Write
  `tests/Architecture.Tests/AuditObservabilityRouteTableCharacterisationTests.cs` per plan "The new
  baseline": SystemVariables shape, `RouteTableRendering` + `InProcessEndpointDriver`, a literal `Baseline`
  captured from the renderer on `f950cb7c`, plus a three-routes count fact. XML doc names the capture SHA and
  says what is pinned (route, verbs, name, summary, tags, policy, responses, handler name and full parameter
  list) and nothing more. Run it (filter `FullyQualifiedName~AuditObservabilityRouteTableCharacterisationTests`)
  and capture the green output verbatim. Commit alone (`test(2974): ...`) before any `src/` edit. Depends on
  T001.

## Phase 4b — implement (backend-engineer)

- [ ] **T010 [US1]** (D-2a) Move `BuildAction`, `BuildSetVariableValue`, `BuildHighlightOverlay`,
  `BuildSwitchWallScene` verbatim from `src/Automation/Api/RulesEndpoints.cs` into new
  `src/Automation/Api/RulesEndpoints.Actions.cs` (`public static partial class RulesEndpoints`, same
  namespace, only the usings the moved code needs). Constants stay. Rebuild the Automation closure: warning
  list identical to T002's; run T001's command: green. Commit alone (`refactor(2974): ...`). Depends on T003.
- [ ] **T011 [P] [US1]** Add the spec §5 attribute verbatim above `Create`, `Publish`, `Archive`
  (`RulesEndpoints.cs`) and `List`, `GetOne`, `DryRun` (`RulesEndpoints.Reads.cs`), plus
  `using System.Diagnostics.CodeAnalysis;` as line 1 of each. Depends on T010.
- [ ] **T012 [P] [US1]** Add the spec §5 attribute verbatim above `Search` and `GetTimeline` in
  `src/AuditObservability/Api/AuditEndpoints.cs`, plus the `using` as line 1. Not `GetSingle`. Depends on T003.
  T011 and T012 land in one commit (`chore(2974): ...`).

## Phase 5 — verify

- [ ] **T020 [US1]** Re-run T002's commands: S107 under both Api projects = 0; remainders `diff` empty
  against T002's; no S104 anywhere in either list (SC-1, SC-2). `grep -c "S107:Methods should not have too
  many parameters"` sums to 6 over the two Rules files and 2 over `AuditEndpoints.cs` (SC-4).
  `grep -c SuppressMessageAttribute` on both built Api DLLs = 0.
- [ ] **T021 [US1]** Re-run T001's command plus the T003 filter, unchanged; all green;
  `git diff f950cb7c --diff-filter=M -- tests/` empty (SC-3). Quote T001, T003 and T021 outputs in the PR.
- [ ] **T022** `dotnet format whitespace` and `dotnet format style --verify-no-changes --diagnostics IDE0005`
  on both Api projects and Architecture.Tests.
- [ ] **T023** If #3036 merged meanwhile: rebase, re-run T020/T021 (spec §7).

## Phase 6-7

- [ ] **T030** `/code-review` on the diff: four `src/` files and one test; confirm no helper or `GetSingle`
  got an attribute and the moved bodies are byte-identical (`git diff --color-moved=plain` shows them as moved).
- [ ] **T031** PR to `develop` (`--base develop`) with `Closes #2974`; body quotes T001/T003/T021, T020's
  before/after lists, the D-1/D-2 choices, and notes ADR-0084's now-stale "AuditObservability has none".
  Issue already on Project #13.

## Dependencies

T001, T002 -> T003 -> T010 -> T011; T003 -> T012; T011, T012 -> T020, T021, T022 (-> T023) -> T030 -> T031.
