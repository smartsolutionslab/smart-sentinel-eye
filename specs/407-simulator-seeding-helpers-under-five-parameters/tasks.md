# Tasks 407: the simulator's seeding helpers under five parameters

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3027 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T000a first adds
`LayoutCompositionClientCharacterisationTests` (commit `C0`), observed green on unchanged code,
with the nine plan §4 counterfactuals each seen red; T001 then runs the suite on that code and its
output is quoted verbatim in the PR. The same commands must pass after T010/T011 with
`git diff C0 -- tests/` empty. An assertion that would have to change is a **block**, not an edit
(ADR-0144).

**Engineer:** backend-engineer (T010, T011). **Test-writer:** T000a (adds tests) and T001 (runs
only). **Not security-sensitive**: Phase 6 runs `/code-review` only.

**Forbidden in every task after T000a:** any file under `tests/`; the signatures of
`EnsureWallAsync`, `AssetCorrelationTable.RecordOverlay`, the four-parameter `PublishAsync`
overload, and the `CameraRegisteredSimHandler` / `MqttPublisher` constructors; any HTTP method,
path, header, body, ordering, retry, exception or log call; any body line of a reshaped helper
other than its signature, one deconstruction line plus its blank line, and the call sites named in
plan §2; `OverlayDesignerClient`, `AutomationRulesClient`, `WallSeeder`; a new file under `src/`;
ADRs, the constitution, CLAUDE.md; suppressions, thresholds, `.editorconfig`, `SonarLint.xml`,
`Directory.Build.props`, csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No
foundational task: nothing touches Shared.Kernel, Shared.Contracts or AppHost. T010 and T011 own
disjoint files and are marked `[P]`, but land **sequentially** on one branch (one branch, one
index); they are separate commits so each builds on its own (ADR-0087).

---

## Phase 1: Baseline (orchestrator; no files)

- [x] **T000** On `c8c3773c`, spec §1's Release `--no-incremental` build of the ScenarioSimulator
  closure: 8 S107 under `src/ScenarioSimulator` (H1-H8), no other Sonar rule there; 0 errors.
  Save the sorted-unique list as `before.txt` in the scratchpad. Done at Phase 1 (architect);
  T020 re-runs it for the after-figure.

## Phase 2: Characterisation, observed green (test-writer)

- [x] **T000a** [US1] Create `tests/ScenarioSimulator.Tests/LayoutCompositionClientCharacterisationTests.cs`
  with plan §4 cases 1-6 (own recording handler capturing method, path-and-query, `If-Match`,
  `Authorization`, body; `RecordingLogger<LayoutCompositionClient>`; rows 2 / cols 3; draft
  revision 5; versions 7/8/9; each tile component its own assertion; exact log text as observed).
  Do not touch `LayoutCompositionClientTests.cs` or any `Fakes/` file (spec D-1). Observe the new
  tests **and** both plan §4 filtered runs green on unchanged production code; run CF1-CF9 and
  report each red verbatim (one that stays green is fixed in the test before commit). No
  shard-filter entry (project not sharded). Commit
  `test(3027): characterise the layout client's recovery and re-tiling before grouping its parameters (green)`
  = `C0`. Depends on T000.

- [x] **T001** [US1] Run every plan §4 in-process command on `C0`; capture each summary line
  verbatim with non-zero counts. Writes no file. Depends on T000a.

## Phase 3: The change (backend-engineer; two commits)

- [x] **T010** [P] [US1] `src/ScenarioSimulator/Seeding/LayoutCompositionClient.cs` only (plan
  §2.1, §2.2): add `DesiredWall` and `RevisionAddress` (nested `private readonly record struct`,
  in the private-records block, each with a `<summary>`); reshape `RecoverAsync(DesiredWall,
  string, CancellationToken)`, `RetileAsync(DesiredWall, LayoutListItem, string,
  CancellationToken)`, `PublishAsync(RevisionAddress, int, string, CancellationToken)`, each
  opening with its deconstruction; update the three call sites (`EnsureWallAsync` :63, `RecoverAsync`
  :123, `RetileAsync` :168) and the four-parameter overload's delegating call. Commit
  `refactor(3027): the layout client's recovery helpers take the desired wall and revision address`.
  Depends on T001.

- [x] **T011** [P] [US1] `src/ScenarioSimulator/Seeding/ScenarioSeeder.cs` only (plan §2.3): add
  `SeededAsset`; `SeedReactionsAsync(SeededAsset, HashSet<string>, IReadOnlyDictionary<…>,
  CancellationToken)` opening with `var (scenario, asset, overlay) = seededAsset;`; update the two
  call sites (:185 with `Overlay: null`, :204). Commit
  `refactor(3027): reaction seeding takes the seeded asset`. Depends on T001 (not on T010).

## Phase 4: Verify (orchestrator)

- [x] **T020** [US1] Re-run T001's commands unmodified; quote the summaries. `git diff --stat C0 --
  tests/` empty. Rebuild per spec §1 and `diff before.txt after.txt`: exactly H1-H4 removed, no
  S138/S104/S1541/S134 or other new warning in the closure. Build each of T010's and T011's
  commits on its own (ADR-0087). `dotnet format --verify-no-changes` clean on the two files.
  Attach `git diff c8c3773c -- src/ScenarioSimulator/Seeding/` to the PR. Depends on T010, T011.
  **Measured (done):** ScenarioSimulator closure warnings before 13 (H1 `RecoverAsync`(74,36) 6
  params, H2 `RetileAsync`(138,35) 7, H3 `PublishAsync`(177,36) 5, H4
  `ScenarioSeeder.SeedReactionsAsync`(313,42) 6, H5-H8, 5 ServiceDefaults), after 9 (H5-H8 + the 5
  ServiceDefaults); nothing new (no S104/S138/S1541). Counts: ScenarioSimulator.Tests 340/340;
  LayoutCompositionClient 21/21; ScenarioSeeder|ScenarioStoryCheckTests|CameraRegisteredSimHandler
  79/79; Architecture Boundary|HandlerDeconstruction 55/55. `git diff --stat 2b87e853 -- tests/`
  empty.
- [ ] **T021** PR body uses `Closes #3027` (all four in-scope hits cleared; H5-H8 were out of scope
  by the issue). Check the issue state after merge (a mention rarely auto-closes). Depends on T020.

## Dependencies

T000 → T000a → T001 → {T010, T011} → T020 → T021. T010 and T011 are file-disjoint and
order-independent; they land one after the other on the same branch.

## Size

One new test file (~250 lines, 6-8 facts), two production files (~+30 / ~+8 lines). Roughly
90-120 minutes, matching the issue's estimate; the counterfactual round is the largest share.
