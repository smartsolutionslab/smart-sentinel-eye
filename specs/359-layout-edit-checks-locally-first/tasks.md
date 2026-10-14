# Tasks 359: A layout draft edit runs its local refusals before asking CameraCatalog

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2937 (feature-level; no
per-task issues).

**Phase 4a colour: RED** (behaviour-changing). T002's five red tests must be observed **failing**
before T003 exists, and pass **unmodified** after T003. A red test arriving green is a phase-4
failure. The AS-6 `Calls == 1` assertion is a supporting guard and is expected green on both sides.

**Engineer:** backend-engineer. **Test-writer:** T002.

**Commits.** One per task, Conventional Commits, no `Co-Authored-By` (ADR-0086), never `--no-verify`.
The red commit (T002) leaves `LayoutComposition.Application.Tests` failing until T003 lands
(accepted per CLAUDE.md "Builds is not passes"); every commit builds on its own.

**Forbidden:** any file outside the three below plus this spec directory; `EditDraftRevisionError`,
`ICameraFabGuard`, `CameraCatalogFabGuard`, `CreateLayoutDraftCommandHandler`, the endpoint,
`Shared.Contracts`; any ADR or the constitution; editing any test other than those named in T002.

Legend: `[P]` = disjoint files. Single slice, all `[US1]`; strictly sequential (T003 depends on T002's
red), so nothing fans out.

## Phase 1: Baseline (orchestrator)

- [ ] **T001** [US1] Re-check spec number 359 against `origin/develop` and every remote head (358 is
  held by #2935's branch). Run
  `dotnet test tests/LayoutComposition.Application.Tests --filter "FullyQualifiedName~EditDraft|FullyQualifiedName~DraftRefusalOrder|FullyQualifiedName~StaleVersionRejection"`
  green on the untouched branch.

## Phase 2: Red tests (test-writer; blocks Phase 3)

- [ ] **T002** [US1] Per plan §4:
  - `tests/LayoutComposition.Application.Tests/Fakes/FakeCameraFabGuard.cs`: add `Calls` and
    `Unreachable()`.
  - `tests/LayoutComposition.Application.Tests/Commands/DraftRefusalOrderCharacterisationTests.cs`:
    replace `Edit_with_a_camera_outside_the_fab_and_a_stale_version_returns_TileCameraOutsideFab`
    with `Edit_with_a_camera_outside_the_fab_and_a_stale_version_returns_LayoutRevisionStale_without_asking_camera_catalog`;
    add `Edit_with_a_stale_version_never_asks_camera_catalog`,
    `Edit_naming_a_missing_revision_never_asks_camera_catalog`,
    `Edit_of_a_published_revision_never_asks_camera_catalog`,
    `Edit_with_a_stale_version_is_answered_LayoutRevisionStale_while_camera_catalog_is_unreachable`;
    add `guard.Calls.ShouldBe(1)` to
    `Edit_with_a_camera_outside_the_fab_returns_TileCameraOutsideFab_listing_those_cameras`; update
    the class summary.
  Run the T001 filter; return the **verbatim** output showing exactly those five failing for the
  reasons in plan §4 (wrong error type / `Calls` 1 / `HttpRequestException`) and every other test
  passing.

## Phase 3: Fix (backend-engineer; depends on T002)

- [ ] **T003** [US1] In `src/LayoutComposition/Application/Commands/Handlers/EditDraftRevisionCommandHandler.cs`,
  move the `RefuseRevision` block above the `RefuseCrossFabTilesAsync` block (plan §3); nothing else.
  T002's tests pass **unmodified**; every other test in the T001 filter unmodified and green.
  `dotnet build -c Release` clean; `dotnet format --verify-no-changes` clean.

## Phase 5: Verify

- [ ] **T004** [US1] Full `LayoutComposition.Application.Tests` green; Application coverage gate holds
  (ADR-0065). Verification note: unit-level proof only (plan §5) — state it.
