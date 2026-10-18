# Tasks 401: the CameraCatalog and Identity route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2975 (feature-level; already on
Project #13; no per-task issues).

**No decision gate** (spec §6). Phase 4 may start once phases 1-3 are reviewed.

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 runs the existing
covering tests on unchanged code (`7f05b12b`) and its output is quoted verbatim in the PR. The same command
must pass after T011 with no `tests/` file modified. An assertion that would have to change is a **block**,
not an edit (ADR-0144).

**Roles:** test-writer T001-T002 (run and measure only; writes no file). backend-engineer T010-T011.
Phase 6: `/code-review` and `security-reviewer`.

**Forbidden in every task:** editing or adding any file under `tests/`; any handler signature, `*Services`
record, `Map*` call or metadata; attributes on `Retire`, on any Identity `Disable`/`List`, or on any helper;
moving code between files; `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`, csproj files; ADRs,
the constitution, CLAUDE.md; any suppression other than the seven in spec §1.

Legend: `[P]` = owns disjoint files (ADR-0109). No foundational task. One branch, one index: the `[P]`
markers say the files are disjoint, not that two agents should commit concurrently.

## Phase 4a — characterisation (before, on `7f05b12b`)

- [ ] **T001 [US1]** Run the existing covering tests on unchanged code, capture verbatim:
  ```sh
  dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~CameraCatalogRouteTableCharacterisationTests|FullyQualifiedName~IdentityRouteTableCharacterisationTests|FullyQualifiedName~CameraRegisterEndpointCharacterisationTests|FullyQualifiedName~CameraPatchEndpointCharacterisationTests|FullyQualifiedName~DeviceAndKioskEndpointCharacterisationTests|FullyQualifiedName~WebhookRotationEndpointCharacterisationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~EventMetadataFabDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests"
  ```
  All green expected. Red on unchanged code is a pre-existing defect: stop and report.
- [ ] **T002 [US1]** Before-measurement (spec §1 commands, one closure at a time, native `C:/` logfile path
  in the scratchpad, `MSYS_NO_PATHCONV=1`): 4 S107 under `src/CameraCatalog/Api`, 3 under `src/Identity/Api`,
  and the two remainders of spec §1 (6 and 7 lines). Save the two sorted lists for T020.

## Phase 4b — implement (backend-engineer)

- [ ] **T010 [P] [US1]** Add the spec §5 attribute verbatim above `Register`, `Patch`
  (`src/CameraCatalog/Api/CameraEndpoints.cs`) and `Get`, `List`
  (`src/CameraCatalog/Api/CameraEndpoints.Reads.cs`), plus `using System.Diagnostics.CodeAnalysis;` as line 1
  of each. Not `Retire`. Depends on T001, T002.
- [ ] **T011 [P] [US1]** Add the spec §5 attribute verbatim above `Register`
  (`src/Identity/Api/DevicesEndpoints.cs`), `Enroll` (`KiosksEndpoints.cs`) and `Rotate`
  (`WebhookRotationEndpoints.cs`), plus the `using` as line 1 of each. Not `Disable`, not `List`. Depends on
  T001, T002. T010 and T011 land in one commit (`chore(2975): ...`).

## Phase 5 — verify

- [ ] **T020 [US1]** Re-run T002's commands: S107 under both Api projects = 0; remainders `diff` empty
  against T002's; no `warning S104` in either log (SC-1, SC-2). `git grep -c "S107:Methods should not have too
  many parameters"` gives 2 + 2 over the CameraCatalog files and 1 + 1 + 1 over the Identity files (SC-4).
  `grep -c -a SuppressMessageAttribute` on both built Api DLLs = 0. Re-count code lines of
  `CameraEndpoints.cs`: 293.
- [ ] **T021 [US1]** Re-run T001's command unchanged; all green; `git diff 7f05b12b -- tests/` empty (SC-3).
  Quote T001 and T021 outputs in the PR.
- [ ] **T022** `dotnet format whitespace` and `dotnet format style --verify-no-changes --diagnostics IDE0005`
  on both Api projects.

## Phase 6-7

- [ ] **T030** `/code-review` on the diff (five `src/` files): confirm the seven attribute blocks are
  byte-identical to `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs`'s, the `using` is line 1
  everywhere, and nothing else changed (`git diff --stat`: 5 files, 40 insertions (7 x 5 + 5), 0 deletions; the probe's diff had exactly this shape).
- [ ] **T031** `security-reviewer` on the same diff (Identity; fab resolution in both): confirm attributes
  and usings only, no helper exempted, both baselines' `authorize policy=` lines unchanged by T021's green.
- [ ] **T032** PR to `develop` (`--base develop`) with `Closes #2975` (spec §6); body quotes T001/T021 and
  T020's before/after lists. Issue already on Project #13.

## Dependencies

T001, T002 -> T010, T011 (parallel: disjoint files) -> T020, T021, T022 -> T030, T031 -> T032.
