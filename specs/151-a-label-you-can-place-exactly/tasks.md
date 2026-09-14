# Spec 151 — Tasks

**Spec:** `specs/151-a-label-you-can-place-exactly/spec.md`
**Plan:** `specs/151-a-label-you-can-place-exactly/plan.md`
**Issue:** #2346. **Board:** the feature issue goes on Project #13 by hand —
`/speckit-tasks` adds nothing (CLAUDE.md §Workflow, Phase 3 gate).

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2346
```

**Phase 4a colour: RED.** Every task below produces or verifies new behaviour,
with **one** named behaviour-preserving move (T005) whose characterisation is
`OverlayEditorKeyboard.test.tsx`, captured green and required to pass unmodified.

**`[P]` = disjoint files, may run concurrently (ADR-0109).**

---

## Phase 4a — tests first, observed RED

### T001 [US1] — The red unit tests

**Agent:** `test-writer`. **File (new):**
`apps/shared/src/ui/composites/OverlayGeometryFields.test.tsx`
**Depends on:** nothing.

Write tests only. Run them. Return the **verbatim** failing output — it is the
engineer's brief and it is quoted in the PR body (ADR-0139, ADR-0144).

Mock `react-rnd` the way `OverlayEditorCharacterisation.test.tsx` does (a stub
that captures props and renders `children`) — jsdom has no layout engine, so a
real drag reports 0×0 and `{x:0,y:0}` regardless of input, and the header comment
in that file explains why at length. Use `fireEvent`; `apps/shared` has no
`user-event`.

Cover, at minimum, one test per scenario in spec §Acceptance scenarios, plus
these, which are about the arithmetic rather than the behaviour:

- `parsePercent('24.87')` is **`toBe(0.2487)`** — **not `toBeCloseTo`**.
  `toBeCloseTo` passes on the `/100` defect this assertion exists to catch.
- `parsePercent('25')` is `toBe(0.25)`; `parsePercent('0.5')` is `toBe(0.005)`;
  `parsePercent('0.01')` is `toBe(0.0001)` — the bottom of the grid.
- `toPercentText(0.2487142)` is `'24.87'` — an off-grid stored value displays at
  grid resolution.
- `toPercentText(0.25)` is `'25'`, not `'25.00'`.
- Round trip: for each of `0`, `0.0001`, `0.005`, `0.25`, `0.3333`, `1`,
  `parsePercent(toPercentText(v)) === v`.
- Rejected drafts: `''`, `'   '`, `'abc'`, `'0x10'`, `'1e2'`, `'--5'`, `'NaN'`,
  `'Infinity'` all yield the FR-007 message and emit nothing.
- `formatPercent` still returns `'30%'` for `0.3` and `'0.5%'` for `0.005` —
  the move in T005 is pinned from the new module's side too.

And these, which are about the wiring and need `OverlayEditor` rendered rather
than the panel alone:

- FR-013: calling the stub's `onDrag` with `{x: 400, y: 0}` on the default
  800×450 canvas makes the Left field read `'50'`.
- FR-014: that same call leaves `onChange` **not called**.
- FR-006: committing `'33.33'` into Left calls `onChange` **once**, with
  `normalizedX` exactly `0.3333` and `normalizedY` / `normalizedWidth` /
  `normalizedHeight` / `text` / `fontSizePx` unchanged.

**Definition of done:** every test fails, for the right reason (the module or the
field does not exist — not a typo), and the output is captured verbatim.

---

### T002 [P] [US1] — The red e2e

**Agent:** `test-writer`. **File (modified):** `e2e/overlays.spec.ts` (35 lines
today). **Depends on:** nothing. **Disjoint from T001.**

Add one Playwright test to the existing overlays spec, alongside the two that are
there. It must prove what jsdom cannot: that a typed percentage survives real
form submission and the `double` → `decimal` boundary to the server.

```
operator types an exact geometry and the saved overlay carries it
  sign in as operator -> Overlays -> New overlay
  fill Left with "24.87", Tab
  fill Width with "50",   Tab
  expect no role="alert" on the panel
  name the overlay, Save as draft
  expect it in the list
  read it back through the gateway and expect normalizedX === 0.2487 exactly
```

Follow the existing file's conventions: `signInAsOperator`,
`FIRST_WRITE_TEST_TIMEOUT_MS` / `FIRST_WRITE_TIMEOUT_MS` for the cold-stack
write, a `Date.now()`-suffixed name, and `e2e/support/archive-e2e-overlays.teardown.ts`
already cleans up.

**Note for the author:** there is no edit dialog — `OverlayEditorDialog` is
create-only — so the read-back is an API call, not a re-open. Use the same
authenticated request shape the other specs use; mint the token from Aspire's
**proxied** endpoint, never the container's mapped port.

**Definition of done:** the test fails because the Left field does not exist.

---

## Phase 4b — implementation

### T003 [US1] — `normalizedPercent.ts`

**Agent:** `frontend-engineer`. **File (new):**
`apps/shared/src/ui/composites/normalizedPercent.ts`
**Depends on:** T001 (its failing output is the brief).

`QUANTUM`, `toPercentText`, `parsePercent`, and `formatPercent` (moved in T005).
Per plan.md §1.

**`parsePercent` must not divide by 100.** `Math.round(percent * 100) / QUANTUM`.
Reject empty/whitespace before `Number`, and reject anything that is not a plain
decimal shape (`0x10` and `1e2` are `Number`-parseable and are not what an
operator typed).

No `package.json` export entry — internal module.

---

### T004 [US1] — `OverlayGeometryFields.tsx`

**Agent:** `frontend-engineer`. **File (new):**
`apps/shared/src/ui/composites/OverlayGeometryFields.tsx`
**Depends on:** T003.

Per plan.md §2. FR-001–FR-012 and FR-017.

The four non-negotiables, each of which a reviewer will check:

1. **`type="text"` + `inputMode="decimal"`.** No `min`, `max`, `step` or
   `pattern`. Every one of those is a native constraint that blocks the enclosing
   `<form>`'s submit, and no unit test can see it (spec §The input type).
2. **Drafts are kept on refusal** (FR-010), and the label does not move.
3. **Bounds are compared on the normalized value against `0`/`1`**; only the
   *message* is in percent.
4. **The advisory is `role="status"`**, the errors are `role="alert"`.

Inline styles, matching the text input and font-size slider already in
`OverlayEditor.tsx`. No Tailwind class, no `Input.tsx`, no `FormField.tsx`.

`useId()` per instance for the label/error ids, never a module constant.

---

### T005 [US1] — Wire it into `OverlayEditor.tsx`, and move `formatPercent`

**Agent:** `frontend-engineer`. **File (modified):**
`apps/shared/src/ui/composites/OverlayEditor.tsx`
**Depends on:** T004.

Per plan.md §3 and §5. Three changes plus one move:

- `onDrag` / `onResize` handlers setting a `preview` state, cleared by the two
  existing `*Stop` handlers; preview values pass through `clamp01` (FR-013).
- **No `onChange` from `onDrag`/`onResize`** (FR-014).
- `handleGeometryCommit` spreading one field — **not** through `emitNormalized`
  (plan.md §3c gives the reason; a reviewer will ask).
- `<OverlayGeometryFields>` rendered below the existing controls grid, before
  `PlaceholderPreviewPanel`. **Not inside the canvas `div`.**
- `formatPercent` moved out and imported back. Body unchanged, output
  byte-identical.

**`clamp01` is not touched.** Its zero (#2361) and its independent per-axis
bounds are both deliberate here.

---

## Phase 4c — the guards

### T006 [US1] — All five guards pass unmodified, plus lint and typecheck

**Agent:** `frontend-engineer`. **Files:** none modified.
**Depends on:** T005.

Run, and report each by name:

```
apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx
apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx
apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx
apps/shared/src/ui/composites/OverlayLabelParity.test.tsx
apps/shared/src/ui/composites/OverlayLabelCharacterisation.test.tsx
```

Then `npm run lint` and `npm run typecheck` in `apps/shared`, and the e2e
typecheck (`typecheck:e2e` fails on a clean `develop` for an unrelated missing
`@types/node`; stash and re-run before blaming this branch).

`OverlayEditorKeyboard.test.tsx` is doing double duty: it is a guard **and** it is
the characterisation for T005's `formatPercent` move. Capture it green before
T005 and green after.

**If any of the five needs an edit, stop and report.** That is evidence the
behaviour moved somewhere the plan did not intend, and the plan says all five
survive. Do not adjust the test.

---

## Phase 3 follow-ups (no code)

### T007 [P] — File the follow-up issue and comment on #2361

**Agent:** the orchestrator, or `frontend-engineer` at PR time.
**Files:** none. `[P]` with everything.

1. **File the snapping/guides issue** per spec §Out of scope — *"An overlay label
   snaps to the canvas's edges, thirds and centre, and says what it snapped to"*,
   `enhancement`, related to #2346 / #2345 / #2344. Carry over the three design
   notes: percent is already the unit; FR-013's continuous-drag state is the hook
   it extends; snap thresholds belong in **pixels**, not normalized units. Add it
   to Project #13. Do **not** add `agent:ready` — that is the human's gate.
2. **Comment on #2361**: the typed-entry exposure it predicted is closed by
   validation (FR-009 refuses a typed `0` with a message) rather than by a clamp,
   so its remaining scope is the **drag path only**, and
   `OverlayEditorCharacterisation.test.tsx:156` is the assertion that must move
   when it is fixed.

---

## Dependency graph

```
T001 ─┐
      ├─> T003 ──> T004 ──> T005 ──> T006
T002 ─┘                                │
                                       └──> PR
T007  (independent of everything)
```

`[P]` pairs: **T001 ‖ T002**, and **T007 ‖ all**.

Everything else is one engineer in one file set, sequenced by the red output.
There is no foundational `Shared.Kernel` / `Shared.Contracts` / AppHost task,
because nothing server-side changes — which is also why there is no fan-out to
offer.

---

## Gate (Phase 3, ADR-0037)

- [ ] Tasks atomic and each names its files
- [ ] `[P]` markers are file-disjoint, and the two that are not are said so
- [ ] Issue **#2346** on Project #13 (feature-level, not per-task — CLAUDE.md;
      verify with `--limit 2000`, the default 30 makes a filled board look empty)
- [ ] Phase 4a colour declared: **RED**, with T005's move characterised by
      `OverlayEditorKeyboard.test.tsx`
- [ ] Spec references ≥ 1 ADR — it references twelve
