# Tasks 399: the OverlayDesigner and SystemVariables route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2973 (feature-level; no per-task
issues; already on Project #13).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 runs the existing covering
tests on unchanged code (`f950cb7c`) and its output is quoted verbatim in the PR. The same command must pass
after T010 and after T011 with `git diff f950cb7c -- tests/` empty. An assertion that would have to change
is a **block**, not an edit (ADR-0144). Both contexts have a route-table baseline, so no test is written.

**Engineer:** backend-engineer (T010, T011). **Test-writer:** T001, T002 (runs and measures only; writes no
test). Not security-sensitive: Phase 6 is `/code-review`.

**Forbidden in every task:** any file under `tests/`; any handler signature, `*Services` record, Map call or
metadata; any edit to a moved member's text; the non-hit handlers; any helper attribute; `.editorconfig`,
`SonarLint.xml`, `Directory.Build.props`, csproj files; ADRs, the constitution, CLAUDE.md; any suppression
other than the ten in spec §6.

Legend: `[P]` = owns disjoint files (ADR-0109). No foundational task. One branch, one index: T010 and T011
are sequential commits by one engineer; the two contexts' attribute edits are disjoint files but land in
one commit, so `[P]` buys nothing.

## Phase 4a - characterisation (before)

- [ ] **T001 [US1]** Run on unchanged code, capture verbatim:
  ```sh
  dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~OverlayDesignerRouteTableCharacterisationTests|FullyQualifiedName~SystemVariablesRouteTableCharacterisationTests|FullyQualifiedName~OverlayDraftEndpointCharacterisationTests|FullyQualifiedName~SystemVariableDefineEndpointCharacterisationTests|FullyQualifiedName~SourceScanCharacterisationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests"
  ```
  All green expected. Red on unchanged code is a pre-existing defect: stop and report.
- [ ] **T002 [US1]** Before-measurement: spec §1's two builds and extraction. Expect 10 S107 in the two Api
  projects plus the 6 + 8 other lines. Save both sorted lists in the session scratchpad (never the
  worktree). One build at a time.

## Phase 4b - implement

- [ ] **T010 [US1]** (backend-engineer; spec D-1) Create `src/SystemVariables/Api/SystemVariableEndpoints.Writes.cs`
  (`namespace SmartSentinelEye.SystemVariables.Api;` + `public static partial class SystemVariableEndpoints`)
  and move lines 173-379 of `SystemVariableEndpoints.cs` into it **verbatim** (`Define` through
  `ResolveWriteFabAsync`). Usings follow use; IDE0005 clean in both files. Build; re-run T001's command
  green; commit `refactor(system-variables): move the variable write handlers to SystemVariableEndpoints.Writes`.
  Depends on T001, T002.
- [ ] **T011 [US1]** (backend-engineer) Add spec §6's attribute verbatim above H1-H10 (OverlayEndpoints.Commands.cs
  x5, SystemVariableEndpoints.Reads.cs x2, SystemVariableEndpoints.Writes.cs x3) and
  `using System.Diagnostics.CodeAnalysis;` in sorted position in those three files. Nothing else. Commit
  `chore(2973): suppress S107 on the ten OverlayDesigner and SystemVariables route handlers per ADR-0084`.
  Depends on T010.

## Phase 5 - verify

- [ ] **T020 [US1]** Re-run T002: S107 in the two Api projects = 0; the other lists `diff` empty; no S104
  (SC-1, SC-2). `grep -rc "S107:Methods should not have too many parameters" src/OverlayDesigner/Api src/SystemVariables/Api`
  sums to 10 with 5/2/3 per file (SC-4). `grep -c SuppressMessageAttribute` on both Api DLLs = 0.
  `git show --color-moved=plain <T010 sha>` shows a pure move (SC-5).
- [ ] **T021 [US1]** Re-run T001 unchanged; all green; `git diff f950cb7c -- tests/` empty (SC-3). Quote
  T001 and T021 outputs in the PR body.
- [ ] **T022** `dotnet format whitespace` / `style --verify-no-changes` on both Api projects; IDE0005 clean.

## Phase 6-7

- [ ] **T030** `/code-review`: three attribute files plus the move; no helper attributed; move is pure.
- [ ] **T031** PR to `develop` (`--base develop`) with `Closes #2973`; body quotes T001/T021 and T020's
  before/after lists, and records D-1. Confirm #2973 is on Project #13.

## Dependencies

T001, T002 -> T010 -> T011 -> T020, T021, T022 -> T030 -> T031.
