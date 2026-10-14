# Tasks 354: The AppHost composition root under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2898 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). C1 (T002) and C2
(T003) must be seen **passing on the unrefactored code** before any Phase 3 task starts, and must
pass **unmodified** after every refactor commit. An assertion or snapshot line that has to be
edited after a refactor commit is evidence the composition moved: revert the production change, do
not adjust the test. A characterisation test that arrives red before any production edit is a test
defect and is fixed in the test.

**Engineer:** infra-engineer. **Test-writer:** T002-T003.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each commit
must build **and pass** on its own: run plan §6 steps 2-4 after every refactor commit.

**Forbidden in every task:** any existing test file (in particular `ContainerImagePinTests`,
`FoundingDecisionRecordTests`, every `Integration.Tests/AppHost*Tests.cs`, `RealmImportMirrorTests*`,
`WhepAuthorize*Tests`, `AspireFixture`); any shared reader helper; moving a line that contains
`AddContainer(`, `AddDockerfile(`, `WithImage(`, `WithImageTag(`, `WithImageRegistry(` or
`bluenviron/mediamtx:`; moving any resource declaration other than the web applications';
reordering, merging or deduplicating builder calls; C1 snapshot edits after T002; suppressions,
thresholds, `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`, agent briefs;
`StackStatusReport.cs`, `OverridableParameters.cs`, `Realms/`, `Resources/`, `mosquitto/`.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index). Every Phase 3 task edits
`AppHost.cs` (a contention file, ADR-0109), so **Phase 3 is strictly sequential** — no `[P]`.

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T001** Record verbatim for the PR body, on the unrefactored tree: plan §4 build output (3
  lines), plan §5 guarded-token output, and a passing run of `ContainerImagePinTests` and
  `FoundingDecisionRecordTests`. Re-check the spec number (354) against `origin/develop` and every
  remote head.

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T002** [P] [US1] C1, `tests/Integration.Tests/AppHostCompositionCharacterisationTests.cs`,
  `tests/Integration.Tests/AppHostComposition/*.snapshot.txt` (copy-to-output in the test csproj only
  if the project does not already glob it), one line appended to one
  `tests/Integration.Tests/ci-shards/shard-N.filter`. Observe green twice on unrefactored code, then
  the three plan §3 counterfactuals red (output quoted), reverted. Commit
  `test(apphost): pin the composed model of every lane before the split`.
- [ ] **T003** [P] [US1] C2, `tests/Architecture.Tests/AppHostExtractionBoundaryTests.cs`. Observe
  green on unrefactored code, then both counterfactuals red (output quoted), reverted. Commit
  `test(apphost): keep what the source guards read inside AppHost.cs`.

## Phase 3: Extraction (infra-engineer; sequential; each commit green on its own)

- [ ] **T004** [US1] `src/AppHost/StackLanes.cs` (new) + `AppHost.cs` lines 20-38 -> one
  deconstruction. Commit `refactor(apphost): read the stack lanes in one place`.
- [ ] **T005** [US1] `src/AppHost/DeveloperLanePersistence.cs` (new) + the seven
  `if (isPersistentDevStack)` blocks in `AppHost.cs` -> one call each at the same position.
  Depends on T004. Commit `refactor(apphost): extract developer-lane persistence`.
- [ ] **T006** [US1] `src/AppHost/RunLaneOverrides.cs` (new) + blocks 291, 536, 782 (original
  numbering). Depends on T005. Commit `refactor(apphost): extract the run-lane overrides`.
- [ ] **T007** [US1] `src/AppHost/IntegrationLaneOverrides.cs` (new) + blocks 521, 686. Depends on
  T006. Commit `refactor(apphost): extract the integration-lane overrides`.
- [ ] **T008** [US1] `src/AppHost/ServiceDependencies.cs` (new) + loops 643, 677 (array literals stay
  at the call sites). Depends on T007. Commit
  `refactor(apphost): extract revocation-list wiring and the migration gate`.
- [ ] **T009** [US1] `src/AppHost/WebApplications.cs` (new) + block 792-910 (the `if` stays around
  one call). Depends on T008. Commit `refactor(apphost): extract the web applications`.

After **each** of T004-T009: plan §6 steps 2-4 green, plan §5 output unchanged, plan §4 shows no new
hit in the file just added.

## Phase 4: Verify (orchestrator / verify skill)

- [ ] **T010** Plan §6 in full on the tip. Record for the PR body: §4 build before/after (expected:
  S104 and S1541 gone from `AppHost.cs`; only `StackStatusReport.cs` S107 remains, out of scope);
  §5 diff empty; `git diff origin/develop --stat -- tests/` = T002/T003 files + one shard line;
  C1/C2 counterfactual outputs from T002/T003. If S104 or S1541 remains, state the figure and why
  (spec FR-008) — do not move a guarded block to get further.

## Dependencies

T001 -> {T002, T003} (parallel, disjoint files) -> T004 -> T005 -> T006 -> T007 -> T008 -> T009 ->
T010. Nothing foundational in `Shared.Kernel`/`Shared.Contracts`; no Aspire resource is added.
