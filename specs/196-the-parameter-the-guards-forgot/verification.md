# Verification 196 — The parameter the guards forgot (#2309)

**Latency: N/A.** Domain argument-guard fix; no leg of constitution §IV's
event→overlay path touched.

## Summary

`Layout.CreateDraft`, `Layout.EditDraft`, and `Layout.ValidateGrid` took a
`grid` parameter but never guarded it — the only unguarded reference-type
parameter in the file. A null `grid` produced a raw
`NullReferenceException`, or (on the empty-tiles path) a misleadingly
worded `InvalidOperationException` with a blank interpolation. Three
`Ensure.That(grid).IsNotNull();` lines close the gap.

## Phase 4a — real, naturally-occurring red (ADR-0139), quoted verbatim

```
Failed EditDraft_rejects_a_null_grid_even_with_an_empty_tile_set [80 ms]
  should throw System.ArgumentNullException but threw System.InvalidOperationException
  ---- System.InvalidOperationException : Grid  with 0 tile(s) violates Empty.

Failed ValidateGrid_rejects_a_null_grid [4 ms]
  should throw System.ArgumentNullException but threw System.NullReferenceException
  at Layout.ValidateGrid(GridDimensions grid, IReadOnlyList`1 tiles) ... Layout.cs:line 78

Failed CreateDraft_rejects_a_null_grid [1 ms]
  should throw System.ArgumentNullException but threw System.NullReferenceException
  at Layout.ValidateGrid ... at Layout.RequireValidGrid ... at Layout.CreateDraft ... Layout.cs:line 133

Total tests: 11  Passed: 8  Failed: 3
```

Independently reproduced by the orchestrator (3/11 matching exactly) and
again by the phase-6 reviewer, who additionally confirmed the degenerate
message's exact whitespace via `cat -A` (`Grid  with 0 tile(s)...` — two
spaces, byte-verified).

## The fix — three lines, one per method

```diff
     public static Option<GridViolation> ValidateGrid(GridDimensions grid, IReadOnlyList<Tile> tiles)
     {
+        Ensure.That(grid).IsNotNull();
         Ensure.That(tiles).IsNotNull();
...
         Ensure.That(fab).IsNotNull();
         Ensure.That(name).IsNotNull();
+        Ensure.That(grid).IsNotNull();
         Ensure.That(tiles).IsNotNull();
         Ensure.That(clock).IsNotNull();
...
     public void EditDraft(...)
     {
+        Ensure.That(grid).IsNotNull();
         Ensure.That(tiles).IsNotNull();
         Ensure.That(clock).IsNotNull();
```

Guard order follows each method's own parameter order (`fab`, `name`,
`grid`, `tiles`, `clock` for `CreateDraft`), so a caller passing multiple
nulls is still told about the first parameter in signature order, not
`grid` specifically — verified by the phase-6 reviewer's own throwaway
probe against the real compiled assembly, not by reasoning:

```
null name + null grid:  ArgumentNullException ParamName=name
null fab  + null grid:  ArgumentNullException ParamName=fab
null grid + null tiles: ArgumentNullException ParamName=grid
null grid + null clock: ArgumentNullException ParamName=grid
```

## Three guards, not two — confirmed load-bearing, not defensive

The issue's own scope named only `CreateDraft`/`EditDraft`; the architect's
plan locked in a third guard on `ValidateGrid` itself. Phase 6 confirmed
this is necessary, not extra: `ValidateGrid` is `public static` with two
direct callers outside the aggregate —
`CreateLayoutDraftCommandHandler.cs:23` and
`EditDraftRevisionCommandHandler.cs:22` — both calling it before touching
the aggregate at all. Guarding only the two aggregate methods would have
left both handler entry paths dereferencing an unguarded `grid`.

**One behavior change, documented and verified harmless**:
`ValidateGrid(null, [])` previously returned `Some(GridViolation.Empty)`
and now throws instead. Checked reachability, not just asserted it: both
HTTP endpoints construct `grid` via `GridDimensions.From(...)` before
building any command, and `From` never returns null — so no null `grid`
can reach a handler over a real HTTP request. The only population
affected is direct in-process programmer misuse, which is exactly what
the guard exists to catch.

## Phase 6 — one review round, no blockers, no should-fixes

`backend-reviewer` independently reproduced the entire red-then-green
sequence by reverting only the production file (`Layout.cs`) with the new
tests still in place, confirming the same 3 failures, then restoring and
confirming 163/163 again — proving the fix is real, not merely reported.
Also confirmed: the guard-ordering convention matches this codebase's
existing pattern (`Camera.Register` cross-checked as the reference shape),
the rest of `Layout.cs` has no other unguarded reference-type parameter,
and `#2480` (the adjacent Api-layer 500-vs-400 gap) is correctly excluded
as a different category of defect belonging in its own issue.

Two cosmetic nits, neither actionable: a typo in an early commit subject
("ungueded" for "unguarded" — permanent in history post rebase-merge, not
worth a force-push on its own), and the branch name lacking a `fix/`
prefix (already recorded in spec.md as inherited and accepted).

## Independent re-verification, this pass

```
$ dotnet test tests/LayoutComposition.Domain.Tests
Passed!  - Failed: 0, Passed: 163, Total: 163

$ dotnet test tests/LayoutComposition.Application.Tests
Passed!  - Failed: 0, Passed: 82, Total: 82

$ git diff origin/develop -- src/LayoutComposition/Domain/Layout/Layout.cs
(3 lines added, 0 removed)

$ dotnet build -c Release
Build succeeded. 0 errors, only pre-existing S107 advisory warnings.
```

## Follow-up filed

- **#2480** — `POST /layouts` (and the edit-revision endpoint) return a
  bare 500 instead of `400 LAYOUT_INVALID_INPUT` when the request body
  omits `grid` entirely, since `RespectNullableAnnotations` isn't
  configured and the resulting NRE escapes a `try` that only catches
  `ArgumentException`. Found while verifying this spec's own claims,
  deliberately out of scope here (Api-layer trust-boundary validation,
  not a domain-argument precondition), filed separately and referenced
  in spec.md.
