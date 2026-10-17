# Tasks 398: the LayoutComposition route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2972 (feature-level; no
per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 runs the existing
covering tests on unchanged code (`7d2d6af3`) and its output is quoted verbatim in the PR. The same
commands must pass after T010 with `git diff 7d2d6af3 -- tests/` empty. An assertion that would have to
change is a **block**, not an edit (ADR-0144).

**Engineer:** backend-engineer (T010). **Test-writer:** T001 and T002 (runs and measures only; writes no
test). Not security-sensitive: Phase 6 is `/code-review`.

**Forbidden in every task:** any file under `tests/`; any handler signature, `*Services` record, Map call or
metadata; the non-hit handlers `List` (both) and `BranchDraft`; any helper; `.editorconfig`,
`SonarLint.xml`, `Directory.Build.props`, csproj files; ADRs, the constitution, CLAUDE.md; any
suppression other than the ten in spec §5.

Legend: `[P]` = owns disjoint files (ADR-0109). No foundational task. T010 is one engineer task over four
files; splitting it would buy nothing and the commit lands as one (one branch, one index).

## Phase 4a — characterisation (before)

- [ ] **T001 [US1]** Run the covering tests on unchanged code and capture the output verbatim:
  ```sh
  dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~LayoutCompositionRouteTableCharacterisationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests"
  ```
  All green expected. Any red on unchanged code is a pre-existing defect: stop and report.
- [ ] **T002 [US1]** Record the before-measurement (spec §1 command, scoped to the Api closure): 10 S107 under
  `src/LayoutComposition/Api`, plus the 9-line list of other distinct warnings. Save both sorted lists in the
  scratchpad for T020's diff. Depends on nothing; may run alongside T001 only if the machine has room (one
  build at a time is the safer default here).

## Phase 4b — implement

- [ ] **T010 [US1]** (backend-engineer) Add the spec §5 attribute, verbatim, directly above each of the ten
  handler declarations in spec §1 (H1-H10), and `using System.Diagnostics.CodeAnalysis;` to each of
  `LayoutEndpoints.Commands.cs`, `LayoutEndpoints.Queries.cs`, `WallEndpoints.Commands.cs`,
  `WallEndpoints.Queries.cs`. Nothing else. Depends on T001, T002.

## Phase 5 — verify

- [ ] **T020 [US1]** Re-run T002's command: S107 under `src/LayoutComposition/Api` = 0; the other list
  `diff`s empty against T002's (SC-1, SC-2). `grep -c "S107:Methods should not have too many parameters"`
  over the four files sums to 10 (SC-4). `grep -c SuppressMessageAttribute` on the built Api DLL = 0.
- [ ] **T021 [US1]** Re-run T001's command unchanged; all green; `git diff 7d2d6af3 -- tests/` empty (SC-3).
  Quote both T001 and T021 outputs in the PR body.
- [ ] **T022** `dotnet format whitespace` / `style --verify-no-changes` on the Api project; IDE0005 clean.

## Phase 6-7

- [ ] **T030** `/code-review` on the diff. Check the four files only; confirm no helper got an attribute.
- [ ] **T031** PR to `develop` with `Closes #2972`; body quotes T001/T021 output and T020's before/after
  lists. Confirm the issue is on Project #13 (feature issue; already there).

## Dependencies

T001, T002 → T010 → T020, T021, T022 → T030 → T031.
