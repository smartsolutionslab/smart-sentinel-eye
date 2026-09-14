# Tasks 154 — An edit you can take back

**Spec:** `spec.md` **Plan:** `plan.md` **Issue:** #2347
**Branch:** `feat/2347-undo-in-the-overlay-editor`
**Engineer:** `frontend-engineer` throughout. No backend agent, no infra agent.
**Phase 4a colour: red.** T004-T010 must be **observed failing** before any
of T011-T016 is written, and the verbatim failure output goes in the PR body
(ADR-0139, constitution §Testing).

Format: `[ID] [P?] [Story]` — `[P]` marks tasks that own **disjoint files**
and may run in parallel (ADR-0109).

---

## Parallelism, up front

This feature is **mostly serial**, and pretending otherwise would waste a
fan-out. Two facts decide it:

- **`OverlayEditor.tsx` is a single 602-line file touched by six of these
  tasks.** One branch, one index — two agents editing it is a conflict, not
  parallelism.
- **T002 is the foundational task.** `useOverlayEditHistory.ts` is the
  contract every wiring task calls. Nothing after it can start until its
  signature exists.

The genuine `[P]` opportunities are three, and they are marked: the two
test files against different subjects (T004 vs T005), and the e2e spec
(T010) against everything else.

```
T001 (guard baseline)
  └─ T002 [foundational]  the hook's contract + implementation
       ├─ T003            editor: the emit path takes a boundary
       ├─ T004 [P]        hook unit tests          (own file)
       ├─ T005 [P]        editor behaviour tests   (own file)
       └─ T010 [P]        e2e test                 (own file)
            └─ T006..T009 the remaining red tests (same file as T005 — serial)
                 └─ T011..T016  make them green (OverlayEditor.tsx — serial)
                      └─ T017..T020  verification
```

---

## Phase 4a — tests first, observed red

### T001 — [US1] Capture the seven guard suites green, before touching anything

Run, record the verbatim output, and keep it for the PR:

```
npm --workspace apps/shared run test -- OverlayEditorCharacterisation OverlayEditorKeyboard OverlayEditorBackdrop OverlayLabelCharacterisation OverlayLabelParity OverlayGeometryFields
npm --workspace apps/management-web run test -- OverlayEditorDialogChainRetention
```

(Use the repo's actual script names; the point is the seven files, not the
incantation.)

**Done when:** all seven are green and the output is saved. This is the
before half of "survive unmodified" — the after half is T017.

**Blocks:** everything.

### T002 — [US1] `useOverlayEditHistory` — the contract and the implementation

**Foundational. Nothing else starts until this file's exported signature
exists.**

New file `apps/shared/src/ui/composites/useOverlayEditHistory.ts`.
Implements plan.md §2: `past` / `future` / `openRun` / `lastEmittedRef` /
`idleTimerRef`, the `commit` / `endRun` / `undo` / `redo` / `canUndo` /
`canRedo` surface, `TEXT_IDLE_MS = 500`, and invariants I1-I8.

Specifically:

- I6's re-seed detector is **reference identity against the last emitted
  object**. Plan.md §5 lists three wrong implementations by name; none of
  them may appear.
- No `react-redux`, no `@reduxjs/toolkit`, no new dependency. `react` and the
  `OverlayLabel` *type* only.
- The idle timer is cleared on unmount, mirroring
  `OverlayEditor.tsx:374-381`.

**Done when:** the file compiles and typechecks. Not when it is proven — that
is T004.

**Blocks:** T003-T010.

### T003 — [US1] Thread a boundary argument through the editor's emit path

`OverlayEditor.tsx` only. Call `useOverlayEditHistory`, and route the six
emission sites through `commit` per plan.md §3. Do **not** add the controls,
the bindings or the live region yet — this task is the plumbing, so that the
red tests in T005-T009 fail for the right reason (no undo) rather than a
compile error.

**Done when:** the seven guard suites are still green. If any needs editing,
**stop** — the behaviour moved.

### T004 — [P] [US1] Red: hook unit tests

New file `apps/shared/src/ui/composites/useOverlayEditHistory.test.ts`.
Disjoint from every other task's file.

One test per invariant, via `renderHook`:

- I1 `canUndo` is false at birth and after a re-seed.
- I2 an undo does not push onto `past` and does not clear `future`.
- I3 a new step clears `future`.
- I4 ten `commit`s with the same run key produce one `past` entry.
- I5 an `'atomic'` commit, and a commit with a different run key, each close
  an open run.
- I6 a `value` the hook did not emit clears both stacks; a `value` it did
  emit does not.
- I7 the hook's surface exposes nothing but `OverlayLabel`.
- I8 the timer is cleared on unmount (no state update after unmount warning).

**Observed red.** Quote the failures.

### T005 — [P] [US1] Red: the atomic sites — drag, resize, geometry commit

New file `apps/shared/src/ui/composites/OverlayEditorUndo.test.tsx`.
Rendered **bare, with no `<Provider>`**, like the five existing suites.

Scenarios from spec.md US1:

- a completed drag is one undo step
- a completed resize is one undo step
- a committed geometry value is one undo step
- a refused or escaped geometry draft is not an undo step
- redo puts back exactly what undo took
- a new edit after an undo discards the redo stack
- bad request: undo with nothing to undo changes nothing, calls `onChange`
  not at all, and the control is disabled rather than present-and-inert

**Observed red.**

### T006 — [US1] Red: the two idle-bounded runs — typing and the font slider

Same file as T005, so serial after it.

- a run of typing is one undo step, not one per keystroke
- **a run on the font-size slider is one undo step** — the fifth emission
  site, named in spec.md *Contradictions* item 1 and in no prior document
- blur closes a run mid-idle
- an emission from another site closes a run before the idle elapses
- two runs separated by more than `TEXT_IDLE_MS` are two steps

Fake timers.

**Observed red.**

### T007 — [US1] Red: the keydown→keyup run — held arrows

Same file. Serial after T006.

- a 160-press held run is one undo step (model the repeat as repeated
  `fireEvent.keyDown` with no `keyUp`, exactly as
  `OverlayEditorKeyboard.test.tsx:409` does)
- release-and-press-again is two steps
- switching axis mid-hold starts a new step
- `Ctrl` mid-hold starts a new step; `Shift` mid-hold does not
- blur of the label closes an open run

**Observed red.**

### T008 — [US1] Red: the floor, and the `editTarget` interaction

Same file. Serial after T007.

- undo cannot pass the opened state — edit mode, floor is `editTarget.label`
- undo cannot pass the opened state — create mode, floor is `DEFAULT_INPUT`'s
  label
- a `value` replaced from outside starts a fresh history and a new floor
  (this is the unit-level stand-in for close-and-reopen; the dialog-level
  version is the e2e in T010, because `OverlayEditorDialog.tsx` is not
  modified and gets no new suite)
- **a query settling does not erase the history** — re-render the editor with
  a new `resolvedPreview` object identity and an unchanged `value`, and
  assert `canUndo` survives. Plan.md §5 calls this the test that catches the
  three bugs this repo has already shipped; it is not optional.
- an uncommitted geometry draft is left alone by undo
- the backdrop is not part of the history, and selecting a backdrop still
  emits no `onChange`

**Observed red.**

### T009 — [US2] Red: the announcement

Same file. Serial after T008.

- an undo is announced in a polite region
- a refused undo says there is nothing to undo
- the undo region has its own `data-testid` and spec 149's geometry
  announcements are unchanged

**Observed red.**

### T010 — [P] [US1] Red: one end-to-end test

`e2e/overlays.spec.ts`, appended. Disjoint from every `apps/` file, so it may
be written in parallel with T004-T009.

One test, modelled on `operator edits a saved draft in place` (`:122`),
including `FIRST_WRITE_TEST_TIMEOUT_MS`:

1. seed and open a saved draft with **Edit**
2. read the Left field
3. drag the label
4. press `Ctrl+Z`
5. assert the Left field reads its step-2 value again
6. press `Ctrl+Z` until the Undo button is `disabled`
7. assert the label is back at the saved geometry

**Observed red.** It will fail at step 4 with no Undo to press.

---

## Phase 4b — make them green

The engineer receives T004-T010's verbatim failing output as its brief and
**may not edit those tests to pass** (ADR-0144).

All of T011-T016 touch `OverlayEditor.tsx`. **Serial.**

### T011 — [US1] Undo and redo, driven from the hook

Wire `undo()` / `redo()` and `canUndo` / `canRedo` into the editor. Make
T004 and T005 green.

### T012 — [US1] The idle runs — text and font size

`{ run: 'text' }` and `{ run: 'fontSize' }` at `:546` and `:556`, with
`onBlur` (and `onPointerUp` on the range) calling `endRun`. Makes T006 green.

### T013 — [US1] The arrow run

`arrowRunKey(axis, resizing)` at the `emitNormalized` call in
`handleLabelKeyDown`, `onKeyUp` on the `<Rnd>` ending the run, and the
existing `onBlur` at `:509` ending it too. Makes T007 green.

**Watch:** `OverlayEditorKeyboard.test.tsx` must stay green **unmodified**.
Its sixteen arrow-combo tests assert `onChange` payloads, which coalescing
does not change. If one of them goes red, the emission changed and that is a
defect, not a test to adjust.

### T014 — [US1] The floor and the re-seed detector

Plan.md §5 in `OverlayEditor.tsx`'s use of the hook. Makes T008 green —
including the query-settling test, which is the one that matters.

**If the query-settling test cannot be made green with reference identity**,
stop and report before reaching for the `editSessionKey` fallback: that
fallback costs FR-016 (`OverlayEditorDialog.tsx` would be modified) and is an
architecture decision, not an implementation detail.

### T015 — [US1] The controls and the key bindings

Two `<button type="button">` between the canvas regions and the input grid
(plan.md §6), disabled from `canUndo` / `canRedo`; the root-element
`onKeyDown` map for `Ctrl/Cmd+Z`, `Ctrl/Cmd+Shift+Z`, `Ctrl+Y`, each
`preventDefault`-ed, everything else falling through.

`type="button"` is not optional — the editor renders inside a `<form>`.

Makes T010's steps 4-7 reachable.

### T016 — [US2] The announcement region

A fourth `sr-only` `aria-live="polite"` region with its own `data-testid`.
Makes T009 green.

**Droppable.** If review on T011-T015 runs long, US2 ships as a follow-up
issue and T009 + T016 move with it. US1 is complete without it.

---

## Phase 5-6 — verification

### T017 — [US1] The guard suites, again, unmodified

Re-run T001's exact seven. All green, **no file among them edited**. Prove it:

```
git diff --stat origin/develop..HEAD -- '*OverlayEditorCharacterisation*' '*OverlayEditorKeyboard*' '*OverlayEditorBackdrop*' '*OverlayLabelCharacterisation*' '*OverlayLabelParity*' '*OverlayGeometryFields.test*' '*OverlayEditorDialogChainRetention*'
```

**Empty output is the pass.** A non-empty one is a blocker, not a diff to
explain.

### T018 — [US1] `OverlayEditorDialog.tsx` is unmodified

```
git diff --stat origin/develop..HEAD -- apps/management-web/src/features/overlays/OverlayEditorDialog.tsx
```

Empty. FR-016.

### T019 — [US1] Nothing under `src/`, nothing under `features/layouts/`

```
git diff --name-only origin/develop..HEAD | grep -E '^(src/|apps/management-web/src/features/layouts/)'
```

Empty. The first is the spec's scope; the second is PR #2373's open territory.

### T020 — [US1] Phase 5: the twelve-step manual run

Execute spec.md *An independent end-to-end test procedure* against the Aspire
stack, by hand, in a browser. Write the verification note on the PR naming
which steps were observed and what was seen — not "tests pass".

State **latency: N/A, §IV untouched**, with the reason: `OverlayEditor` is
imported by `management-web` alone, `kiosk-web` never imports it, and the
composite+render leg (ADR-0123, p50 54.2 ms against 50 ms) gains nothing.

### T021 — Phase 6: `/code-review`, then `/security-review` only if it earns it

`/code-review` on the diff. `/security-review` is **not** indicated by
default — no endpoint, no auth surface, no persisted state, no request. Say
so in the PR rather than running it for form.

---

## Delivery shape

**One PR**, carrying US1 and US2.

The split the issue suggests — undo/redo as one deliverable, revert-to-opened
as the other — **no longer exists**: revert-to-opened ships today (spec.md
Decision 1, *Contradictions* item 2), and undo-to-floor is the only remaining
form of it.

The split that *looks* available — ship the three atomic sites first, add the
three streaming sites later — **is a correctness hazard and is rejected**. If
typing is not recorded, then *drag, type, undo* silently discards the typing:
the undo restores a snapshot taken before the typing happened. An incomplete
history is not a partial feature, it is a lossy one, and shipping it would put
an operator's work at risk in exactly the situation the issue was filed about.

The split that is available and safe is **US1 vs US2** — the mechanism vs its
screen-reader announcement. They are separate stories here, in separate tasks,
against separable scenarios, so US2 can be lifted into a follow-up issue at
any point up to T016 without disturbing US1. Default: both in one PR.

## Phase 3 gate

The feature issue (#2347) must be on Project #13 before Phase 4 begins:

```
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2347
```

Per-task issues are **not** created — the practice stopped after spec 028
(CLAUDE.md §Workflow). `tasks.md` is the artefact this work is tracked
against.
