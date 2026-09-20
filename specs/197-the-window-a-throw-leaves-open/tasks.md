# Tasks 197 — The window a throw leaves open

**Phase:** 3 (Tasks) — ADR-0037
**Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2314](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2314)
— feature-level, on Project #13, **status Todo** (verified 2026-09-20 with
`gh project item-list 13 --owner smartsolutionslab --limit 2000`, matched on
`content.url`, per the memory note that the number filter returns zero)

Per CLAUDE.md §Workflow, phase 3 creates **no per-task issues** since spec 028.
`tasks.md` is the artefact the work is tracked against, and #2314 is already on
the board — **nothing to add**.

---

## Spec number — 197

- `origin/develop` @ `76248bc1` tops out at **`specs/196-the-parameter-the-guards-forgot`**.
- All seven other remote branches were swept with
  `git ls-tree --name-only origin/<branch> specs/`; the highest anywhere is
  **150** (`feat/2345-a-revision-that-holds-more-than-one-label`). The rest top
  out at 111, 110, 80, 63, 44.
- Every local branch (35 of them) swept the same way: **no `specs/197*` path
  exists anywhere in the repo**, on any ref.

**Re-check before opening the PR.** The memory note *"Spec number:
origin/develop isn't enough"* applies exactly here — two unmerged branches can
both claim the next number, and this check is only as fresh as the last
`git fetch`.

## File contention — verified 2026-09-20, clean

- `gh pr list --state open --limit 50` → **zero open PRs**. Nothing is parked, so
  nothing can be rebased into a conflict here.
- Seven remote branches diffed against `origin/develop` for
  `CameraViewer|wallAlignment`:
  `feat/1975-camera-edits-staged-together`,
  `feat/2345-a-revision-that-holds-more-than-one-label`,
  `fix/1995-a-hand-made-account-inherits-offline`,
  `fix/1995-offline-access-is-granted-not-inherited`,
  `perf/1956-batch-audit-writes`,
  `refactor/1970-the-seams-section-ix-mandates`, `main` —
  **none touches either file**.
- All 35 local branches swept the same way. Two hits, **both stale**:
  - `fix/2189-a-sampler-that-fails-loudly` — touches `CameraViewer.tsx` and
    `CameraViewerAlignment.test.tsx`. **Already merged**: the two-counter code
    and its `#2189` comments are present in `origin/develop`'s copy of
    `CameraViewer.tsx`. `git merge-base --is-ancestor` answers *no*, which is
    expected and means nothing here — ADR-0087 rebase-merge renames the SHAs, so
    every merged branch in this repo fails that check. Verified by content, not
    by ancestry.
  - `docs/2191-a-citation-the-fixing-commit-broke` — touches
    `CameraViewerMedia.test.tsx`, a **different file**. No overlap.
- `git worktree list` → two worktrees: `D:/Github/smart-sentinel-eye`
  (`develop`) and `D:/Github/sse-2314` (this one).

**Conclusion: `CameraViewer.tsx` is uncontended.** The new test file
`CameraViewerSamplerWindow.test.tsx` does not exist on any ref, so it cannot
collide.

---

## Parallelism (ADR-0109)

`[P]` marks disjoint file ownership **and** independence in time. This spec
owns two files:

```
apps/shared/src/ui/composites/CameraViewerSamplerWindow.test.tsx   (T002, T005)
apps/shared/src/ui/composites/CameraViewer.tsx                     (T004, T006)
```

They are disjoint but **strictly ordered**: the tests must be observed red
before the source is touched (ADR-0139, constitution §Testing), and the red is
the engineer's brief (ADR-0144). So **no task carries `[P]` in the critical
path.**

**T005 is the one exception and it is marked `[P]`**: the US2 decode case is
written into the same new test file, but it exercises the decode sampler's
`.catch` (`CameraViewer.tsx:227`), which is a different effect from US1's
(`:300`). A second worker could take T005/T006 against a branch that already has
T002–T004, with no file conflict beyond an append to the test file. **In
practice one worker should do all six** — the whole change is four lines and the
coordination cost exceeds the work.

There are no foundational tasks. No `Shared.Kernel`, no `Shared.Contracts`, no
`AppHost`, no Aspire resource, no migration. Nothing blocks anything outside
this spec.

---

## Ordering

```
T001 ──> T002 ──> T003 ──> T004 ──> T005 ──> T006 ──> T007 ──> T008
 base     red      quote    US1      red      US2      gate     review
                   the red   fix     (decode)  fix
```

| tasks | phase | role |
|---|---|---|
| T001–T003, T005 | **4a** | **`test-writer`** |
| T004, T006 | **4b** | **`frontend-engineer`** |
| T007 | 4b | `frontend-engineer` |
| T008 | **6** | **`frontend-reviewer`** |

**Not `backend-engineer` / `backend-reviewer`.** This is `apps/shared`
TypeScript/React composite code with a vitest + jsdom suite. No `.cs` file is
opened.

**Phase-4a colour: RED.** Behaviour-changing — what happens after a thrown tick
is different. Per CLAUDE.md §Workflow, a test arriving green here is a phase-4
failure, not a shortcut, and this spec names the specific way that would happen
(the constant fixture). T003's verbatim output is T004's brief; the engineer may
not edit the tests to pass.

---

## US1 — A failed lag tick starts the next window fresh (P1)

### `[T001]` `[US1]` Capture the baseline

Run the two existing suites that cover this component and record them green
**before anything is written**:

```sh
pnpm --filter "./apps/shared" test -- CameraViewerAlignment
pnpm --filter "./apps/shared" test
```

Record the pass counts. `CameraViewerAlignment.test.tsx` is the #2189 regression
guard and must be **green now, green after, and byte-identical throughout** —
`git diff --stat -- apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx`
must stay empty to the end of T007.

**Done when:** both commands green, counts recorded.
**Depends on:** nothing.

---

### `[T002]` `[US1]` Write the red — a widened window after a callback throw

New file `apps/shared/src/ui/composites/CameraViewerSamplerWindow.test.tsx`.
Mirror `CameraViewerAlignment.test.tsx`'s module doubles (`streams.api`,
`WhepClient` — **including the `onConnectionStateChange('connected')` call**,
without which every effect is skipped and the whole file is vacuous).

**The fixture is the task.** Per plan §"The fixture must be a function of TIME":

- Counter values computed from `Date.now()` elapsed, **not** mutated per call —
  both samplers share one `stats()` double and their reads interleave at
  2 000 / 5 000 ms.
- `Date.now()`, **not** `performance.now()` — vitest's default `toFake` excludes
  `performance` (`CellPage.test.tsx:503-506`).
- Rate: 0.015 frames/ms; processing flat at 0.5 ms/frame; buffer **1.0 ms/frame
  before `STEP_AT = 4_000`, 4.0 ms/frame after**.
- A `latencyLines(measurement)` helper over an `infoSpy`, filtering
  `call[0] === '[latency]'`, mirroring the sibling file's `resilienceLines`.

Cases (spec §Acceptance scenarios):

1. **The headline.** `onLagMeasured` throws on its **first** call only. Advance
   10 000 ms. Assert the **first** `presentation_buffer` figure reported after
   the throw is **`4.0`**, and the first post-throw `onLagMeasured` lag argument
   is **`4.5`**. Exact equality — no epsilon; the expected values divide exactly
   in binary and an epsilon wide enough for float noise would swallow the
   1.5 ms under test.
2. **Persistent throw.** Callback throws for ten ticks, then stops. Assert no
   reported figure is a mean over the outage — the first post-recovery figure
   describes one 2 000 ms window.
3. **`stats()` throws**, not the callback — same expectation. Proves the fix
   covers the path that is not ours.
4. **Nothing changes when nothing throws.** Ten clean ticks, every figure a
   2 000 ms window. **Must be green before and after** — it is the guard against
   a fix that resets `previous` unconditionally and reports nothing ever.

**Every case asserts the callback and the sampler were actually reached before
asserting what they produced** — the convention the sibling file states in prose
("a callback never reached would make … true of a component that measured
nothing"). Also assert `container.querySelector('video')` is non-null: FR-013,
reporting must not cost the picture.

**Done when:** case 1 is **red**, case 4 is **green**, and the red message shows
`2.5` received against `4.0` expected. **A case 1 that passes means the fixture
is wrong — fix the fixture, not the expectation.**
**Depends on:** T001.

---

### `[T003]` `[US1]` Quote the red verbatim

Capture the full vitest failure output for case 1 — expected, received, file and
line. This is the PR-body evidence ADR-0139 requires and **the brief T004
receives**. Do not summarise it; paste it.

**Done when:** the verbatim block is in hand and shows `2.5` where `4.0` was
expected.
**Depends on:** T002.

---

### `[T004]` `[US1]` Reset `previous` in the lag sampler's `.catch`

`apps/shared/src/ui/composites/CameraViewer.tsx:300`. Turn the expression-bodied
`.catch` arrow into a block, set `previous = null` **before**
`reportSamplerFailure(...)`, and add the comment from plan §"The change" — *why*
(the unbounded pin and the cumulative-average contract `lagBetween` states),
never *what*.

**Do not:**
- wrap the callback in a new try/catch (plan §"Why the outer `.catch`");
- move `reportKioskLatency` before the callback (spec §"considered and NOT done"
  — it would emit clean in-budget figures from a wall that is not aligning);
- touch `reportSamplerFailure`, either counter ref, or the decade cadence;
- touch `wallAlignment.ts` or `kioskLatency.ts` — read-only in this spec;
- edit any assertion in T002 or in `CameraViewerAlignment.test.tsx`.

**Done when:** T002's four cases are green and
`CameraViewerAlignment.test.tsx`'s sixteen are green **unmodified**.
**Depends on:** T003 (its output is the brief).

---

## US2 — The decode sampler does the same (P2)

### `[T005]` `[P]` `[US2]` Write the red for the decode sampler

Append to the same new file, reusing the time-based fixture unchanged — it
already serves both cadences.

One case: `stats()` throws on one decode tick (5 000 ms cadence) and succeeds
afterwards; the buffer/decode rate steps across it. Assert the first
`receive_to_decoded` figure after the throw describes **one 5 000 ms window**,
and that `decode-sampler-failed` is still reported on its decade cadence.

Work the arithmetic the way plan §"The arithmetic" does for US1 and write the
expected figure out before running, so a green-on-arrival result is recognisable
as a broken fixture rather than a passing test.

**Done when:** the case is red with a figure that is demonstrably the widened
window.
**Depends on:** T004 (so US1's green is not disturbed).

---

### `[T006]` `[US2]` Reset `previous` in the decode sampler's `.catch`

`apps/shared/src/ui/composites/CameraViewer.tsx:227`. Identical shape to T004,
with `decodeSampleFailuresRef` / `'decode-sampler-failed'`. The comment **points
at the lag sampler** rather than restating the argument — CLAUDE.md §"No
drive-by comments", and the file already uses this
"same-reasoning-as-above" idiom throughout.

**Done when:** T005 green; everything from T001–T004 still green.
**Depends on:** T005.

---

## Gate and review

### `[T007]` Full frontend gate

```sh
pnpm format:check
pnpm lint
pnpm typecheck
pnpm test
```

`format:check` is listed first because CI runs it first (`ci.yml`'s
`frontend` job's own first step) — a formatting deviation fails the whole
job before Lint/Typecheck/Test ever execute. Added at phase 6 after this
exact gap caused a real CI-blocking finding: this checklist omitted it,
and the new test file's own one line-wrap deviation would have failed
CI's first step silently, with none of the other (passing) gates below
it even running to say so.

Plus the two contention assertions:

```sh
git diff --stat -- apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx   # must be EMPTY
git diff --stat                                                                    # exactly 2 files
```

Note the memory item *"typecheck:e2e fails on a clean develop"* — a missing
`@types/node` in the workspace root reads as this branch's fault. If
`typecheck:e2e` fails, stash and re-run on clean `develop` before touching
config.

**Done when:** all three green, both assertions hold.
**Depends on:** T006.

---

### `[T008]` Phase 6 — `frontend-reviewer`

Review the diff. Specifically check:

1. **#2189 is intact.** `reportSamplerFailure` is called with the same counter,
   transition and error, on the same decade cadence, from both `.catch`
   handlers. The reset is a sibling statement, never a wrapper, and no throw is
   swallowed that was previously surfaced.
2. **No assertion was edited to pass.** `CameraViewerAlignment.test.tsx` byte-identical.
3. **The red was real.** The quoted failure shows `2.5` against `4.0` — not a
   missing-import error, not a vacuous absence.
4. **The fixture is time-based and uses `Date.now()`** — the two failure modes
   the plan names.
5. **The diff is four lines plus comments** in `CameraViewer.tsx`. Anything more
   is scope creep; `wallAlignment.ts` and `kioskLatency.ts` must be untouched.
6. **§IV is cited and the table is not edited.** The PR names the presentation
   buffer and SFU → kiosk decode legs, states no duration changes, and states
   the §IV table stays as it is.

**Done when:** findings resolved or accepted in writing.
**Depends on:** T007.

---

## Phase 7 notes (for the orchestrator, not a task)

- Branch `2314-reset-previous-on-callback-throw`, cut from and rebased onto
  `origin/develop`. `gh pr create --base develop` (CLAUDE.md: the flag stays
  mandatory).
- PR body: the verbatim red from T003, the §IV citation, and
  `Closes #2314` — the memory note says a bare mention auto-closes about one
  time in three, so use the keyword and check the issue state after the merge.
- Phases 5 skipped is **not** appropriate here: `/verify` should record the
  test-level observation. A manual wall observation is described in spec
  §"Independent end-to-end test procedure" but is **not** required to close
  this issue and does **not** discharge §IV's *observed* column.
- No ADR is written and none is needed (spec §"Locked tech choices"); ADR-0144
  forbids the lane writing one in any case.
