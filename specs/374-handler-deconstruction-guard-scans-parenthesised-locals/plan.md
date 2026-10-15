# Plan 374: The handler-deconstruction guard scans `(var a, var b) = x;`

**Spec**: [spec.md](./spec.md). **Issue**: #2914.

## 1. Context and layers

No bounded context, no layer, no entity, no message. The change lives entirely in
`tests/Architecture.Tests`, which reads `src/**/*.cs` as text. Boundary rules (no cross-context
references) are unaffected: Architecture.Tests already references nothing it scans as source.
Constitution §II, §IV, §IX: not touched. §Testing: phase 4a red (spec header).

## 2. Current shape (`38c35d72`)

`HandlerDeconstructionTests` is one class holding one `[Fact]` and every helper as `private static`:
`HandlerSignature` and `Deconstruction` regexes, `Verify`, `IndexOfField`, `Same`, `Candidates`
(simple-name lookup, cache keyed by type name), `DeconstructOverloads`, `Names`, `SplitTopLevel`,
`BodyAfter`, `Balanced`, `ReadSources` (keys are `Path.GetRelativePath` — **backslashes on
Windows**). The `[Fact]` both reads the tree and scans it, so there is no way to feed it a constructed
source without committing one under `src/`.

## 3. Target shape

Two files, split so that the test-writer and the engineer own disjoint files (ADR-0109; it also makes
"the engineer did not edit the tests" a `git diff --stat` check):

| File | Owner | Contents |
|---|---|---|
| `tests/Architecture.Tests/HandlerDeconstructionScanner.cs` (new, `internal static class`, **not** a test class) | test-writer creates it by **moving** today's helpers verbatim (T001); engineer then changes it (T010) | `Scan(IReadOnlyDictionary<string, string> sources)` returning `HandlerDeconstructionScan(int Checked, int CheckedParenthesised, IReadOnlyList<string> Failures)`; `ReadSources()`; all current private helpers. |
| `tests/Architecture.Tests/HandlerDeconstructionTests.cs` (existing class) | test-writer only | The existing real-tree `[Fact]` (calls `ReadSources` + `Scan`, its two assertions verbatim) and the new fixture facts. |

`CheckedParenthesised` counts checked deconstructions whose matched text starts with `(` rather than
`var`. Under today's regex it is always 0 — that is what lets the test-writer add the real-tree
"parenthesised form is reached" fact before the engineer widens anything, and see it red.

### Engineer's change (T010), all in `HandlerDeconstructionScanner.cs`

1. **Regex** (FR-001/002). Replace `Deconstruction` with an alternation, e.g.

   ```
   (?:var\s*\((?<plain>[^)]*)\)|\((?<paren>\s*(?:var\s+\w+|_)\s*(?:,\s*(?:var\s+\w+|_)\s*)+)\))\s*=\s*(?<target>\w+);
   ```

   `\s` already spans newlines (SearchAudit and EditWallScenes are multi-line). For the `paren`
   group, strip a leading `var\s+` from each element after splitting on `,`; `_` passes through.
   It cannot match `foreach ((var a, var b) in …)` (no `=`) or a swap `(a, b) = (b, a)` (no `var`/`_`,
   and RHS is not `\w+;`). The engineer may choose another pattern; the facts decide.
2. **All matches** (FR-003). Iterate `Matches(body)`, check every one whose `target` is the
   parameter, instead of `Match(body)` + `continue`.
3. **Context-scoped resolution** (FR-004). Context = second segment of the `/`-normalised relative
   path (`src/<Context>/…`). Look up `record T(` among that context's files first; otherwise the
   unique declaration anywhere in `src/`; several outside the context and none inside → a failure
   line `"{path}: record '{T}' is declared in several contexts ({list}) — cannot tell which this
   handler deconstructs."`. Cache key `(context, T)`. Several declarations **inside** one context
   (none exist today) → the same ambiguity failure.
4. **Slash normalising** (FR-005). `ReadSources` keys become `Path.GetRelativePath(...).Replace('\\',
   '/')`; resolution compares on those keys only. The `obj`/`bin` exclusion keeps using
   `Path.DirectorySeparatorChar` on the absolute path, as today.

Nothing else: the failure message texts in `Verify` stay byte-identical (the fixture facts assert on
their parts).

## 4. Fixture facts (test-writer, T002) — what they assert and why they can fail

Each fact builds a source map in code, keyed with `/` paths under `src/<Context>/`, calls `Scan`,
and asserts on the **scanner's output** (counts and failure lines), never on the fixture strings it
just wrote. The record is `public sealed record SampleV1(string Kind, string Device);` in
`src/Alpha/Contracts/SampleV1.cs`; handlers are a minimal
`public sealed class H { public async Task Handle(SampleV1 message, CancellationToken cancellationToken) { <body> } }`.

| Fact (sentence-style, ADR-0053) | Body / sources | Assertion | Today |
|---|---|---|---|
| `A_transposition_in_parenthesised_locals_is_reported` | `(var device, var kind) = message;` | `Checked == 1`; 2 failures; one contains `position 0 is 'Kind'` and `'device'`, the other `position 1 is 'Device'` and `'kind'` | **red** (Checked 0) |
| `A_transposition_beside_a_discard_is_reported` | `(var device, _) = message;` | `Checked == 1`; exactly 1 failure, `position 0` | **red** |
| `Correct_parenthesised_locals_are_checked_and_pass` | `(var kind, var device) = message;` | `Checked == 1`, `CheckedParenthesised == 1`, 0 failures | **red** |
| `A_parenthesised_deconstruction_spanning_lines_is_checked` | `(var device,\n var kind) = message;` | `Checked == 1`; 2 failures | **red** |
| `A_transposition_with_the_var_prefix_is_reported` (control) | `var (device, kind) = message;` | `Checked == 1`; 2 failures | green — proves the fixture is well-formed |
| `A_handler_is_checked_against_its_own_contexts_record` | Alpha: `ArchiveCommand(string Kind, string Device)`; Beta: `ArchiveCommand(string Device, string Kind)`; Beta handler `var (device, kind) = message;` — Alpha's file inserted first | 0 failures, `Checked == 1` | **red** (resolves Alpha) |
| `A_same_named_record_elsewhere_does_not_mask_a_transposition` | as above, Beta handler `var (kind, device) = message;` | 2 failures | **red** (passes against Alpha) |
| `A_record_declared_once_outside_the_context_still_resolves` | record in `src/Shared.Contracts/…`, handler in `src/Automation/…`, transposed `var (device, kind)` | 2 failures | green (control) |
| `A_record_name_declared_in_several_other_contexts_is_reported_as_ambiguous` | record in Alpha and Beta, handler in Gamma | 1 failure containing `several contexts` | **red** |
| `Real_tree_handlers_in_the_parenthesised_spelling_are_scanned` | `ReadSources()` | `CheckedParenthesised > 0` | **red** |
| existing `Handler_deconstructions_bind_their_records_fields_in_order` | `ReadSources()` | verbatim | green before and after |

The `var (…)` fixtures in the two same-name facts are deliberate: they isolate the resolution defect
from the regex widening, so each red has exactly one cause. Insertion order into the
`Dictionary<string,string>` is deterministic (Alpha first), so the "today" column is reproducible.

Assertion on message *parts* (`position 0 is 'Kind'`, `'device'`) rather than the whole line keeps the
facts robust to the path prefix while still being produced by `Verify`, not by the fixture.

## 5. Alternatives rejected

- **Rewrite the 40 handlers to `var (…)`.** Ruled out by the owner decision; also a 40-file production
  diff to fix a test.
- **A Roslyn-based scan** (syntax tree instead of regex). Correct in principle and would remove the
  regex class of hole entirely, but a new dependency and a rewrite of a working guard — speculative
  for one missing spelling (ADR-0036).
- **Counterfactual by a scratch file under `src/`, deleted after.** Leaves no trace a reviewer can
  re-run; the fixture facts make the counterfactual permanent.
- **Resolve by namespace / `using` directives.** More exact, much more parsing; the context directory
  is what actually disambiguates every collision in the tree (5 pairs, all LayoutComposition vs
  OverlayDesigner).

## 6. Measurement commands

```sh
# the guard (before and after)
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"
# whole project, after
dotnet test tests/Architecture.Tests -c Release
# release build of the project (IDE0007/IDE0305/collection-expression analyzers)
dotnet build tests/Architecture.Tests -c Release
# scope check
git diff --stat origin/develop
```

## 7. Constitution / ADR check

| Item | Status |
|---|---|
| Every spec cites ≥ 1 ADR | ADR-0144, 0036, 0040, 0073, 0119 |
| New ADR | **No.** Implements CLAUDE.md's existing enforced rule; decides nothing. |
| Gate weakening (ADR-0144 forbidden list) | None — the guard gets stricter; no suppression, no deleted assertion. |
| Production code | Unchanged (FR-006). |
| Shard filters | N/A — no new test class in a sharded project; `HandlerDeconstructionScanner` is not a test class. |
| Linux CI path separators | FR-005; fixture keys use `/` so a Windows-only pass cannot hide a Linux red. |
| Code metrics (ADR-0084, advisory) | `Scan` should stay ≤ 30 lines; split resolution into its own method. |
