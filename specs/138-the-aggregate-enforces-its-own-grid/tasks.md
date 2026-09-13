# Tasks 138 — The `Layout` aggregate enforces its own grid invariants

**Spec:** `specs/138-the-aggregate-enforces-its-own-grid/spec.md`
**Plan:** `specs/138-the-aggregate-enforces-its-own-grid/plan.md`
**Issue:** #2187 (feature-level; the Project #13 item — no per-task issues, ADR-0037 as corrected)
**Engineer:** `backend-engineer` (single)
**Phase 4a colour:** **RED** — behaviour-changing (ADR-0139, ADR-0144)

---

## Parallelism (ADR-0109)

**No `[P]` markers in this feature, and that is the correct answer, not an
omission.** Every task touches `Layout.cs` or the single new test file, and the
phase-4a → 4b handoff is strictly sequential by ADR-0144: the `test-writer`'s
verbatim red output *is* the engineer's brief. There are no disjoint files to
fan out across, no second bounded context, and no frontend slice.

**No foundational blocker either.** Nothing in `Shared.Kernel`,
`Shared.Contracts`, `AppHost` or an Aspire resource changes, so this feature
gates no other board item and no other board item gates it.

---

## US1 — The aggregate cannot be put into an invalid grid state (P1)

### Phase 4a — tests first, observed RED (`test-writer`)

**[T001] [US1] Add `LayoutGridInvariantTests` — the eight refusals.**

New file `tests/LayoutComposition.Domain.Tests/Layout/LayoutGridInvariantTests.cs`.
Eight facts, sentence-style names (ADR-0053), Shouldly, reusing `LayoutBuilder`
and the existing `TileAt` shape:

| # | Method | Grid | Tiles | Expected |
|---|---|---|---|---|
| 1 | `CreateDraft` | `GridDimensions.Cell` | `[]` | `InvalidOperationException` naming `Empty` |
| 2 | `CreateDraft` | `GridDimensions.Default` | two tiles at `(0,0)` | `…` naming `DuplicatePosition` |
| 3 | `CreateDraft` | `GridDimensions.Cell` | one tile at `(0,1)` | `…` naming `OutOfBounds` |
| 4 | `CreateDraft` | `new GridDimensions(3, 3)` | one tile at `(0,0)` | `…` naming `TooLarge` |
| 5 | `EditDraft` | `GridDimensions.Cell` | `[]` | `…` naming `Empty` |
| 6 | `EditDraft` | `GridDimensions.Default` | two tiles at `(0,0)` | `…` naming `DuplicatePosition` |
| 7 | `EditDraft` | `GridDimensions.Cell` | one tile at `(0,1)` | `…` naming `OutOfBounds` |
| 8 | `EditDraft` | `new GridDimensions(3, 3)` | one tile at `(0,0)` | `…` naming `TooLarge` |

**Two things this task must get right, or the red proves nothing:**

- The `TooLarge` cases (**4** and **8**) must use `new GridDimensions(3, 3)`,
  **not** `GridDimensions.From(3, 3)`. `From` runs
  `Ensure.That(rows * cols).Satisfies(cells => cells <= MaxCells, …)` and throws
  `ArgumentException` from the value object — which would make the test pass
  today for a reason that has nothing to do with the aggregate.
  `LayoutTests.cs:305-311` already uses the direct constructor for exactly this
  reason and says so in a comment.
- The `EditDraft` cases (**5–8**) must target a revision that is still a
  **Draft**, so the failure is the grid and not `ReplaceTiles`' Draft-state
  guard. `new LayoutBuilder().Build()` returns a chain whose only revision is a
  fresh Draft — use it and do not `Publish` first.

**Also assert the state did not move** on cases 5–8: after the throw, the
revision still carries its original 1×1 grid and its original single tile. A
refusal that half-applies is not a refusal.

**Depends on:** nothing.

---

**[T002] [US1] Add the two "still accepts valid input" facts.**

Same file. `CreateDraft` with a full 2×2 and `EditDraft` with two in-bounds tiles
must **not** throw. These are green from the start and stay green — they are the
over-tightening guard. (`LayoutTests.cs:165-182` already covers the `EditDraft`
happy path; this pair is the local, adjacent statement of the same thing so the
new file reads as a complete contract.)

**Depends on:** T001 (same file).

---

**[T003] [US1] Run the new tests and capture the VERBATIM failure output.**

```
dotnet test tests/LayoutComposition.Domain.Tests --filter "FullyQualifiedName~LayoutGridInvariant"
```

**Gate — all of these must hold before phase 4b starts:**

- All **eight** refusal facts FAIL.
- They fail because **no exception was thrown** (`Should.Throw<InvalidOperationException>`
  reporting that the action completed), **not** with a compile error and **not**
  with an `ArgumentException` from `GridDimensions.From`. A `TooLarge` case
  failing with `ArgumentException` means T001's first rule was broken — fix the
  test, re-run, do not proceed.
- The two acceptance facts (T002) PASS.

The captured output is the `backend-engineer`'s brief and is quoted verbatim in
the PR body (ADR-0139). **The engineer may not edit these tests to make them pass.**

**Depends on:** T001, T002.

---

### Phase 4b — implementation (`backend-engineer`)

**[T004] [US1] Add `Layout.RequireValidGrid` and call it from both write paths.**

`src/LayoutComposition/Domain/Layout/Layout.cs`:

- Add a `private static void RequireValidGrid(GridDimensions grid, IReadOnlyList<Tile> tiles)`
  beside `ValidateGrid` (plan §3 has the body). It calls `ValidateGrid` — it does
  not re-implement any check. `GridViolation` stays the single enum.
- `CreateDraft`: call it **after** the four `Ensure.That(...)` guards and
  **before** `clock.UtcNow` / the object construction.
- `EditDraft`: call it **after** the two `Ensure.That(...)` guards and
  **before** `RequireRevision(number)`, so nothing is looked up or mutated.
- Name it `RequireValidGrid` to match the file's existing `RequireRevision`.
- Throw `InvalidOperationException` with a message naming the `GridViolation`
  value. **Do not** introduce a new exception type — no such convention exists in
  this repo (spec §2), and `Layout.cs:20-21` already assigns this category to
  `InvalidOperationException`.
- **Do not** touch `Revision.cs`'s code, `BranchDraft`, or EF materialisation
  (spec §5, plan §5).

**Depends on:** T003 (the red output is the brief).

---

**[T005] [US1] Leave both Application handlers exactly as they are — and prove it.**

No edit. This task exists because "remove the now-redundant handler check" is the
single most likely review suggestion, and acting on it turns every operator input
error into a `500`.

Verification: `git diff --stat` must show **no** change under
`src/LayoutComposition/Application/`, and
`git grep -c ValidateGrid -- src/LayoutComposition/Application` must still be 2.

**Depends on:** T004.

---

**[T006] [US1] Run the full LayoutComposition domain suite; confirm nothing else moved.**

```
dotnet test tests/LayoutComposition.Domain.Tests
```

All ten new facts green, and **every pre-existing fact green unmodified** — in
particular `LayoutGuardTests` (the `ArgumentNullException` ordering),
`LayoutTests.cs:245` and `LayoutRevisionStateMachineTests.cs:108` (the
Draft-state guard still fires ahead of nothing), and
`LayoutChainArchivalTests` (`EditDraft` still recomputes the marker). **No
existing test file may be edited.** If one goes red, the guard was placed wrong —
fix `Layout.cs`, not the test.

**Depends on:** T004.

---

## US2 — The domain's doc comments and its behaviour agree (P1, same slice)

**[T007] [US2] Correct the two contradicted statements in `Layout.cs`.**

- `Layout.cs:93` (`CreateDraft`) and `Layout.cs:154` (`EditDraft`): delete
  *"The grid + tiles must already be valid (`ValidateGrid`)"* and state the real
  arrangement — the aggregate validates; the handlers validate first and own the
  `LAYOUT_GRID_*` `400`.
- `Layout.cs:16-19` (class doc): extend so it says the aggregate calls
  `ValidateGrid` too, as the backstop, rather than only the handlers.
- Verification: `grep -n "must already be valid" src/LayoutComposition/Domain/Layout/Layout.cs`
  returns nothing.

**Depends on:** T004 (the comment must describe shipped code).

---

**[T008] [US2] Record the two-tier arrangement on `GridViolation`.**

`src/LayoutComposition/Domain/Layout/GridViolation.cs:5-9` currently reads
*"an operator input error is a `Result` failure, not a thrown exception
(ADR-0047)"*. Keep that sentence — it is true and it is why the handlers stay —
and add the second tier: a violation reaching the aggregate is programmer error
and throws. Without this, the existing line reads as forbidding T004.

**Depends on:** T004.

---

**[T009] [US2] Confirm `Revision.cs:16`, `Revision.cs:108` and `GridPosition.cs:9-11`
need no edit.**

These three now state the truth. Re-read them against the shipped `Layout.cs` and
change nothing. Recorded as a task so the phase-6 reviewer sees the three claims
were checked rather than skipped — they are the claims the issue was filed over.

**Depends on:** T004.

---

## Phase 5 — Verify (`/verify`)

**[T010] Observe the backstop and the unchanged HTTP surface end to end.**

Per spec §8:

1. `dotnet test tests/LayoutComposition.Domain.Tests --filter "FullyQualifiedName~LayoutGridInvariant"` — 10 green.
2. Boot the Aspire stack. `POST /layouts` with two tiles at `(0,0)` →
   **`400` / `LAYOUT_GRID_DUPLICATE_POSITION`**. A `500` here is a failure of this
   spec, not a pass.
3. Create + publish a valid 2×2 wall; confirm four tiles render on the kiosk.
4. `grep -n "must already be valid" …/Layout.cs` → empty.

**Latency:** no figure to cite. §IV is **N/A** for this change (spec §6) — no leg
is on the diff, so no §VII dashboard obligation attaches.

**Depends on:** T006, T007, T008, T009.

---

## Phase 6 — QA (`backend-reviewer`)

**[T011] Review.** `/code-review`. `/security-review` is **not** required: no
endpoint, scope, auth guard, token, secret or trust boundary is touched; the diff
is one private helper and four comments inside a domain project.

Reviewer's specific checklist for this diff:

- The handler-side `ValidateGrid` calls are still there (T005). Suggesting their
  removal is out of scope by spec §10.
- `RequireValidGrid` calls `ValidateGrid` rather than duplicating any of the four
  checks.
- No new exception type; `InvalidOperationException` only.
- The `Ensure.That` guards still precede the grid check in both methods.
- `BranchDraft` is untouched, and spec §5 argues why.
- No existing test was edited (ADR-0144: editing tests to reach green is a
  blocked outcome).

**Depends on:** T010.

---

## Phase 7 — PR

**[T012] Open the PR against `develop`.**

`gh pr create --base develop`. Body must carry:

- the **verbatim** phase-4a red output from T003 (ADR-0139);
- `Closes #2187`;
- the mechanism decision and its one-line justification — *throw
  `InvalidOperationException`, per ADR-0112 §2's "enforced inside the aggregate"
  plus ADR-0047's programmer-error category; the handlers and the HTTP error
  shape are unchanged*;
- **Phase 5: not applicable for a latency figure** — §IV is N/A, argued in spec §6;
- the `BranchDraft` exclusion and its reason (spec §5), so a reviewer does not
  read it as an oversight.

**Depends on:** T011.

---

## Dependency graph

```
T001 → T002 → T003 (RED gate) → T004 ─┬→ T005
                                      ├→ T006 ─┐
                                      ├→ T007 ─┤
                                      ├→ T008 ─┼→ T010 → T011 → T012
                                      └→ T009 ─┘
```

## Definition of done

- Eight refusal facts were **observed failing for the right reason** and now pass.
- No existing test file was modified.
- `POST /layouts` and `PATCH /layouts/{id}/revisions/{n}` still answer `400` with
  the same `LAYOUT_GRID_*` codes for an operator's bad grid.
- No statement in `LayoutComposition.Domain` about who validates the grid is false.
- CI green; PR rebase-merged to `develop` (ADR-0087).
