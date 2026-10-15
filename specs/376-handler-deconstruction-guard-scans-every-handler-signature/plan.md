# Plan 376: The handler-deconstruction guard scans every handler signature

**Spec**: [spec.md](./spec.md). **Issue**: #2984.

## 1. Context and layers

No bounded context, no layer, no entity, no message. The change lives entirely in
`tests/Architecture.Tests`, which reads `src/**/*.cs` as text. Boundary rules are unaffected.
Constitution §II, §IV, §IX: not touched. §Testing: phase 4a red (spec header).

## 2. Current shape (`6acde2b9`)

Spec 374 left two files:

- `tests/Architecture.Tests/HandlerDeconstructionScanner.cs` — `internal static class`:
  `HandlerSignature`, `Deconstruction`, `Scan(sources) → HandlerDeconstructionScan(Checked,
  CheckedParenthesised, Failures)`, `CheckBody`, `Verify`, `Resolve` (context-first), `BodyAfter`
  (first `{` after the signature index), `Balanced`, `ReadSources` (`/`-normalised keys).
- `tests/Architecture.Tests/HandlerDeconstructionTests.cs` — 11 facts; fixture helpers
  `HandlerWith(statement, type)` (hard-codes `public async Task Handle(… message, CancellationToken
  cancellationToken)`), `Sources(...)`, `SampleRecord`, `AlphaSample`, `AlphaHandler`.

## 3. Target shape

Same two files, sequential ownership (as spec 374; ADR-0109 — "the engineer did not edit the tests"
stays a `git diff --stat` check):

| File | Owner | Change |
|---|---|---|
| `HandlerDeconstructionScanner.cs` | test-writer (T001: seam only), then engineer (T010) | T001: `HandlerDeconstructionScan` gains `IReadOnlyList<string> CheckedSources`; `CheckBody` appends `path` for each checked deconstruction. T010: signature + block-body rule. |
| `HandlerDeconstructionTests.cs` | test-writer only (T002) | New theories/fact; a fixture helper taking the whole handler member text. |

### Engineer's change (T010), all in `HandlerDeconstructionScanner.cs`

1. **Signature** (FR-001). For example:

   ```
   public\s+(?:static\s+)?(?:async\s+)?(?:Task|ValueTask)\b[^(\n]*?\s(?:Handle|HandleAsync)\s*\(\s*
   ([A-Za-z0-9_]+)\s+(\w+)\s*[,)]
   ```

   `[^(\n]*?` still forbids `(` in the return type, so the first `(` after the match start is the
   parameter list's. Group numbers 1/2 (type, parameter) are unchanged, so `CheckBody` is untouched.
   The engineer may choose another pattern; the facts decide.
2. **Block-body rule** (FR-002). Before `BodyAfter`, find the parameter list's closing `)` (the
   existing `Balanced` from the first `(` at/after `signature.Index`, or an index-returning sibling)
   and skip the signature unless the next non-whitespace character is `{`. This replaces "first `{`
   after the signature" with "the `{` that opens this method's body"; for every signature matched
   today the two coincide (all 105 are block-bodied), so today's 85 checks are unchanged.
3. Nothing else: `Deconstruction`, `Resolve`, `Verify` texts, `ReadSources` stay byte-identical.
   ADR-0084 (advisory): keep `Scan` and any new helper ≤ 30 lines.

## 4. Facts (test-writer, T002) — what they assert and why they can fail

All assert on `Scan`'s output (counts, failure lines, `CheckedSources`), never on the fixture text.
Fixture record and paths as spec 374: `SampleRecord` in `src/Alpha/Contracts/SampleV1.cs`, handler in
`src/Alpha/Handlers/H.cs`. A new helper, e.g. `ClassWith(string members)` →
`"public sealed class H { " + members + " }"`, lets a theory supply the whole signature; the existing
`HandlerWith` stays as it is (its callers are unchanged).

Signature rows (shared `[InlineData]` set, sentence-style names per ADR-0053):

| Row | Signature |
|---|---|
| non-async | `public Task Handle(SampleV1 message, CancellationToken cancellationToken)` |
| non-async result | `public Task<int> HandleAsync(SampleV1 message, CancellationToken cancellationToken)` |
| value task | `public async ValueTask Handle(SampleV1 message, CancellationToken cancellationToken)` |
| single parameter | `public async Task Handle(SampleV1 message)` |
| static | `public static Task Handle(SampleV1 message, CancellationToken cancellationToken)` |

Bodies need not compile (the scanner reads text); keep them plausible:
`{ var (device, kind) = message; return default; }` / `{ var (kind, device) = message; return default; }`.

| Test | Assertion | Today (after T001) |
|---|---|---|
| `[Theory] A_transposition_is_reported_whatever_the_handler_signature(signature)` × 5 | `Checked == 1`; 2 failures; one contains `position 0 is 'Kind'` and `'device'`, the other `position 1 is 'Device'` and `'kind'` | **red** ×5 (Checked 0) |
| `[Theory] A_correct_deconstruction_is_checked_whatever_the_handler_signature(signature)` × 5 | `Checked == 1`; 0 failures | **red** ×5 |
| `[Theory] A_handler_without_a_block_body_does_not_borrow_the_next_members_body(declaration)` × 2: `public Task Handle(SampleV1 message, CancellationToken cancellationToken) => Task.CompletedTask;` and `public Task Handle(SampleV1 message, CancellationToken cancellationToken);`, each followed by `public void Other(SampleV1 message) { var (device, kind) = message; }` | `Checked == 0`; 0 failures | green ×2 (unmatched today); **red under a naive widening** (SC-003) |
| `[Fact] Real_tree_non_async_handlers_are_scanned` | `scan.CheckedSources.ShouldContain("src/SystemVariables/Application/EventHandlers/OverlayRevisionPublishedV3Handler.cs")` | **red** |
| existing 11 facts (incl. control `A_transposition_with_the_var_prefix_is_reported`, `async Task`) | verbatim | green |

Expected after T002 on T001's state: **11 failed** (5 + 5 + 1), **13 passed** (11 existing + 2
no-body rows). After T010: **24 passed**.

The real-tree fact names one path rather than a count: a count would pin the tree, and the issue
names this handler. If the handler is moved or renamed the fact fails loudly with the path in the
message, and the maintainer points it at another non-async handler that deconstructs.

## 5. Alternatives rejected

- **Match any method named `Handle`/`HandleAsync` regardless of modifiers/return type.** Would admit
  private helpers and `void` methods; no handler in `src/` needs it (spec §1, 0 occurrences).
- **Roslyn-based scan.** Rejected in spec 374 §5 for the same reasons; still a rewrite for a regex gap.
- **Treat expression-bodied handlers by scanning the expression.** A deconstruction is a statement;
  an expression body cannot contain the house-rule shape. Skipping is exact, not lossy.
- **Pin `Checked == 86` in the real-tree fact.** Pins the tree; every new handler would break it.
  The figure is recorded as observed (SC-004) instead.

## 6. Measurement commands

```sh
# the guard (before and after)
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"
# whole project, after
dotnet test tests/Architecture.Tests -c Release
# release build of the project (IDE0007 / collection-expression analyzers)
dotnet build tests/Architecture.Tests -c Release
# scope check
git diff --stat origin/develop
```

## 7. Constitution / ADR check

| Item | Status |
|---|---|
| Every spec cites ≥ 1 ADR | ADR-0144, 0139, 0036, 0040, 0073 |
| New ADR | **No.** Implements CLAUDE.md's existing enforced rule; decides nothing. |
| Gate weakening (ADR-0144 forbidden list) | None — the guard gets stricter; no suppression, no deleted assertion. |
| Production code | Unchanged (FR-005). |
| CLAUDE.md | Not edited (issue). |
| Shard filters | N/A — `Architecture.Tests` is not sharded; no new test class. |
| Linux CI path separators | `CheckedSources` uses the existing `/`-normalised keys; fixture keys use `/`. |
| Code metrics (ADR-0084, advisory) | `Scan` ≤ 30 lines; extract the block-body check if needed. |
