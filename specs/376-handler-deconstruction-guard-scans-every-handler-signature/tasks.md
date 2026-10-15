# Tasks 376: The handler-deconstruction guard scans every handler signature

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2984 (feature-level;
no per-task issues).

**Phase 4a colour: RED** (the guard's behaviour changes; production code does not). T001 is a
test-only seam, observed **green** (all 11 existing facts, assertions verbatim). T002's cases marked
red in plan §4 must be **observed failing** against T001's state — a case arriving green there is a
test defect, fixed in the test, not a shortcut. The controls (existing facts, no-body rows) must be
observed green at the same point. Verbatim output quoted in the PR body.

**Engineer:** backend-engineer (T010). **Test-writer:** T001, T002. **Security-sensitive:** no.
Phase 6: `/code-review` only.

**Forbidden in every task:** any file under `src/`; any existing fact's assertions; `Verify`'s
failure-message texts; `Deconstruction`/`Resolve`/`ReadSources`; ADRs, the constitution,
**CLAUDE.md**; suppressions, `.editorconfig`, csproj files. **T010 may not touch
`HandlerDeconstructionTests.cs`.**

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No `[P]`
here: two files, strictly sequential. No foundational task (nothing touches Shared.Kernel,
Shared.Contracts or AppHost).

---

## Phase 1: Baseline (orchestrator; no files)

- [x] **T000** Record for the PR body, on `6acde2b9`:
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"` →
  `Passed: 11`. Captured by the architect 2026-10-15. The architect's throwaway-script figures (105 →
  137 signatures; 85 → 86 checked; 40 parenthesised; 4 block-bodied + 28 expression-bodied newly
  matched; 0 real transpositions; 0 ambiguous resolutions) are in spec §1 and are re-observed by
  T011, not trusted.

## Phase 2: Tests (test-writer; blocks Phase 3)

- [x] **T001** [US1] Seam (characterisation). In `HandlerDeconstructionScanner.cs` only: add
  `IReadOnlyList<string> CheckedSources` to `HandlerDeconstructionScan` (append `path` in
  `CheckBody` beside `state.Checked++`). Nothing else. Run the filter command: observed **green**,
  `Passed: 11`. Commit: `test(2984): expose the sources of checked handler deconstructions`.

- [x] **T002** [US1] Red cases, in the existing `HandlerDeconstructionTests` class (no new class):
  the three theories and one fact of plan §4, exactly as tabled; a `ClassWith(members)` fixture
  helper; `/` paths; assert on `Scan`'s output only. Run:

  ```sh
  dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"
  ```

  Expected on T001's state: **11 failed** (5 transposition rows + 5 correct-binding rows +
  `Real_tree_non_async_handlers_are_scanned`), **13 passed** (11 existing + 2 no-body rows). Return
  the verbatim output; any other split is a test defect to fix before handing over. Commit:
  `test(2984): pin non-async, ValueTask, single-parameter and static handlers escaping the guard (red)`.

## Phase 3: Implementation (backend-engineer; after T002)

- [x] **T010** [US1] `HandlerDeconstructionScanner.cs` only — plan §3 steps 1-2: widen
  `HandlerSignature` (optional `static`, optional `async`, `Task|ValueTask`, first parameter
  followed by `,` or `)`); skip a signature whose parameter list is not followed by `{`.
  **Counterfactual first (SC-003, not committed):** apply the signature widening alone and run the
  filter — the two no-body rows must be observed **red**; quote that output. Then add the block-body
  rule. Done when the filter shows **24 passed, 0 failed** and
  `git diff --stat HEAD~1 -- tests/Architecture.Tests/HandlerDeconstructionTests.cs` is empty.
  Commit: `test(architecture): handler deconstruction guard scans non-async, ValueTask, single-parameter and static handlers`.

## Phase 4: Verification (orchestrator / phase 5)

- [x] **T011** Run and quote:
  `dotnet test tests/Architecture.Tests -c Release` (whole project green);
  `dotnet build tests/Architecture.Tests -c Release` (0 warnings-as-errors);
  `git diff --stat origin/develop` (only `tests/Architecture.Tests/` and this spec dir).
  Re-observe the real-tree figures with a temporary diagnostic (not committed): expect
  `Checked == 86`, `CheckedParenthesised == 40`, `Failures` empty, and
  `src/SystemVariables/Application/EventHandlers/OverlayRevisionPublishedV3Handler.cs` the only
  `CheckedSources` entry absent on `6acde2b9`. A different figure is reported, not adjusted.
  No real-tree transposition counterfactual: a swap in a production handler is a production edit;
  T002's fixture rows are the counterfactual, kept permanently.

## Dependencies

T000 → T001 → T002 → T010 → T011. Each commit builds on its own; T002's commit leaves the filter red
until T010 lands (the accepted phase-4a red/green pair, CLAUDE.md "Builds is not passes").

## As executed

Commit subjects differ from the drafts above, and one task was folded:

- T001 landed as `test(2984): the scan reports which files it checked`.
- T002 landed as `test(2984): pin the handler signatures the guard does not reach (red)`; its format
  and test-name fixes were folded into that commit after review (the test is `Real_tree_non_async_handler_is_scanned`).
- T010 landed as `test(architecture): the handler-deconstruction guard reaches non-async, ValueTask, single-parameter and static handlers`.
- Review follow-ups: `test(2984): a comment between the signature and its body does not hide the handler`
  (red), then `test(architecture): only a semicolon or => ends a handler signature without a block body`.
  FR-002 was tightened accordingly: only `;` or `=>` skips a signature, and string literals are
  honoured when balancing the parameter list.
- T011 was run by the orchestrator; figures: Checked 86, CheckedParenthesised 40, failures 0.
