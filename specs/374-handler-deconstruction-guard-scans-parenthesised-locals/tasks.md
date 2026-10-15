# Tasks 374: The handler-deconstruction guard scans `(var a, var b) = x;`

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2914 (feature-level;
no per-task issues).

**Phase 4a colour: RED** (the guard's behaviour changes; production code does not). T001 is a
test-only move, observed **green** before and after (the existing fact, assertions verbatim). T002's
facts marked red in plan §4 must be **observed failing** against T001's state — a fact arriving green
there is a test defect, fixed in the test, not a shortcut. The two controls must be observed green at
the same point. The verbatim failing output is quoted in the PR body.

**Engineer:** backend-engineer (T010). **Test-writer:** T001, T002. **Security-sensitive:** no.
Phase 6: `/code-review` only.

**Forbidden in every task:** any file under `src/` (no handler is rewritten — owner decision); any
change to the two assertions of `Handler_deconstructions_bind_their_records_fields_in_order`; any
`Verify` failure-message text; ADRs, the constitution, CLAUDE.md; suppressions, `.editorconfig`,
csproj files; shard filters. **T010 may not touch `HandlerDeconstructionTests.cs`.**

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No `[P]`
here: two files, strictly sequential. No foundational task (nothing touches Shared.Kernel,
Shared.Contracts or AppHost).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record for the PR body, on `38c35d72`:
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"` →
  `Passed: 1`. Captured by the architect 2026-10-15. The architect's throwaway-script figures (45
  checked today; 85 after widening, 40 parenthesised; 0 real transpositions; 5 false failures from
  same-name resolution without FR-004) are in spec §1 and are re-observed by T011, not trusted.

## Phase 2: Tests (test-writer; blocks Phase 3)

- [ ] **T001** [US1] Characterisation move. Create `tests/Architecture.Tests/HandlerDeconstructionScanner.cs`
  (`internal static class`, not a test class) and move into it, **verbatim**, every private helper
  and both regexes from `HandlerDeconstructionTests.cs`. Add
  `internal static HandlerDeconstructionScan Scan(IReadOnlyDictionary<string, string> sources)` holding
  today's loop body, and `internal sealed record HandlerDeconstructionScan(int Checked, int
  CheckedParenthesised, IReadOnlyList<string> Failures)` — `CheckedParenthesised` counts checked
  matches whose text starts with `(` (always 0 today). Make `ReadSources` `internal`. **Do not**
  widen the regex, change resolution, or normalise slashes — that is T010. The existing fact becomes
  `var scan = HandlerDeconstructionScanner.Scan(HandlerDeconstructionScanner.ReadSources());` plus its
  two assertions verbatim (reading `scan.Checked` / `scan.Failures`). Run the filter command: observed
  **green**, `Passed: 1`. Commit: `test(architecture): lift the handler-deconstruction scan out of its fact`.

- [ ] **T002** [US1] Red facts, in the existing `HandlerDeconstructionTests` class (no new class).
  Add the ten facts of plan §4, exactly as tabled (fixture paths `src/<Context>/…` with `/`; assert on
  `Scan`'s output, never on the fixture text; message parts like `position 0 is 'Kind'` and
  `'device'`). Run:

  ```sh
  dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"
  ```

  Expected on T001's state: **8 failed** (the 7 fixture facts marked red + the real-tree
  parenthesised fact), **3 passed** (the two controls + the existing real-tree fact). Return the
  verbatim output; any other split is a test defect to fix before handing over. Commit:
  `test(architecture): handlers deconstructing as (var a, var b) and same-named records are checked`.

## Phase 3: Implementation (backend-engineer; after T002)

- [ ] **T010** [US1] `tests/Architecture.Tests/HandlerDeconstructionScanner.cs` only — plan §3 steps
  1-4: widen the deconstruction regex to both spellings (with `_`, across lines); check every match of
  the parameter, not the first; resolve records context-first, unique-elsewhere second, ambiguity a
  failure, cache keyed by `(context, name)`; normalise `ReadSources` keys to `/`. Keep `Verify`'s
  messages byte-identical; keep `Scan` under 30 lines (ADR-0084, advisory) by extracting resolution.
  Done when the filter command shows **11 passed, 0 failed** and
  `git diff --stat HEAD~1 -- tests/Architecture.Tests/HandlerDeconstructionTests.cs` is empty.
  Commit: `test(architecture): handler-deconstruction guard scans parenthesised locals and resolves records per context`.

## Phase 4: Verification (orchestrator / phase 5)

- [ ] **T011** Run and quote:
  `dotnet test tests/Architecture.Tests -c Release` (whole project green);
  `dotnet build tests/Architecture.Tests -c Release` (0 warnings-as-errors);
  `git diff --stat origin/develop` (only `tests/Architecture.Tests/` and this spec dir).
  Re-observe the real-tree figures with a temporary diagnostic (not committed — e.g. a local
  `ITestOutputHelper` line or a debugger read of `scan`): expect `Checked == 85`,
  `CheckedParenthesised == 40`, `Failures` empty. A different count is reported, not adjusted.
No separate real-tree counterfactual task: a swap in a production handler only compiles where the
swapped fields share a type, and the test project builds `src/`, so it would be a production edit in
all but name. T002's fixture facts are the counterfactual, kept permanently; the real-tree
`CheckedParenthesised > 0` fact proves the widened scan actually reaches `src/`.

## Dependencies

T000 → T001 → T002 → T010 → T011. Each commit builds on its own; T002's commit leaves the
filter red until T010 lands (the accepted phase-4a red/green pair, CLAUDE.md "Builds is not passes").
