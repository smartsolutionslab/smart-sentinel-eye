# Spec 151 — Plan

**Spec:** `specs/151-a-label-you-can-place-exactly/spec.md`
**Engineer:** `frontend-engineer`. **Reviewer (phase 6):** `frontend-reviewer`.
**Phase 4a colour:** **RED** — with one named behaviour-preserving move inside it
(§5), which `OverlayEditorKeyboard.test.tsx` characterises, unmodified.

---

## Bounded context and layers

**None.** This change touches no bounded context, no `src/**` project, no
`Shared.Kernel`, no `Shared.Contracts`, no message, no endpoint, no DbContext, no
migration. The standard plan sections — **entities, value objects, invariants,
domain → integration events, cross-context boundary rules** — have **no content
here**, and stating that is the point: a reviewer should be able to confirm the
absence rather than wonder whether it was forgotten. NetArchTest's boundary rules
are untouched and not at risk.

Three server-side facts are **read and never changed**. They are the source of
the field bounds in FR-008 / FR-009, and a reviewer should check the numbers
against them:

| Type / file | Bound | Where it lands in this plan |
|---|---|---|
| `src/OverlayDesigner/Domain/Overlay/NormalizedPosition.cs` | `InRange(0m, 1m)`; **off-edge rectangles explicitly permitted** | FR-008's `[0%, 100%]`, and the whole of spec §The off-edge question |
| `src/OverlayDesigner/Domain/Overlay/NormalizedSize.cs` | `value is > 0m and <= 1m` — **zero refused** | FR-009's `(0%, 100%]` |
| `apps/shared/src/api/overlays.schema.ts` | `min(0).max(1)` / `gt(0).max(1)` | the same two, client-side; the field's message now arrives *before* submit instead of silently blocking it |

---

## Where it lives

```
apps/shared/src/ui/composites/
  normalizedPercent.ts            NEW   ~45 lines — the unit boundary (FR-016)
  OverlayGeometryFields.tsx       NEW   ~150 lines — the panel (FR-001..FR-012, FR-017)
  OverlayEditor.tsx               MOD   517 lines today; +~35, −~10
  OverlayGeometryFields.test.tsx  NEW   the red tests (T001)
e2e/
  overlays.spec.ts                MOD   35 lines today; one test added (T002)
```

**Read and not modified — a reviewer should confirm each:**

```
apps/shared/src/ui/composites/overlayLabelStyle.ts    on the §IV render leg, already over budget
apps/shared/src/ui/composites/CameraViewer.tsx        the wall label stays inert
apps/management-web/src/features/overlays/OverlayEditorDialog.tsx   the form around it
src/OverlayDesigner/Domain/Overlay/Normalized{Position,Size}.cs     read for bounds only
```

**The five guard files, and which survive unmodified:**

| Guard file | Status |
|---|---|
| `OverlayEditorCharacterisation.test.tsx` | **unmodified** — its two clamp tests exercise `onDragStop`/`onResizeStop`, which are unchanged; `clamp01` is untouched (spec §The zero-size question) |
| `OverlayEditorKeyboard.test.tsx` (697 lines) | **unmodified** — and it is the characterisation for §5's `formatPercent` move; it pins the announcement strings verbatim |
| `OverlayEditorBackdrop.test.tsx` | **unmodified** |
| `OverlayLabelParity.test.tsx` | **unmodified** — reaches the label via `getByTestId('overlay-editor-preview').parentElement`, inside the canvas; the panel is a sibling below it |
| `OverlayLabelCharacterisation.test.tsx` | **unmodified** |

**All five.** If any one of them has to change, that is evidence the behaviour
moved somewhere this plan did not intend: block and report, do not adjust
(CLAUDE.md §House rules).

### Why a new component, when spec 149 argued against one

Spec 149 declined to extract a hook for the keyboard, and was right to: the state
was "two booleans and a debounce timer" and `emitGeometry` already lived in
`OverlayEditor.tsx`. **This is a different weight**, and the difference is
stated so the divergence is reviewable rather than inconsistent:

- The panel owns **four drafts, four error messages, and a commit/revert
  protocol**. None of that has anything to do with the canvas, the backdrop, the
  WHEP capture session, or the placeholder preview that already share this file.
- `OverlayEditor.tsx` is **517 lines** and the largest composite in the folder.
  Inlining the panel would take it past 700. *(No lint rule enforces this —
  spec §What was verified item 6 — so this is a readability argument on its
  merits, not compliance. Do not tell a reviewer a rule exists.)*
- It gives the red tests a **direct unit under test**, rather than reaching
  through `react-rnd`'s mock to get at a form field.
- It is not speculative generality: one call site, no props beyond what that call
  site passes, no configuration.

The file follows `BackdropControls.tsx`, `FrameGrabber.tsx` and
`PlaceholderPreviewPanel.tsx` — internal composites imported relatively, with
**no `package.json` export entry**.

---

## The mechanism

### 1. `normalizedPercent.ts` — the unit boundary, and the only float trap

Three functions and the grid constant. This is the whole of FR-016, and the one
module a reviewer must read character by character.

```
QUANTUM = 10_000                      // the same 1e-4 grid spec 149 defined

toPercentText(normalized) -> string   // 0.2487 -> "24.87";  0.25 -> "25";  0.005 -> "0.5"
    String(Number((normalized * 100).toFixed(2)))

formatPercent(normalized) -> string   // MOVED from OverlayEditor.tsx, unchanged
    `${toPercentText(normalized)}%`   // "30%", "0.5%" — byte-identical output

parsePercent(text) -> number | null   // "24.87" -> 0.2487 exactly;  "abc" -> null
    trim; reject empty; Number(text); reject non-finite
    Math.round(percent * 100) / QUANTUM
```

**`parsePercent` must not divide by 100.** `24.87 / 100 === 0.24870000000000003`
— off-grid, latched at 17 decimals, which is exactly the class of defect spec
149's phase-6 review found in `quantizeBoundFloor`. `Math.round(percent * 100) /
QUANTUM` gives `0.2487` exactly. T001 pins this with `toBe`; **`toBeCloseTo`
passes on the defect and must not be used for that assertion.**

`Number('')` is `0` and `Number('  ')` is `0`, so the empty/whitespace rejection
is explicit and comes first. `Number('0x10')` is `16`; rejecting anything not
matching a plain decimal shape before `Number` is the simplest way to keep the
field honest, and T001 covers `"0x10"` and `"1e2"` for it.

### 2. `OverlayGeometryFields.tsx` — the panel

Props, complete:

```
value:     OverlayLabel                      the committed truth
preview:   OverlayGeometry | null            in-flight drag geometry (FR-013), or null
onCommit:  (field, normalized) -> void       one field at a time (FR-006)
```

`field` is a `'normalizedX' | 'normalizedY' | 'normalizedWidth' | 'normalizedHeight'`
union, not a string. `onCommit` is deliberately **not** `(next: OverlayLabel) =>
void`: the panel changes exactly one value and the parent builds the payload, so
the panel cannot accidentally carry a stale `text` forward.

State: `drafts: Partial<Record<Field, string>>` and `errors: Partial<Record<Field,
string>>`. A field with no draft renders `toPercentText(preview?.[field] ??
value[field])` — so an untouched field tracks the drag (FR-013) and a field being
edited shows what the operator typed (FR-004's draft model, and the fix for the
"re-render eats keystrokes" risk).

**The field row**, one per axis, driven by a small module-level table so the four
are not four copies:

```
{ field: 'normalizedX',      label: 'Left',   kind: 'position' }
{ field: 'normalizedY',      label: 'Top',    kind: 'position' }
{ field: 'normalizedWidth',  label: 'Width',  kind: 'size'     }
{ field: 'normalizedHeight', label: 'Height', kind: 'size'     }
```

The four labels are **the same strings as spec 149's `AXIS_ANNOUNCE_LABEL`**
(FR-001), so the field and its spoken announcement name the same thing. They are
not imported from `OverlayEditor.tsx` — that would be a circular import — they
are restated with a comment pointing at the reason they match. A reviewer should
check the four strings against each other.

**Commit (FR-004/FR-005/FR-010/FR-011):**

```
onBlur, and onKeyDown Enter  -> commit(field)
onKeyDown Escape             -> clear draft + error, no emit
onChange                     -> set draft (never emit)

commit(field):
  draft absent          -> nothing (a field that was never typed into)
  parsePercent -> null  -> error "Enter a number."          , draft KEPT
  kind position, outside [0, 1]      -> error FR-008        , draft KEPT
  kind size,     outside (0, 1]      -> error FR-009        , draft KEPT
  otherwise -> clear draft + error, onCommit(field, parsed)
```

The bound comparison is on the **normalized** value against `0`/`1`, not on the
percent against `0`/`100` — one representation for every bound in the codebase.
The *message* is in percent, because that is what the operator typed.

`type="text"`, `inputMode="decimal"` (FR-003 — spec §The input type states why
this is correctness, not style). No `min`, no `max`, no `step`, no `pattern`:
every one of those is a native constraint that blocks `OverlayEditorDialog`'s
form submission, and jsdom would not catch it.

**Accessibility (FR-017):** `<label htmlFor>` per field with a `useId()`-derived
id — `useId()`, not a module constant, because a document may hold two editors
(spec 149's phase-6 should-fix 8, same trap). `aria-describedby` points at the
error span only when there is one. The error span is `role="alert"`.

**The off-edge advisory (FR-012)** is a single `role="status"` node below the four
rows, derived from `value` (and `preview` when dragging) — not from a draft, and
not stored. It is `status`, not `alert`: neither the schema nor the domain calls
this a problem, and an `alert` interrupts a screen reader for something that is
not one.

**Styling:** inline, matching the text input and font-size slider already in
`OverlayEditor.tsx`. **No Tailwind class, no `Input.tsx`, no `FormField.tsx`** —
those are Tailwind-classed primitives and mixing them into an inline-styled file
creates a hybrid for #2342 (gated on #2332) to untangle. Same call spec 149 made
for its focus ring.

### 3. `OverlayEditor.tsx` — three small changes

**a. The live readout hook (FR-013/FR-014).** Two handlers added to the existing
`<Rnd>`, beside the two that are already there:

```
onDrag   (_e, data) -> setPreview({ x: data.x/W, y: data.y/H, width: cur, height: cur })
onResize (_e, _dir, ref, _delta, position) -> setPreview(... from ref/position ...)
onDragStop   -> setPreview(null); emitGeometry(...)    unchanged emission
onResizeStop -> setPreview(null); emitGeometry(...)    unchanged emission
```

`preview` is a `useState<OverlayGeometry | null>`. **`onChange` is not called
from `onDrag`/`onResize`** — that is FR-014, and it is the requirement that keeps
mouse-move rate away from `OverlayEditorDialog`'s React Hook Form.

The preview values go through **`clamp01`, the same function the stop handlers
use**, so the readout during a drag cannot show a number the release would not
produce. It is a readout of the value being produced, not of the pointer.

**b. The panel, rendered below the existing controls grid**, after the font-size
slider and before `PlaceholderPreviewPanel`. Below the canvas `div`, as spec 149
put its two `sr-only` nodes, and for the same reason: `OverlayLabelParity.test.tsx`
reaches the label through the canvas subtree, and nothing there may gain a
sibling.

**c. `handleGeometryCommit`** — a `useCallback` that maps one field to one
`onChange`:

```
(field, normalized) => onChange({ ...value, [field]: normalized })
```

**Not** through `emitNormalized`. That is deliberate and a reviewer should
confirm it:

- `emitNormalized` applies `clamp01` to all four values. The committed value is
  **already** validated tighter than `clamp01` by the panel (FR-008/FR-009), so
  `clamp01` would be a no-op on it — but on the *other three* it would silently
  rewrite a stored off-grid or out-of-range value the operator never touched.
  FR-006 says the other three travel forward untouched.
- Routing through it would also mean a typed `0` hitting `clamp01`'s zero, which
  spec §The zero-size question is at pains to keep unreachable.

The spread is one line and needs no clamp, because the panel has already done
strictly more.

### 4. `clamp01` is not touched

Stated as a mechanism item because its absence is the decision. `clamp01`
continues to bound each of the four values to `[0, 1]` independently for the drag
path, permitting zero sizes (#2361) and off-canvas rectangles. Typed entry never
reaches either: the zero is refused with a message (FR-009) and the off-canvas
rectangle is deliberately allowed (FR-012). See spec §The zero-size question for
why fixing #2361 here would cost a characterisation edit for a defect this PR is
not about.

### 5. The one behaviour-preserving move, named

`formatPercent` moves from `OverlayEditor.tsx` into `normalizedPercent.ts` and is
imported back. Its body is unchanged; its output must be **byte-identical**.

This is a **refactor inside a red spec**, so it carries the other obligation
(constitution §Testing): its covering test is `OverlayEditorKeyboard.test.tsx`,
which pins the announcement strings verbatim — `'Width 0.5%, at the minimum'`,
`'Left 30%'` — and which **must pass unmodified, before and after**. It is
already green on `develop`, so it is captured green by running it; nothing new
needs writing for it. An assertion there that has to be edited is evidence the
move was not pure: block.

`AXIS_ANNOUNCE_LABEL`, `edgeName` and `buildAnnouncement` **do not move**. Only
`formatPercent` does, and only because two modules now need it.

---

## Boundary rules for this change

1. **No file under `src/**` is modified.** The two `Normalized*` value objects
   and the Zod schema are read for their bounds.
2. **`overlayLabelStyle.ts` is not modified and gains no caller.** It is on the
   composite-and-render leg, which ADR-0123 records at p50 **54.2 ms against a
   50 ms allowance** — already over. Any diff to it is out of scope for this
   issue.
3. **`CameraViewer.tsx` is not modified.** The wall label stays non-interactive.
4. **No Redux.** All five guards render `OverlayEditor` bare; a `useSelector`
   anywhere in this change breaks all five at once.
5. **No new package.** `apps/shared` has no React Hook Form and no `user-event`
   (spec §What was verified item 5). Validation is hand-written; tests use
   `fireEvent`.
6. **No design tokens, no Tailwind class, no `Input`/`FormField` primitive.**
   #2342 owns that conversion and is gated on #2332.
7. **No `package.json` export entry** for the new files — they are internal
   composites, imported relatively, following three existing precedents.
8. **`onChange` fires at most once per operator action**, and never during a
   drag.

---

## Parallelism (ADR-0109)

Small. The honest answer is that this is nearly serial, and saying so is better
than inventing fan-out:

- **T001 (unit tests) and T002 (e2e) own disjoint files** and are genuinely `[P]`.
- **T003/T004/T005 are one engineer in one file set**, sequenced by T001's red
  output, which is their brief.
- **T007 (the follow-up issue + the #2361 comment) is `[P]` with everything** —
  it touches no file in the repo.

There is no foundational `Shared.Kernel` / `Shared.Contracts` / AppHost task to
block on, because nothing server-side changes.

---

## Risks

| Risk | Handling |
|---|---|
| `parsePercent` divides by 100 | §1 names the wrong expression; T001 asserts `toBe(0.2487)`. Review must check the operator, not just the test's green |
| The engineer reaches for `type="number"` because it is the obvious control | §2 and spec §The input type. jsdom cannot catch the consequence; test-procedure step 10 is the only net |
| The live readout is wired through `onChange` | FR-014; T001 asserts `onChange` is not called during `onDrag`. If the dialog's form starts re-validating at mouse-move rate the symptom is a laggy drag, which is easy to misread as `react-rnd` |
| The commit is routed through `emitNormalized` "for consistency" | §3c states why not: it would rewrite three values the operator did not touch |
| The four field labels drift from `AXIS_ANNOUNCE_LABEL` | They are restated, not imported (circular). A reviewer compares the four strings; the alternative — exporting them from `OverlayEditor.tsx` into a module the editor imports — is a cycle, and moving them into `normalizedPercent.ts` would put UI copy in a numeric module |
| `formatPercent`'s move changes an announcement | §5; `OverlayEditorKeyboard.test.tsx` unmodified is the whole guard |
| #2345 merges first | It carries no code today. Rebase and re-run all five guards |
| The panel's `useId` collides across two editors in one document | `useId()` per instance, not a module constant — spec 149 hit exactly this |
