# Tasks 383: OverlayDesigner's shape-element parsing under five parameters

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2973 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T000a first adds the
shape-parsing characterisation tests to `OverlayDraftEndpointCharacterisationTests` (commit `C0`),
observed green on unchanged code; T001 then runs the suite on that code and its output is quoted
verbatim in the PR. The same commands must pass after T010 with `git diff C0 -- tests/` empty. An
assertion that would have to change is a **block**, not an edit (ADR-0144).

**Engineer:** backend-engineer (T010). **Test-writer:** T000a (adds tests, spec §4, D-3; plan §4)
and T001 (runs only). **Not security-sensitive**: Phase 6 runs `/code-review` only.

**Scope is 1 of the issue's 11 hits** (H1). H2-H11 are blocked on the owner decision recorded on
#2971 (spec §6) and **must not be attempted**: each is reachable only by changing a route handler's
parameter list, which edits `OverlayDesignerRouteTableCharacterisationTests` or
`SystemVariablesRouteTableCharacterisationTests`.

**Forbidden in every task after T000a:** any file under `tests/`; any route handler's signature
(H2-H11 and every other `OverlayEndpoints*`/`SystemVariableEndpoints*` handler); any SystemVariables
file; `ElementRequest.cs`; lines 17-26 of `ParseElement` other than the one `ParseShapeElement` call;
`ParseColor`, `ParseKind`, `RequireNoTextFields`, `ParseTextElement`; any body change in
`ParseShapeElement` beyond the signature and one deconstruction line plus its blank line; a new or
renamed file under `src/*/Api`; ADRs, the constitution, CLAUDE.md; suppressions, thresholds,
`.editorconfig`, `SonarLint.xml`, `Directory.Build.props`, csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No
foundational task: nothing touches Shared.Kernel, Shared.Contracts or AppHost. Nothing is `[P]`:
one production file, one test file, strictly ordered by the characterisation rule. Commits land
sequentially (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [x] **T000** On `81f68a62`, run spec §1's Release `--no-incremental` build and record the S107
  list under `src/(OverlayDesigner|SystemVariables)/Api` (the 11 of spec §1; 110 solution-wide) and
  the full Sonar list there (S107 only). Done at Phase 1; re-run by T020 for the after-figure.

## Phase 2: Characterisation, observed green (test-writer)

- [ ] **T000a** [US1] Add plan §4 cases 1-8 to
  `tests/Architecture.Tests/OverlayDraftEndpointCharacterisationTests.cs`, on both routes where the
  plan says so, every numeric value distinct within a request and each component asserted on its
  own. Exact problem bodies as observed on unchanged code. No existing fact or helper changes; no
  new class; no host change. Observe the new tests **and** the existing 22 plus both route-table
  tests green on unchanged production code; run the five plan §4 counterfactuals and report each
  red verbatim (a counterfactual that stays green is fixed in the test before commit). Commit
  `test(overlay-designer): pin shape-element parsing before the helper is reshaped` = `C0`.
  Depends on T000.

- [ ] **T001** [US1] Run every plan §4 "In-process" command (and "Aspire fixture" if the stack is
  free) on `C0`. Capture each summary line verbatim, with non-zero counts. Writes no file. Depends
  on T000a.

## Phase 3: The change (backend-engineer; one commit)

- [ ] **T010** [US1] `src/OverlayDesigner/Api/OverlayElementParsing.cs` only: add the nested
  `private readonly record struct ElementAppearance(NormalizedPosition Position, NormalizedSize Size,
  OverlayColor Color)` with its summary; `ParseShapeElement` takes `(ElementKind kind, ElementRequest
  request, ElementAppearance appearance)` and opens with `var (position, size, color) = appearance;`,
  the rest of its body byte-identical; the call in `ParseElement` passes
  `new ElementAppearance(position, size, color)` (plan §2.1). Remove any redundant `using` in this
  file only. One commit: `refactor(overlay-designer): shape-element parsing takes the element's
  appearance`. Depends on T001.

## Phase 4: Verify (orchestrator)

- [ ] **T020** [US1] Re-run T001's commands unmodified; quote the summaries. `git diff --stat C0 --
  tests/` empty. Release `--no-incremental` build: S107 in scope is exactly H2-H11 (H1 gone), 109
  solution-wide, and no S138, S104, S1541, S134 or other new warning in either Api project. Attach
  `git diff 81f68a62 -- src/OverlayDesigner/Api/OverlayElementParsing.cs` to the PR. Depends on T010.
- [ ] **T021** Comment on #2973: H1 cleared; H2-H11 are route handlers in two contexts whose
  route-table baselines pin their parameter lists (spec D-1), so they wait on the #2971 decision
  (options a-c). The PR uses `Refs #2973`, not a closing keyword; the issue stays open. Depends on
  T020.

## Dependencies

T000 → T000a → T001 → T010 → T020 → T021. Strictly sequential; nothing is `[P]`.
