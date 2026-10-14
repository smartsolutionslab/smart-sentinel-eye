# Tasks 358: Four SystemVariables defects

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2935 (feature-level; no
per-task issues).

**Phase 4a colour: RED** (behaviour-changing). Each red test must be observed **failing** before its
fix exists and pass **unmodified** after. A test arriving green is a phase-4 failure.

**Engineer:** backend-engineer. **Test-writer:** T002.

**Commits.** Conventional Commits, no `Co-Authored-By` (ADR-0086), never `--no-verify`. One red
commit per defect (T002, four commits: `test(system-variables): …`), then one fix commit per defect
(T003–T006, `fix(system-variables): …`) in the same order — the issue asks for one commit per defect.
Each commit builds on its own; a red commit leaves its suite failing until its fix lands (accepted per
CLAUDE.md "Builds is not passes").

**Forbidden:** any file outside the four source files and three test files named here plus this spec
directory; `Log.cs` (no new message), `IVariableValueRequestDedupStore`, the dedup store or its
migrations, `Shared.Contracts`, any ADR or the constitution; editing any test other than those named
in T002; changing `AdvanceAsync(affectedOverlays)` (spec §2, out of scope).

Legend: `[P]` = disjoint files. Single slice, all `[US1]`. Nothing fans out: D1 and D2 share
`VariableArchivedDomainEventHandler.cs` and `ArchivedFanOutCharacterisationTests.cs`, and every fix
depends on its red test.

## Phase 1: Baseline (orchestrator)

- [ ] **T001** [US1] Re-check spec number 358 against `origin/develop` and every remote head
  (357 was the highest, on `origin/test/2922-hook-scope-guards`). Run
  `dotnet test tests/SystemVariables.Application.Tests` and
  `dotnet test tests/SystemVariables.Infrastructure.Tests` green on the untouched branch.

## Phase 2: Red tests (test-writer; blocks Phase 3)

- [ ] **T002** [US1] Replace the six spec-349 pins and add one test, exactly as plan §4:
  - `tests/SystemVariables.Application.Tests/EventHandlers/ValueChangedFanOutCharacterisationTests.cs`:
    `…_the_log_counts_the_skipped_one` → `The_versions_are_advanced_once_for_both_listed_overlays_and_the_log_counts_only_the_pushed_one`.
  - `tests/SystemVariables.Application.Tests/EventHandlers/ArchivedFanOutCharacterisationTests.cs`:
    `The_versions_are_advanced_once_and_the_log_counts_the_overlay_that_was_skipped` →
    `The_versions_are_advanced_once_and_the_log_counts_only_the_pushed_overlay`;
    `With_no_referencing_overlay_only_the_V1_is_published_and_nothing_is_logged` →
    `With_no_referencing_overlay_only_the_V1_is_published_and_the_debug_line_is_logged`.
  - `tests/SystemVariables.Application.Tests/EventHandlers/ValueRequestRefusalOrderCharacterisationTests.cs`:
    `An_invalid_name_has_already_consumed_its_reservation` → `An_invalid_name_is_dropped_before_any_reservation`;
    `A_duplicate_with_an_invalid_name_is_reported_as_a_duplicate_not_as_an_invalid_name` →
    `An_invalid_name_is_reported_as_invalid_even_when_its_key_would_be_a_duplicate`.
  - `tests/SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederBranchCharacterisationTests.cs`:
    `A_non_guid_identifier_aborts_the_seed_after_the_overlays_before_it` →
    `A_non_guid_identifier_is_skipped_and_the_overlays_after_it_are_still_seeded`; add
    `A_non_string_identifier_is_skipped_like_a_non_guid_one`.
  - Rewrite each class summary's "Spec 349 section 6 oddity (x) is pinned as it is" paragraph to say
    spec 358 fixed it.
  Four commits, one per defect (D1 = both count tests; D2; D3 = both; D4 = both). Return the
  **verbatim** test output showing exactly these eight tests failing for the reasons in plan §4 and
  every other test in the two projects passing.

## Phase 3: Fixes (backend-engineer; sequential, each depends on T002)

- [ ] **T003** [US1] D1 — count published V3s in `VariableValueChangedDomainEventHandler.cs` and
  `VariableArchivedDomainEventHandler.cs` (plan §2 D1). Both D1 tests green unmodified.
- [ ] **T004** [US1] D2 — log `NoOverlaysReferenceVariable` on the archive handler's empty branch
  (plan §2 D2). Depends on T003 (same file). D2 test green unmodified.
- [ ] **T005** [US1] D3 — parse the name before `TryReserveAsync` in
  `SystemVariableValueRequestedV1Handler.cs` (plan §2 D3). Both D3 tests green unmodified; every
  other refusal-order test unmodified and green.
- [ ] **T006** [US1] D4 — string-kind + `TryGetGuid` guard in `ReverseIndexSeederHostedService.SeedOverlays`
  (plan §2 D4). Both D4 tests green unmodified.

After T006: `dotnet build -c Release` clean (incl. IDE0007/IDE0005), `dotnet format --verify-no-changes`
clean.

## Phase 5: Verify

- [ ] **T007** [US1] Full `SystemVariables.Application.Tests` and `SystemVariables.Infrastructure.Tests`
  green; coverage gates hold (ADR-0065). Verification note: unit-level proof only (plan §5) — state it,
  and quote the red output from T002 in the PR body (ADR-0139).
