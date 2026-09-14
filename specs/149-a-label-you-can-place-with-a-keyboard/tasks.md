# Spec 149 — Tasks

**Engineer:** `frontend-engineer`. **Reviewer (phase 6):** `frontend-reviewer`.
**No security review.** `OverlayEditor` is presentational: it calls `onChange`
and nothing else. No endpoint, no scope, no token, no header, no trust boundary
changes (spec §Acceptance → Auth / scope).

**Format:** `[ID] [P?] [Story]`. `[P]` = disjoint files, safe to run in parallel
(ADR-0109).

---

## Phase 4a — the colour, declared

**The new tests are RED. The three existing guards are GREEN, unmodified.**

This is one behaviour-changing slice, so ADR-0144's default applies to
everything new: `OverlayEditorKeyboard.test.tsx` must be **observed failing
before any production edit**, and the verbatim output quoted in the PR body
(ADR-0139).

**The red must be a missing control, not a missing module.** The new file
imports the *existing* `OverlayEditor` and queries
`screen.getByTestId('overlay-editor-label')` — which fails with "unable to find
an element", a real absence. A test that failed on an import error would be
evidence of nothing (spec 147 plan.md, "the trap in the red").

The three files that already exist are the behaviour-preserving half. They are
**not rewritten and not extended** — they are run before T004 and again after
T009, and both runs are quoted:

- `OverlayEditorCharacterisation.test.tsx` (spec 147 T001) — pins the drag path,
  which T005's `emitGeometry` split must not move
- `OverlayLabelParity.test.tsx` (spec 146) — pins the label's rest appearance,
  including `border`, which the focus ring must not become
- `OverlayEditorBackdrop.test.tsx` (spec 147 T002)

**An assertion in any of the three that has to be edited is evidence the
behaviour moved — block, do not adjust** (CLAUDE.md, §Testing).

### The one testability constraint that will bite

`OverlayEditorCharacterisation.test.tsx` mocks `react-rnd` as
`Rnd: (props) => props.children` — **it renders no element**. The new test file
must **not** copy that mock; it uses the real `react-rnd`, which
`OverlayLabelParity.test.tsx` already proves mounts cleanly in jsdom. Keyboard
events need no layout, so jsdom's all-zero `getBoundingClientRect` and zero
`offsetWidth`/`offsetHeight` — the reason spec 147 stubbed at all — are
irrelevant here.

### What is genuinely testable, and what is not

**Testable in jsdom (T001–T003, and the gate for phase 4b):** focusability; the
`role` / `aria-roledescription` / `aria-label` / `aria-describedby` attributes
and the described text; the exact `onChange` payload for all sixteen
arrow × modifier combinations; the `Shift` ×10 ratio; top-left-anchored resize;
all four position clamps and the size floor; the 1e-4 quantum over 160 presses;
`text` / `fontSizePx` carried through; `defaultPrevented` true for handled keys
and false for `Tab` / `Escape` / `Enter` / a letter / `Alt`+arrow; the debounced
live region under `vi.useFakeTimers()`; the focus ring appearing on `focus` and
gone on `blur`; that the ring is not a `border`.

**Not testable in jsdom — phase 5 or nothing (T011):**

1. **The ring's contrast** against the checkerboard and against a real captured
   camera frame. jsdom paints no pixels. Two screenshots are the only evidence
   FR-002 will ever have.
2. **That `bounds="parent"` and FR-007 agree.** jsdom has zero layout, so the
   claim "the keyboard reaches exactly what the drag reaches" is asserted by
   construction in code and confirmed by eye.
3. **That `role="application"` makes NVDA/JAWS pass arrow keys through**, and
   that the live region is actually spoken. If no screen reader is available on
   the machine, the verification note **says it was not verified** — it does not
   claim it.
4. **That `Ctrl`+arrow is free of browser and Radix Dialog conflicts.**

---

## Phase 4a — write the tests, observe the colours

- [ ] **T001 [US1]** — Run the three existing guards unchanged and **record them
      GREEN**, before touching anything:
      `pnpm --filter @smart-sentinel-eye/shared test -- OverlayEditorCharacterisation OverlayEditorBackdrop OverlayLabelParity`.
      Quote the output in the PR. This is the baseline the post-change run is
      compared against.

- [ ] **T002 [P] [US1]** — Write `apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx`
      (`// @vitest-environment jsdom`, **real `react-rnd`, no mock**,
      `fireEvent.keyDown`, **no `user-event`** — it is not a dependency of
      `apps/shared`). Cover the *movement and sizing* half:
      1. **FR-001** — `overlay-editor-label` exists, carries `tabIndex={0}`, and
         takes focus.
      2. **FR-004** — each of the four arrows moves its axis by `0.005` in the
         right direction, one `onChange` per press.
      3. **FR-005** — `Shift`+arrow moves by `0.05`; assert the ratio, not just
         the value.
      4. **FR-006** — `Ctrl`+arrow changes width/height by `0.005`,
         `Ctrl`+`Shift` by `0.05`, and `normalizedX`/`normalizedY` are
         **unchanged** in every resize case.
      5. **FR-007** — a press at each of the four edges is refused:
         `x=0`+`ArrowLeft`, `y=0`+`ArrowUp`, `x=1-w`+`ArrowRight`,
         `y=1-h`+`ArrowDown`. And growth stops at the canvas edge:
         `x=0.8, w=0.19` + `Ctrl`+`Shift`+`ArrowRight` → `w=0.2`, not `0.24`.
      6. **FR-008** — `w=0.005` + `Ctrl`+`ArrowLeft` → `0.005`, not `0`.
      7. **FR-009** — 160 `ArrowRight` presses from `x=0, w=0.2` land on
         **exactly** `0.8`, and no intermediate value exceeds four decimals.
         Drive it by feeding each `onChange` payload back as the next `value`.
      8. **FR-010** — `text` and `fontSizePx` survive every keyboard press.
      9. **FR-011 / bad request** — `Tab`, `Escape`, `Enter`, `"a"` and
         `Alt`+`ArrowLeft` fire no `onChange` and are **not** `preventDefault`-ed;
         `ArrowRight` and `Ctrl`+`ArrowRight` **are**.
      **Observe RED. Quote the failure.**

- [ ] **T003 [P] [US2]** — Extend the same file (or a second `describe` in it)
      with the *assistive-technology* half. Same file so the two halves cannot
      drift; `[P]` against T002 only if they are written as separate files —
      **they are not**, so T003 follows T002 in the same file:
      1. **FR-012** — `aria-roledescription="Overlay label"`; `aria-label` names
         the label by its text, and falls back to a fixed string when `text` is
         `''`.
      2. **FR-013** — `role="application"` is present on the label element.
         (Attribute only. What NVDA does with it is T011.)
      3. **FR-014** — an `sr-only` node states the key map, and the label's
         `aria-describedby` resolves to it. Assert the *resolution* — the id
         matches an element that exists — not just that the attribute is a
         string.
      4. **FR-015** — under `vi.useFakeTimers()`: ten `ArrowRight` presses inside
         500 ms produce ten `onChange` calls and, after advancing 500 ms,
         **exactly one** live-region write naming the left position as a
         percentage.
      5. **FR-016** — a refused press announces its edge
         (`…, at the left edge` / `…, at the minimum`).
      6. **FR-017** — the live region is empty on mount and after a `focus` with
         no keypress.
      7. **FR-002 / FR-003** — the label has no ring at rest, gains `outline` and
         `boxShadow` on `focus`, loses them on `blur`, and **never sets
         `border`** in any state.
      8. **FR-018** — the whole file renders `OverlayEditor` with **no Redux
         `<Provider>`**; that it passes at all is the assertion.
      **Observe RED. Quote the failure.**

---

## Phase 4b — implement against the quoted red

The engineer receives T001–T003's verbatim output as its brief and **may not
edit those tests to pass** (ADR-0144).

- [ ] **T004 [US1]** — Add the step constants and the key map to
      `OverlayEditor.tsx`: `FINE_STEP = 0.005`, `COARSE_STEP = 0.05`,
      `MIN_NORMALIZED_SIZE = 0.005`, `QUANTUM = 10_000`,
      `ANNOUNCE_DELAY_MS = 500`. One comment saying *why* `0.005` and why
      `Ctrl` — the reasoning lives in `spec.md` §The step grid and §The keyboard
      map and is referenced, not restated (CLAUDE.md, no drive-by comments).

- [ ] **T005 [US1]** — Split `emitGeometry` into a normalized-input
      `emitNormalized(x, y, width, height)` and keep `emitGeometry(px…)` as its
      pixel-converting caller. **Behaviour-preserving.** Re-run
      `OverlayEditorCharacterisation.test.tsx` immediately and confirm it is
      still green, unmodified, before going further — this is the one step in
      the slice that could move the drag path, and catching it here is cheap.

- [ ] **T006 [US1]** — Add `nudge(axis, delta)` and `resize(axis, delta)` pure
      helpers implementing plan.md §2's clamp order exactly:
      `quantize → clamp01 → reachable-region → (resize only) size floor`.
      They take and return normalized numbers and touch no DOM.

- [ ] **T007 [US1]** — Add the `onKeyDown` handler and wire `tabIndex={0}`,
      `data-testid="overlay-editor-label"` and the handler onto `<Rnd>`. Early
      return for non-arrows and for `altKey`/`metaKey`; `preventDefault()` only
      on a handled combination. Do **not** filter `event.repeat`.

- [ ] **T008 [US2]** — Add `role="application"`, `aria-roledescription`,
      `aria-label` and `aria-describedby` to `<Rnd>`; add the `sr-only`
      instructions node and the `aria-live="polite"` node as **siblings below
      the canvas `div`**, not inside `<Rnd>` (plan.md §6 — the parity guard
      reaches the label through `overlay-editor-preview`'s `parentElement`, and
      PR #2360 rewrites that same `<span>`). Add the debounce `useRef` timer and
      clear it on unmount.

- [ ] **T009 [US2]** — Add the focus-ring state (`onFocus`/`onBlur`) and merge
      the two-ring style into `<Rnd>`'s `style` **after**
      `overlayLabelSurfaceStyle(value)`. `outline` + `outlineOffset` +
      `boxShadow`. **Never `border`.**

- [ ] **T010 [US1+US2]** — Run the full shared suite plus lint and typecheck:
      `pnpm --filter @smart-sentinel-eye/shared test`,
      `pnpm --filter @smart-sentinel-eye/shared lint`,
      `pnpm --filter @smart-sentinel-eye/shared typecheck`, and the
      `management-web` suite (it hosts `OverlayEditorDialog`). **All four
      overlay test files green; the three pre-existing ones unmodified.** Quote
      T001's baseline and this run side by side in the PR.

---

## Phase 5 — what only a browser can settle

- [ ] **T011 [US1+US2]** — Execute `spec.md` §Independent end-to-end test
      procedure against the Aspire stack, and write the verification note.
      It **must** carry:
      - the two screenshots from steps 2 and 6 — the focus ring over the
        checkerboard and over a **bright** captured frame. These are the only
        evidence FR-002 has;
      - the four screenshots from step 9 (added at phase 6 review,
        should-fix 3) — the ring at each of the four canvas edges, unclipped
        by the canvas's `overflow: hidden`;
      - step 4's result stated as a fact: the label stopped at the canvas edge
        and did not leave it;
      - step 8's `PATCH` status and the decimal places in the payload;
      - **step 7 honestly.** If NVDA or JAWS was not available, the note says
        *"FR-013 unverified — no screen reader on this machine"*. It does not
        say "works with screen readers". A claim nobody checked is exactly the
        failure mode this repo has had to correct repeatedly;
      - **latency: N/A.** The editor is on no §IV leg. Say it, with the reason,
        rather than omitting the line.

---

## Phase 6

- [ ] **T012** — `frontend-reviewer`. Point it at plan.md §Boundary rules and
      ask specifically: is `overlayLabelStyle.ts` untouched, is `CameraViewer`
      untouched, is there any `useSelector`, any new package, any design token,
      and does the label set `border` in any state.

---

## Dependencies and parallelism

```
T001 ─┐
      ├─> T002 ─> T003 ─> T004 ─> T005 ─> T006 ─> T007 ─┬─> T008 ─> T009 ─> T010 ─> T011 ─> T012
      └──────────────────────────────────────────────────┘
```

**Nothing here is parallel, and that is the correct answer rather than a missed
opportunity.** ADR-0109 marks `[P]` for *disjoint files*. This slice is one
production file and one new test file, written by one engineer in one order:
tests red, then the split that must not move the drag path, then the helpers,
then the wiring. T002 and T003 are marked `[P]` only in the sense that they are
independent *subjects*; they land in the same file, so they are sequential.

**The foundational task is T005** — the `emitGeometry` split. Everything after it
depends on `emitNormalized` existing, and it is the only step that can silently
break the drag path. Its own checkpoint (re-run the characterisation immediately)
is why it is a task and not a line inside T006.

---

## Phase 3 gate (ADR-0037, as corrected in CLAUDE.md)

Per-task issues stopped after spec 028. The gate is **the feature's issue on
Project #13** — #2344 is already on the board (`Smart Sentinel Eye`,
*In Progress*, labelled `agent:ready`), confirmed by `gh issue view 2344`. No
`/speckit-taskstoissues` run; nothing to add.
