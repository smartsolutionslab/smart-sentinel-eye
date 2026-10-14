# Tasks 364: The scenario simulator under its method-length limit

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2933 (feature-level;
already on Project #13 via the autonomous lane).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T007 are written
and observed **green on unrefactored code** before any `src/` edit; they and every existing test in
`tests/ScenarioSimulator.Tests` must pass **unmodified** after Phase 3. An assertion that must be
edited after a refactor commit is evidence the behaviour moved: block, do not adjust. A test that
cannot be made green on the unrefactored code has found a bug: stop, report it, do not fold a fix in.

**Engineer:** backend-engineer. **Test-writer:** T001-T007.

**Commits.** One commit per task, Conventional Commits (`test(scenario-simulator): ...`,
`refactor(scenario-simulator): ...`), no `Co-Authored-By` (ADR-0086), never `--no-verify`. Each
commit builds and its tests pass on its own (rebase-merge lands them individually).

**Forbidden in every task:** `src/ScenarioSimulator/Program.cs`; any file outside
`src/ScenarioSimulator/` and `tests/ScenarioSimulator.Tests/`; any public/internal signature change;
any helper with more than four parameters; any new suppression, `#pragma`, `.editorconfig` or
`Directory.Build.props` edit.

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Run plan §5 on the branch tip before Phase 2; record verbatim for the PR body (11
  S138/S134 rows, 15 S107 rows on `0296275e`).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

All files under `tests/ScenarioSimulator.Tests/`. Disjoint files, so `[P]`.

- [ ] **T001** [P] [US1] `CameraCatalogClientTests.cs`: C1, C2 (plan §4).
- [ ] **T002** [P] [US1] `CameraSimPathClientTests.cs`: C3-C7.
- [ ] **T003** [P] [US1] `ClipManifestValidationTests.cs` (it already hosts the loader tests):
  C8, C9.
- [ ] **T004** [P] [US1] `AutomationRulesClientTests.cs`: C10, C11 (C11 only if unpinned).
- [ ] **T005** [P] [US1] `ReactionRuleSeedTests.cs`: C12-C16.
- [ ] **T006** [P] [US1] New `ScenarioSeederRunCharacterisationTests.cs`: C17-C20
  (`ExecuteAsync`; reuse `Fakes/RecordingLogger.cs`).
- [ ] **T007** [P] [US1] New `ScenarioSeederAssetCharacterisationTests.cs`: C21-C26
  (`SeedOverlayAndRuleAsync`, `SeedReactionsAsync`).
- [ ] **T008** [US1] (depends T001-T007) Run `dotnet test tests/ScenarioSimulator.Tests -c Release`
  on unrefactored `src/`; quote the verbatim summary. Record any C-test skipped as already pinned
  (C11, C26) or reduced (C20) with the reason. Note the Phase-2 tip SHA.

## Phase 3: Refactor (backend-engineer; depends on T008)

One file per task, disjoint, so `[P]`. Each task ends with plan §5's build: the task's rows gone,
no new S107/S134/S138 row, tests green with `git diff <T008 SHA> -- tests/` empty.

- [ ] **T009** [P] [US1] `CameraCatalog/CameraCatalogClient.cs`: plan §3.1 (`ReadPageAsync`).
- [ ] **T010** [P] [US1] `CameraSim/CameraSimPathClient.cs`: plan §3.2 (`ReadPathStateAsync`).
- [ ] **T011** [P] [US1] `Cues/ClipManifestLoader.cs`: plan §3.3 (`LoadSidecar`).
- [ ] **T012** [P] [US1] `Cues/ClipManifestValidation.cs`: plan §3.4 (`AddCountViolation`).
- [ ] **T013** [P] [US1] `Seeding/AutomationRulesClient.cs`: plan §3.5 (`CreateBody`,
  `PublishAsync`; `CreateBody` before the token fetch).
- [ ] **T014** [P] [US1] `Seeding/ReactionRuleSeed.cs`: plan §3.6 (`RefuseBlankTrigger`; name check
  stays first).
- [ ] **T015** [P] [US1] `Seeding/ScenarioSeeder.cs`: plan §3.7 (`SeedScenarioAsync`,
  `SeedAssetAsync`, `SeedWallsAsync`, `SeedOverlayAsync`, `SeedHighlightRuleAsync`, `IsUnreachable`,
  `EnsureReactionRuleAsync`). May be split into up to three commits (rows 7+8, 9, 10), each green.

## Phase 4: Verify (orchestrator)

- [ ] **T016** [US1] After evidence, verbatim: plan §5 grep shows only `Program.cs(125)` S138 plus
  the same 15 S107 members; `dotnet test` summary; `git diff <T008 SHA> -- tests/` empty. Declare
  plan §6 shapes. Latency: N/A (spec §7).
- [ ] **T017** PR to `develop` (`gh pr create --base develop`). Body: T000, T008 and T016 outputs,
  spec J2 (`BindRuntime` left; follow-up is the orchestrator's call), J3 oddities pinned not fixed,
  `Closes #2933`.

## Dependency graph

```
T000 -> {T001..T007} [P] -> T008 -> {T009..T015} [P] -> T016 -> T017
```

No foundational/shared task: nothing in `Shared.*`, `AppHost` or Aspire resources is touched, so
Phase 3 fans out fully after T008. Phase 2 fans out fully after T000. Note: with one branch and one
index, parallel agents need separate worktrees or must serialise commits.
