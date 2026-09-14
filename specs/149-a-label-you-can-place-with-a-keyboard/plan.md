# Spec 149 — Plan

**Spec:** `specs/149-a-label-you-can-place-with-a-keyboard/spec.md`
**Engineer:** `frontend-engineer`. **Reviewer (phase 6):** `frontend-reviewer`.

---

## Bounded context and layers

**None.** This change touches no bounded context, no `src/**` project, no
`Shared.Contracts`, no message, no endpoint, no migration. The standard
plan sections — entities, value objects, invariants, domain → integration
events, cross-context boundary rules — have **no content here**, and saying so
explicitly is the point: a reviewer should be able to confirm the absence rather
than wonder whether it was forgotten.

The only server-side facts this plan depends on are **read, never changed**:

| Type | Bound | Why it matters here |
|---|---|---|
| `src/OverlayDesigner/Domain/Overlay/NormalizedPosition.cs` | `[0, 1]` per axis; off-edge rectangles **permitted** | FR-007's bound is the *editor's*, not the domain's |
| `src/OverlayDesigner/Domain/Overlay/NormalizedSize.cs` | `(0, 1]` — **zero refused** | FR-008's `0.005` floor exists so the keyboard cannot mint an unsaveable label |

The architecture boundary rules (NetArchTest, no cross-context references) are
untouched and not at risk.

---

## Where it lives

One production file changes:

```
apps/shared/src/ui/composites/OverlayEditor.tsx   (222 lines today)
```

One production file is **read and not modified**, and a reviewer should check
that:

```
apps/shared/src/ui/composites/overlayLabelStyle.ts   -- on the §IV render leg
apps/shared/src/ui/composites/CameraViewer.tsx       -- the wall label stays inert
```

One test file is added; three existing test files are **run and not edited**:

```
apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx        NEW
apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx  unchanged
apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx          unchanged
apps/shared/src/ui/composites/OverlayLabelParity.test.tsx             unchanged
```

### Why it is not a new component

The obvious alternative — a `useOverlayKeyboardGeometry` hook, or a
`KeyboardNudgeTarget` wrapper — buys nothing. There is exactly one call site,
the state is two booleans and a debounce timer, and `emitGeometry` already lives
in this file. ADR-0036: no speculative generality. If #2346 or #2345 later needs
the same arithmetic from a second place, extracting it then is a five-line
refactor with four test files already standing behind it.

---

## The mechanism

### 1. The geometry writer splits in two, and the split is the whole design

Today `emitGeometry(xPx, yPx, widthPx, heightPx)` converts pixels → normalized
and clamps with `clamp01`. The keyboard's steps are **already normalized**, so
routing them through pixels and back would add a float round-trip for nothing.

```
emitGeometry(px…)        →  converts to normalized  →  emitNormalized(…)   [drag path]
keyboard step            →  quantize, clamp region  →  onChange(…)         [key path]
```

`emitNormalized` becomes the single place that builds the `onChange` payload,
and `clamp01` stays the one clamp function both paths call — which is how FR-007
keeps requirement 4's letter while FR-007/FR-008's reachable-region and minimum
bounds sit *inside* it on the keyboard path only.

**The drag path's observable behaviour must not move.** `emitGeometry`'s output
for every input is identical after the split — that is exactly what
`OverlayEditorCharacterisation.test.tsx` pins, unmodified.

### 2. Clamp order, stated once so it is reviewable

**Corrected at phase 6 review (findings 1 and 2).** The order below is what
`OverlayEditor.tsx` implements; the version this plan originally specified had
two defects the review caught and the corrected implementation fixes — recorded
here rather than silently overwritten, because the plan is what a reviewer
checks the code against.

**Finding 1 — the bound must be quantized too, not just the step.** `1 - s` is
arithmetic on an already-quantized `s`, but IEEE-754 lands it off-grid for a
large fraction of grid-valid sizes (`1 - 0.8 === 0.19999999999999996`), and the
bound was applied *last* — so an unquantized bound silently defeated the
quantum at exactly the boundary the quantum exists for. The fix floors the
bound to the grid, with a small epsilon so a bound that is mathematically exact
but float-noisy (the `0.8` case above) still lands on its true grid point
instead of one step short, while a genuinely off-grid bound (an origin/size
from a drag) still floors *inward*, never past the true edge.

**Finding 2 — the bound must clamp the *motion*, not the resulting *value*.**
`NormalizedPosition`/`NormalizedSize` bound each axis to `[0, 1]` independently
and do not relate position to size, so the domain permits a label already
off-canvas (`x + width > 1`) — not reachable by drag, but exactly the shape
#2346's typed entry produces routinely. Clamping the *value* to the bound (the
original `Math.min(stepped, bound)`) snapped such a label onto the bound on the
very first keypress — including an `ArrowRight` (nominally rightward) that
moved the label 40% of the canvas to the *left*, and a shrink that dropped 80%
of the width in one press, both silent (no refusal announced). The fix
compares against `current`, not just the bound: a press that would move the
value further out is refused (the result stays at `current`); a press moving
toward the region is unclamped by this rule and proceeds normally.

For a **move**, per axis, given the axis's current size `s`:

```
stepped = clamp01(quantize(current ± step))    FR-009, 4 dp; FR-007, the same
                                                function the drag uses
bound   = quantizeBoundFloor(max(0, 1 - s))    FR-007, the bounds="parent"
                                                equivalent, floored to the grid
next    = delta <= 0
            ? stepped
            : min(stepped, max(bound, current))  refuse only a press that would
                                                   move further out than `current`
```

For a **resize**, per axis, given the axis's current origin `o`:

```
stepped = clamp01(quantize(current ± step))    FR-009; FR-007
floored = max(stepped, 0.005)                  FR-008, NormalizedSize refuses
                                                zero — applied *before* the
                                                canvas bound (finding 2a: an
                                                outermost floor can win over the
                                                bound and mint an off-canvas
                                                label at a near-edge origin;
                                                canvas containment must win)
bound   = quantizeBoundFloor(max(0, 1 - o))    FR-007, stay inside the canvas,
                                                floored to the grid
next    = delta <= 0
            ? floored
            : min(floored, max(bound, current))  same motion-not-value rule
```

`max(0, 1 - s)` (and `1 - o`) is guarded because `s`/`o` can exceed 1 in props
(the component is controlled and a caller may hand it anything); `Math.max`
keeps the target non-negative instead of producing an inverted range.

### 3. Key dispatch

A single `onKeyDown` on the `<Rnd>`. It returns immediately unless the key is
one of the four arrows; `Alt` or `Meta` held also returns immediately, so
`Alt`+`ArrowLeft` stays browser Back (spec §The keyboard map). Otherwise:

```
step     = event.shiftKey ? 0.05 : 0.005
resizing = event.ctrlKey
```

then `preventDefault()`, compute, `onChange`, and queue the announcement.
`event.repeat` is **not** filtered — a held key should keep moving the label,
which is how every nudge affordance behaves and what test-procedure step 4
observes.

### 4. Focus ring

`useState<boolean>` set by `onFocus`/`onBlur`, merged into the `<Rnd>`'s existing
`style` object after `overlayLabelSurfaceStyle(value)` so the parity guard's rest
state is untouched. Two rings, `outline` for the inner and `boxShadow` for the
outer — **not** `border`, which `OverlayLabelParity.test.tsx` compares against
the wall.

### 5. Announcement

`useState<string>` for the message plus a `useRef` timer. Every geometry keypress
clears and restarts a 500 ms timeout; the timeout writes the message. The timer
is cleared on unmount. One utterance per burst (FR-015).

The message is built from **which value the press targeted** and whether the
clamp refused it — not from a diff of all four values, which would be both
noisier and wrong when a press lands on an already-clamped edge.

### 6. Where the two extra nodes go

Both the `sr-only` instructions node (FR-014) and the `aria-live` node (FR-015)
are rendered **outside** the canvas `div`, as siblings below it — not inside the
`<Rnd>`. Two reasons: `OverlayLabelParity.test.tsx` reaches the label element via
`getByTestId('overlay-editor-preview').parentElement`, and PR #2360 rewrites that
same `<span>`. Keeping the `<Rnd>` subtree to its one existing child keeps both
of those out of the way.

---

## Why `react-rnd` needs no work at all

Verified in `node_modules`, not assumed (spec §What was verified, finding 1):

- `Rnd.prototype.render` destructures its own props by name and forwards the
  **rest** to `re-resizable`'s `Resizable`.
- `Resizable.prototype.render` filters its props against a `definedProps`
  allow-list and spreads the remainder onto the rendered `div`.
- `tabIndex`, `role`, `aria-*`, `onKeyDown`, `onFocus`, `onBlur` and
  `data-testid` are in neither list.

So the accessibility attributes and the key handler land on the real element,
and because `<Rnd>` is already given `position` and `size` from `value`, a
keyboard `onChange` re-renders it into place through the same path a drag uses.
**The alternative that was considered and rejected** — a separate absolutely
positioned focusable element layered over the `<Rnd>` — would duplicate the
geometry, double the elements a screen reader meets, and put a second thing on
the canvas for #2345 and #2346 to reconcile.

---

## Boundary rules for this change

1. **No file under `src/**` is modified.** The two `Normalized*` value objects
   are read for their bounds only.
2. **`overlayLabelStyle.ts` is not modified and gains no caller.** It is on the
   composite-and-render leg (§IV, ≤ 50 ms, up to 250 calls per wall render). Any
   diff to it is out of scope for this issue.
3. **`CameraViewer.tsx` is not modified.** The wall label stays non-interactive;
   a tab stop per tile on a 250-tile wall is a defect, not a feature.
4. **No Redux.** The three guards render `OverlayEditor` bare. A `useSelector`
   anywhere in this change breaks all three at once.
5. **No new package.** `apps/shared` has no `user-event`; `fireEvent` is the
   tool (spec finding 6).
6. **No design tokens.** #2342 owns that conversion and is gated on #2332; the
   ring's `#ffffff`/`#000000` are inline hex like every other colour in this
   file, and #2342 inherits them.

---

## Risks

| Risk | Handling |
|---|---|
| `role="application"` does not actually put NVDA in focus mode | jsdom cannot prove it either way. Phase 5 step 7 is the verification; if no screen reader is available the note must **say so**, not claim it |
| The two-ring indicator is still hard to read over one particular frame | Phase 5 step 6 screenshots it over a deliberately bright scene; a failure here is a ring-thickness change, not a design restart |
| `Ctrl`+arrow collides with something in the Radix Dialog | Phase 5 step 5. Radix Dialog binds `Escape` and `Tab`, not `Ctrl`+arrow |
| PR #2360 merges and conflicts | One adjacent hunk. Rebase, re-run all four test files (spec §Dependency on PR #2360) |
| A held key floods `onChange` and the parent dialog re-renders per press | Accepted: one `onChange` per keypress is exactly what a slider drag already does in this file, and the editor is not on any latency leg (§IV, N/A) |
