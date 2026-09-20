# Tasks 196 — The parameter the guards forgot

**Phase:** 3 (Tasks) — ADR-0037
**Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2309](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2309)
— feature-level, on Project #13, status Todo (verified `--limit 2000`, matched on
`content.url`)

Per CLAUDE.md §Workflow, phase 3 creates **no per-task issues** since spec 028.
`tasks.md` is the artefact the work is tracked against, and #2309 is already on
the board — nothing to add.

---

## Parallelism: none, by construction

**No task carries `[P]`.** ADR-0109 marks `[P]` only for disjoint file
ownership. This spec owns two files and they are strictly ordered in time:

```
tests/LayoutComposition.Domain.Tests/Layout/LayoutGuardTests.cs   (T002)
src/LayoutComposition/Domain/Layout/Layout.cs                     (T004)
```

The test file must be red before the source file is touched (ADR-0139,
constitution §Testing), so even though the files are disjoint the tasks are not.
One worker, sequential.

### File contention — verified 2026-09-20, clean

- `gh pr list --state open --limit 30` → **zero open PRs**. Nothing is parked.
- Six remote branches swept with
  `git diff --name-only origin/develop...origin/<branch>` —
  `feat/1975-camera-edits-staged-together`,
  `feat/2345-a-revision-that-holds-more-than-one-label`,
  `fix/1995-a-hand-made-account-inherits-offline`,
  `fix/1995-offline-access-is-granted-not-inherited`,
  `perf/1956-batch-audit-writes`,
  `refactor/1970-the-seams-section-ix-mandates` —
  **none touches `Layout.cs` or anything under `LayoutComposition`**.
- `git worktree list` → two worktrees only: `D:/Github/smart-sentinel-eye`
  (`develop`) and `D:/Github/sse-2309` (this one).

`feat/2345-a-revision-that-holds-more-than-one-label` is the one to re-check
before opening the PR: it is a LayoutComposition-adjacent overlay feature whose
spec directory tops out at 150, so it is well behind and currently touches
nothing here — but it is the only live branch in the neighbourhood.

### Spec number — 196

Highest on `origin/develop` is **195** (`195-the-upstreams-a-build-trusts`).
All six remote branches top out far lower (150, 111, 110, 80, 63, 44); no branch
or commit anywhere in the repo adds a `specs/196*` path
(`git log --all --diff-filter=A -- 'specs/196*'` → empty). **Re-check before
opening the PR**: an unmerged branch can claim a number `origin/develop` does
not show, and the check is only as fresh as the last `git fetch`.

---

## Ordering

```
T001 ──> T002 ──> T003 ──> T004 ──> T005 ──> T006
(baseline)  (3 red tests)  (quote red)  (3 guard lines)  (all green)  (review)
```

T001–T003 are phase 4a (test-writer). T004–T005 are phase 4b
(backend-engineer). T006 is phase 6 (backend-reviewer). **T003's output is
T004's brief** — the engineer receives the verbatim red and may not edit the
tests to pass it (ADR-0144).

---

## Phase 4a — **test-writer** (colour: RED)

> ADR-0144: the test-writer writes tests only, runs them, and returns the
> **verbatim output**. It does not open `Layout.cs`.

### [T001] [US-1] Capture the characterisation baseline

Run and keep the verbatim output of both projects:

```
dotnet test tests/LayoutComposition.Domain.Tests/SmartSentinelEye.LayoutComposition.Domain.Tests.csproj
dotnet test tests/LayoutComposition.Application.Tests/SmartSentinelEye.LayoutComposition.Application.Tests.csproj
```

**Done when:** both report 0 failed, and the pass counts are written down. These
counts are the behaviour-preserving half of constitution §Testing (AS-5, AS-6):
T005 must reproduce them with **no assertion edited**.

**Depends on:** nothing.

### [T002] [US-1] Write the three red tests

In `tests/LayoutComposition.Domain.Tests/Layout/LayoutGuardTests.cs`, beside the
existing `CreateDraft_rejects_a_null_name` / `_null_clock` facts, mirroring their
shape exactly (`Action act = () => …; act.ShouldThrow<…>()`, `LayoutBuilder`,
`OneTile()`, `FixedMoment`):

1. **`CreateDraft_rejects_a_null_grid`** (AS-1) — `CreateDraft(fab, name,
   grid: null!, OneTile(), operator, clock)`. Assert
   `ShouldThrow<ArgumentNullException>()` **and** `ParamName == "grid"`.
2. **`EditDraft_rejects_a_null_grid_even_with_an_empty_tile_set`** (AS-2) —
   `new LayoutBuilder().Build()`, then `EditDraft(LayoutRevisionNumber.One,
   grid: null!, Array.Empty<Tile>(), clock)`. Assert
   `ShouldThrow<ArgumentNullException>()` **and** `ParamName == "grid"`. The
   empty tile set is deliberate: it is the path that does **not** crash today.
3. **`ValidateGrid_rejects_a_null_grid`** (AS-3) —
   `Layout.ValidateGrid(grid: null!, OneTile())`. Assert
   `ShouldThrow<ArgumentNullException>()` **and** `ParamName == "grid"`.

Naming is sentence-style with underscores (ADR-0053); assertions are Shouldly
(ADR-0052). `null!` is required because NRT is on solution-wide (ADR-0141) —
the sibling facts already use it.

**Assert `ParamName`, not only the exception type.** The type alone would pass
if the throw came from an unrelated guard; `ParamName == "grid"` is what proves
`[CallerArgumentExpression]` resolved at the right call site. An assertion that
cannot distinguish its subject is the defect this session has already found five
times.

**Done when:** the three facts compile.

**Depends on:** T001.

### [T003] [US-1] Observe them red and record the verbatim failure

```
dotnet test tests/LayoutComposition.Domain.Tests/SmartSentinelEye.LayoutComposition.Domain.Tests.csproj \
  --filter "FullyQualifiedName~LayoutGuardTests"
```

**Expected — all three fail:**

- tests 1 and 3: a `NullReferenceException` escapes where `ArgumentNullException`
  was expected (thrown at `Layout.ValidateGrid`, `Layout.cs:78`, `grid.Rows`).
- test 2: an `InvalidOperationException` escapes instead, carrying the literal
  message `Grid  with 0 tile(s) violates Empty.` — two spaces after `Grid`.

**Done when:** three failures, and the output is saved verbatim for the PR body.

**A green run here blocks the spec.** A test arriving green is a phase-4
failure, not a shortcut (ADR-0144). If any of the three passes against the
unmodified tree, stop and report — it means the tree is not what `spec.md` §What
was verified describes.

**Record test 2's message string explicitly in the report.** After T004 it
becomes unreachable, and this is the only record of it anyone will have.

**Depends on:** T002.

---

## Phase 4b — **backend-engineer** (colour: GREEN)

> Brief: T003's verbatim output. **The tests are not yours to edit.** If a test
> looks wrong, report it — do not adjust it (ADR-0144).

### [T004] [US-1] Add the three guards

`src/LayoutComposition/Domain/Layout/Layout.cs` — three insertions, nothing else:

| method | line (at `75ee9545`) | insertion |
|---|---|---|
| `ValidateGrid` | before `Ensure.That(tiles)` at `:72` | `Ensure.That(grid).IsNotNull();` |
| `CreateDraft` | between `:130` (`name`) and `:131` (`tiles`) | `Ensure.That(grid).IsNotNull();` |
| `EditDraft` | before `Ensure.That(tiles)` at `:190` | `Ensure.That(grid).IsNotNull();` |

Match the surrounding lines exactly: `Ensure.That(x).IsNotNull();`, ADR-0105.
No `ArgumentNullException.ThrowIfNull` (banned at `error` by `RS0030`, see
`build/guards/BannedSymbols.txt`). **No comment** — five guards in signature
order explain themselves (ADR-0036, §No drive-by comments).

**Position is load-bearing in `CreateDraft`:** the guard goes *after* `name`, so
a call with both null still names `name` (AS-4). Putting it first would flip
`LayoutGridInvariantTests.cs:232`.

**Done when:** it compiles and the diff is exactly three added lines under
`src/`.

**Depends on:** T003.

### [T005] [US-1] Green, and the characterisation net intact

```
dotnet test tests/LayoutComposition.Domain.Tests/SmartSentinelEye.LayoutComposition.Domain.Tests.csproj
dotnet test tests/LayoutComposition.Application.Tests/SmartSentinelEye.LayoutComposition.Application.Tests.csproj
```

**Done when:**

- the three T002 tests pass;
- the pass counts equal T001's, plus exactly three;
- `git diff --name-only HEAD` lists exactly two files — `Layout.cs` and
  `LayoutGuardTests.cs` — plus this spec directory;
- **no existing assertion has been edited.** Verify, do not assume:
  `git diff HEAD -- tests/ | grep '^-'` must show no removed assertion line.

An existing test that now needs its assertion changed is evidence the behaviour
moved somewhere this spec did not intend. **Block and report** — do not adjust
it (constitution §Testing).

**Depends on:** T004.

---

## Phase 6 — **backend-reviewer**

### [T006] [US-1] Review

Domain code in a bounded context; `backend-reviewer` per ADR-0144's phase↔role
table. Not `security-reviewer`: no trust boundary, no token, no scope, no fab
authorization is touched (`spec.md` §Auth / scope).

Specific things worth the reviewer's attention, beyond the usual:

- Guard **order** in `CreateDraft` (AS-4) — the one way to get this wrong.
- Whether the third guard in `ValidateGrid` is wanted. `spec.md` §Why
  `ValidateGrid` gets the third line argues for it and records the one-deletion
  trim-back. This is the spec's single deliberate step beyond the issue's
  literal scope and should be accepted or trimmed explicitly, not silently.
- That the three new tests assert `ParamName`, not merely the exception type.
- That nothing else was swept in — no guard-coverage architecture test, no
  message-format defence, no touch to `Revision.NewDraft` (`spec.md` §Out of
  scope).

**Depends on:** T005.

---

## Phase 5 (verify) and phase 7 (PR) — notes for the orchestrator

- **Phase 5 is the test run, and that is not a shortcut here.** The deliverable
  is which exception a domain method throws on an invalid argument, and no HTTP
  request can reach it: the endpoints build `GridDimensions.From(body.Grid.Rows,
  body.Grid.Cols)` themselves (`LayoutEndpoints.Commands.cs:143`, `:354`), so a
  null never travels from the wire into the aggregate — it fails earlier, in the
  API layer, and that earlier failure is **a separate defect this spec does not
  fix** (`spec.md` §Out of scope, finding 2). The end-to-end procedure in
  `spec.md` is the honest form of phase 5 for this change, and it is
  reproducible by a reviewer without the diff.
- **The PR body must quote T003's verbatim red**, including test 2's
  `Grid  with 0 tile(s) violates Empty.` (ADR-0139).
- **PR base `develop`**, `--base develop` passed explicitly (ADR-0028). Commit
  message per ADR-0030; no `Co-Authored-By` (ADR-0086) — the session
  attribution footer for this session still applies.
- Suggested commit: `fix(layouts): guard grid against null at the layout
  aggregate's boundary`.
- Use a closing keyword for #2309 and **check the issue state after the merge** —
  a PR mention alone closes it about a third of the time.

---

## Gate — phase 3

Tasks are atomic and ordered; #2309 is on Project #13. Hand back for review
before phase 4.
