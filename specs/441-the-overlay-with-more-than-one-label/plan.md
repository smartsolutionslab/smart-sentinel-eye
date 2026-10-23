# Plan 441: The overlay with more than one label

**Spec:** [spec.md](spec.md) · **Issue:** #3072 · **Base:** `develop` at `29968a0d`

## Slices (ordered; each independently mergeable)

The issue's original arc assumed a domain + contract + kiosk slice first. Those are delivered (spec §"Premise"):
specs 150/300/301 carry 1..8 elements through domain, HTTP, `OverlayRevisionPublishedV3`,
`ResolvedOverlayTextChangedV3` and the wall. What remains is the editor.

| # | Slice | Scope | Issue |
|---|---|---|---|
| **1** | **Several labels + layer list + Text-over-Text warning** (this spec) | `apps/shared` composite + `apps/management-web` form; Text only can be added | #3072 |
| 2 | Author Box/Ellipse: "Add box/ellipse", the selected shape draggable/resizable/nudgeable with the geometry fields; no text or font controls | `OverlayEditor` generalised from `OverlayLabel` to `OverlayElement` (characterisation of the four `OverlayEditor*` guards first) | file on merge of 1 |
| 3 | Colour authoring: hue `<input type="color">` + opacity slider whose minimum is `minimumTextAlpha(hue)` + hex field (spec 300 US4 / T027, never built) | form + editor | file on merge of 1 |

Not filed (no stated need, ADR-0036): list-level undo, drag reorder, multi-select/align, snapping, copy between
overlays.

## Bounded context and layers

Overlay Designer, **frontend only**. No Domain/Application/Infrastructure/Api, no `Shared.Contracts`, no migration,
no AppHost resource. NetArchTest/`PrimitiveBoundaryTests` are untouched. No value object or entity is introduced;
the invariants that matter are already enforced server-side (`ElementSetViolation`, `OverlayElement.MaxElements`,
`TextElement` legibility) and mirrored by `overlays.schema.ts`, which is **not modified**.

## Components

### C1. `overlayLayers.ts` (new, `apps/shared/src/ui/composites/`) — pure functions

- `layerName(element, ordinal, elements): string` — Text: trimmed text, or "Empty label"; Box/Ellipse:
  "Box n" / "Ellipse n", n = 1-based count of that kind in ordinal order.
- `listPositionOf(ordinal, count) = count - ordinal` (front-most first, decision 1) and its inverse.
- `textOverlaps(elements): Array<[i, j]>` — `i < j`, both `kind === 'Text'`, positive-area intersection
  (`ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah`, strict, so touching edges are not an overlap).
- `selectionAfterRemove(removedListPosition, newCount)`.

No React, no DOM: unit-testable exhaustively.

### C2. `OverlayLayerList.tsx` (new, `apps/shared/src/ui/composites/`)

Props: `elements`, `rowIds` (from `useFieldArray`), `selectedId`, `onSelect(id)`, `onMove(ordinal, +1|-1)`,
`onRemove(ordinal)`, `onAddLabel()`, `canAdd`, `canRemove`. Renders a native `<ol aria-label="Layers">`, front-most
first; each `<li>`: `ToggleChip pressed={selected}` (name = `layerName`) + `Button variant="secondary"` "Move up" /
"Move down" / "Remove" with `aria-label` "Move <name> up" etc., `unavailable` at the ends (ADR-0151 — focus is not
dropped when a move reaches the top). "Add label" `Button` below the list, `unavailable` at `MAX_ELEMENTS` with
visible text "Up to 8 elements". One `aria-live="polite"` region (`data-testid="overlay-layer-list-live-region"`),
using `OverlayEditor`'s `{ text, token }` keyed-span pattern so a repeated announcement still mutates the DOM.
**Focus after a move:** the moved row keeps its React key (`rowIds`), so the same button node stays focused; an
explicit `focus()` is not needed and must not be added unless the test shows focus lost.

Kept in `apps/shared`, Redux-free, beside `OverlayEditor` (ADR-0169 posture), because slices 2-3 extend it.

### C3. `OverlayEditor.tsx` — one optional prop

`inertElements?: { below: readonly OverlayElement[]; above: readonly OverlayElement[] }`. Painted as absolutely
positioned `div`s, `aria-hidden`, `pointer-events: none`, geometry from normalized coordinates × canvas size, style
from `overlayLabelSurfaceStyle` / `overlayShapeStyle` (Text nodes show their raw text; the resolve preview stays for
the selected label only). `below` before the `<Rnd>`, `above` after it, inside `overlay-editor-canvas`.
**Absent ⇒ byte-identical render**; that is what the characterisation suite proves. No other edit to this file.

### C4. `OverlayDraftForm.tsx` — from one index to a selected row

- `useFieldArray({ control, name: 'elements' })` → `fields`, `move`, `append`, `remove`.
- `selectedId` state, seeded to `fields[textIndexOf(defaultValues.elements)]?.id ?? fields[0].id` (decision 3) and
  re-seeded on `reset`.
- `selectedIndex = fields.findIndex(f => f.id === selectedId)`; the `Controller` binds
  `elements.${selectedIndex}`; `OverlayEditor` is keyed by `selectedId` (decision 4: history per element).
- `hasTextElement` (today: drives the resolve query and the camera-choices `skip`) becomes "any Text element in the
  watched set"; the resolve query uses the **selected** label's text.
- Notices: no Text at all → existing `overlay-editor-dialog-shapes-only-notice` (wording may drop "#2343", testid
  kept); a selected shape among labels → `overlay-editor-shape-selected-notice`.
- Overlap: `textOverlaps(watchedElements)` → `role="status"` lines (`overlay-editor-overlap-warning`).
- Validation (FR-009): `handleSubmit`'s `onInvalid` selects the first element index carrying an error; the
  existing label-error alert reads `errors.elements?.[selectedIndex]`.
- Submit unchanged: `input.elements` is the whole array in field order (RHF strips `id`).

`OverlayCreatePage.tsx` / `OverlayEditPage.tsx`: header copy only ("type the label" → "add labels"); no logic.
`PLACEHOLDER_ELEMENTS` stays a single Text element (its comment's reasoning holds per selection).

## Messaging

None. Domain → integration events are unchanged (`OverlayRevisionPublishedV3` at publish, as today).

## Boundary rules

No cross-context reference is possible (no backend file). `apps/kiosk-web` and `CameraViewer.tsx` must be absent
from the diff (spec FR-010) — the reviewer checks `git diff --stat`.

## Phase 4a — colours (declared here, ADR-0144)

**CHARACTERISATION (captured green before any change, must pass unmodified after):**
`OverlayEditorCharacterisation`, `OverlayEditorKeyboard`, `OverlayEditorUndo`, `OverlayEditorBackdrop`,
`OverlayLabelParity`, `OverlayLabelCharacterisation`, `OverlayGeometryFields`, `OverlayDraftForm*.test.tsx`
(all six, including the two FR-019 tests at `OverlayDraftForm.test.tsx:506,572`), `OverlayEditPage*`,
`OverlayCreatePage`, `overlays.schema.test.ts`, `e2e/overlays.spec.ts`. Any assertion that has to change is
evidence the behaviour moved: block, do not adjust.

**RED (observed failing on unmodified code):** C1 unit tests; C2 component tests; the C3 `inertElements` test; new
`OverlayDraftFormLayers.test.tsx` (add, select, move, remove, bounds, overlap, invalid-element selection, PATCH
body order, 409 stale on a reorder); new `e2e/overlay-layer-list.spec.ts`.

### Counterfactuals (each must turn a named test red; run, quote in the PR)

1. Reverse the list order (ordinal-first) → "Move up brings forward" and the list-order assertion fail.
2. Make `textOverlaps` non-strict (`<=`) → the touching-edges case fails.
3. Include Box in `textOverlaps` → "shapes never warn" fails.
4. Key `OverlayEditor` by index instead of row id → "selection follows the element across a move" fails.
5. Drop `inertElements` painting → the canvas-shows-siblings test fails.
6. Send `fields` (with `id`) instead of `input.elements` → the PATCH-body equality test fails.

## Measurement

None owed: no §IV leg is touched (spec §"Latency"). The verification note records `git diff --stat` showing no
kiosk/backend/contract file, which is what makes N/A true rather than asserted.

## Risks

- **RHF `useFieldArray` + `Controller` on a moving index:** a `Controller` bound to `elements.${i}` re-binds when
  `i` changes after a move. Guarded by counterfactual 4 and the "edit after move" test.
- **Two `Rnd`-free inert layers above the `<Rnd>`:** `pointer-events: none` is what keeps drag working under them;
  pinned by a test that drags the selected label beneath an `above` element.
