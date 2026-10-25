# Spec 457: An operator adds a box or an ellipse

**Issue:** #3107 (slice 2 of the overlay editor arc, spec 441 plan §"Slices") · **Branch:** `feat/3107-overlay-editor-box-ellipse`
**ADRs:** ADR-0165 (one element value object with a kind and a colour; strokes carry no readability floor),
ADR-0164 (the cap of 8 counts every element), ADR-0112 §2 (an element has no identity on the wire), ADR-0113
(`If-Match` wholesale replace), ADR-0077 (Radix headless + own primitives; no new dependency), ADR-0079 (RHF + Zod),
ADR-0151 (`unavailable`, not `disabled`), ADR-0146 (design language), ADR-0144 (lane). Constitution §II, §III, §IV.
**Specs built on:** 441 (layer list, selection, inert siblings), 300 (Box/Ellipse, `overlayShapeStyle`),
149/151/154 (keyboard, geometry fields, undo), 305 (full-surface pages).
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** behaviour-changing → **red**, with a declared
characterisation set for the Text path (plan §"Phase 4a").
**Security-sensitive:** no. No endpoint, scope, contract or server validation changes.
**Written 2026-10-25 against `develop` at `677b8054`.**

---

## Premise, checked

The issue's premise holds on `677b8054`:

| Layer | State | Evidence |
|---|---|---|
| Domain / HTTP / contract / wall | 1..8 elements of kind Text/Box/Ellipse, painted in order | spec 441 §"Premise" (unchanged since) |
| Client schema | `overlayElementSchema` = discriminated union of `textSchema`, `boxSchema`, `ellipseSchema` | `apps/shared/src/api/overlays.schema.ts:70-82` |
| Layer list | lists every kind, names shapes "Box n"/"Ellipse n"; **one "Add label" button** | `OverlayLayerList.tsx` (`onAddLabel`) |
| Editor | `value: OverlayLabel` — text input, font size, resolve preview are unconditional | `OverlayEditor.tsx:20,796-829` |
| Form | a selected shape shows `overlay-editor-shape-selected-notice`; a shapes-only set shows `overlay-editor-dialog-shapes-only-notice`; camera choices `skip: !hasTextElement` | `OverlayDraftForm.tsx:369,515-574` |
| Undo history | compares `OverlayLabel`'s six fields | `useOverlayEditHistory.ts:78` |
| Dropdown primitive | exists, Radix-backed, used by `LayoutsPage` | `apps/shared/src/ui/primitives/DropdownMenu.tsx` |
| Icons | no icon library, no inline `<svg>` anywhere under `apps/` | `grep -rln "<svg" apps/*/src` → none |

**Frontend only** (`apps/shared` + `apps/management-web` + `e2e/`). No backend, contract or kiosk change is needed.

## Owner decisions (issue #3107, 2026-10-23) — honoured, not reopened

1. An **"Add" menu** offering **Text / Box / Ellipse**.
2. A **kind icon** in each layer-list row.
3. A **per-kind inspector**: geometry x/y/width/height for shapes; text fields only for Text.
4. Reuse the existing **move up/down** and **remove**, and the single-source **`MAX_ELEMENTS`** cap.

## Decisions this spec makes (from existing ADRs and spec 441; none open)

1. **The "Add" menu is the existing `DropdownMenu` primitive** with a `Button` trigger named "Add" and three items
   "Text", "Box", "Ellipse" (owner decision 1; ADR-0077 — no new dependency). At `MAX_ELEMENTS` the trigger is
   `unavailable` (ADR-0151) and the existing "Up to 8 elements" text shows.
2. **What each kind adds.** Text: exactly spec 441 FR-003's label (create-default geometry and font size, text
   "Overlay text", `DEFAULT_OVERLAY_COLOR`). Box/Ellipse: `DEFAULT_OVERLAY_COLOR`, geometry
   `(0.1, 0.1, 0.3, 0.3)`. *Marked guess:* the shape geometry is not owner-ruled; it shares the label's origin, as
   spec 441 FR-003 did, and is one constant to change.
3. **Add behaviour is kind-independent.** The new element goes to the front, is selected, its chip takes focus,
   and the live region says "<Kind> added, position 1 of n" with Kind = "Label" / "Box" / "Ellipse" (spec 441's
   "Label added, …" is the Text case, unchanged).
4. **The kind icon is decorative.** An inline SVG per kind (a "T" glyph, a square outline, a circle outline),
   `aria-hidden`, inside the chip before the name. The chip's accessible name stays `layerName` exactly, so every
   spec 441 name-based query holds. No icon library (ADR-0077; none exists in the repo).
5. **`OverlayEditor` edits any element.** `value` widens from `OverlayLabel` to `OverlayLabel | OverlayShape`.
   For a Text value the render is **byte-identical to today** (characterisation, plan §"Phase 4a"). For a shape:
   the interactive canvas node is painted with `overlayShapeStyle` (the wall's own function), named by its kind
   ("Box"/"Ellipse") with `aria-roledescription="Overlay shape"`, and its instructions say "shape" where Text's
   say "label"; drag, resize, arrow nudges, the four geometry fields and undo/redo work exactly as for a label;
   **no** text input, font-size slider or placeholder-preview panel is rendered. Backdrop controls stay.
6. **Colour is preserved, never authored** (that is slice 3). Every edit spreads the incoming value, so a shape's
   `color` (and `kind`) round-trips byte-identical.
7. **Both shape notices go.** With every kind editable there is nothing to explain:
   `overlay-editor-shape-selected-notice` and `overlay-editor-dialog-shapes-only-notice` are removed. A shapes-only
   draft opens with its first element selected (spec 441 decision 3's fallback, `Math.max(textIndexOf, 0)`).
8. **Camera choices are fetched whenever the editor is mounted** — now always — because the backdrop's camera
   picker is offered for a shape too. The `skip: !hasTextElement` guard is removed; the resolve query keeps its
   own Text-only gate.
9. **The overlap warning stays Text-over-Text** (spec 441 decision 6). Shapes never warn.

---

## User story 1 (P1): An operator adds and edits a box or an ellipse

*As an operator on the overlay page, I want to add a box or an ellipse from the same place I add a label, and
move and size it, so that I can frame a machine on the wall without dropping to the API.*

Independently shippable and observable: on `/overlays/new` add a Box, drag it, save; `GET /overlays/{id}` returns
`[Text, Box]` with the Box at the dragged geometry; publish and the wall paints the box (the wall path is already
built and guarded by `e2e/kiosk-paints-a-reordered-overlay.spec.ts`).

### Acceptance scenarios

```gherkin
Scenario: The Add menu offers the three kinds
  Given an operator holding sse.overlays.write on /overlays/new
  When they open the "Add" menu
  Then it offers exactly "Text", "Box" and "Ellipse", in that order

Scenario: A box is added, selected, moved and saved
  Given the create page with its single default label "Overlay text"
  When the operator chooses "Box" from the "Add" menu
  Then the layer list shows "Box 1" above "Overlay text", pressed, with its chip focused
  And the live region announced "Box added, position 1 of 2"
  And the inspector shows the four geometry fields and no "Label text" field and no font size
  When they set X to 40 % in the geometry fields and save
  Then exactly one POST /overlays is sent whose elements are
    [Text "Overlay text", Box (#FFFFFFD9, x 0.4, y 0.1, w 0.3, h 0.3)]

Scenario: An ellipse is edited by keyboard and undone
  Given a draft [Text "A", Ellipse] open on its edit page
  When the operator selects "Ellipse 1" and presses ArrowRight on the canvas shape
  Then the ellipse's x grows by one fine step and the geometry live region announces it
  When they press Ctrl+Z
  Then the ellipse's x is back to its opened value and "Undone" is announced

Scenario: A shape keeps its colour and kind through an edit
  Given a draft [Box (#D32F2F80, 0.05, 0.05, 0.2, 0.2), Text "Zone A"]
  When the operator selects "Box 1", drags it, and saves
  Then exactly one PATCH is sent with If-Match equal to the chain version
  And element 0 is kind "Box", color "#D32F2F80", at the dragged geometry
  And element 1 is byte-identical

Scenario: A shapes-only draft opens editable
  Given a draft [Box]
  When the page opens
  Then "Box 1" is selected and its geometry fields are shown
  And no shapes-only notice is shown
  And the backdrop's camera picker is offered

Scenario: Each row carries a kind icon that does not change its name
  Given a draft [Text "A", Box, Ellipse]
  Then each layer row shows a decorative icon for its kind (aria-hidden)
  And the chips are named exactly "Ellipse 1", "Box 1", "A"

Scenario: A label still edits exactly as before
  Given a draft [Text "A"]
  Then the label editor, its text field, its font size and its placeholder preview render as they did before this change
```

**Conflict**

```gherkin
Scenario: A stale version loses an added shape, entire
  Given two operators editing the same draft at version 4
  When the first saves
  And the second adds an Ellipse and saves with If-Match 4
  Then the second sees the existing stale-conflict notice with Reload, never Try again
  And the ellipse stays in the second operator's form, unsent
```

**Bad request (client-side bounds; the server's are unchanged)**

```gherkin
Scenario: No ninth element of any kind
  Given a draft with 8 elements
  Then the "Add" trigger is unavailable (aria-disabled) and "Up to 8 elements" is shown
  And activating it opens no menu and changes nothing

Scenario: An out-of-range shape geometry is refused in the field, never committed
  Given a selected Box of width 0.3
  When the operator types 0 into its Width field and commits it
  Then the Width field shows its existing refusal message (OverlayGeometryFields, spec 151 / #2361)
  And the Box's width stays 0.3
  When they save
  Then the request carries the Box at width 0.3
```

**Auth (unchanged, pinned)**

```gherkin
Scenario: Without sse.overlays.write a save with a shape is refused by the server, not the editor
  Given an operator who passes the route's existing Gated path="/overlays" check but lacks sse.overlays.write
  When they add a Box and save
  Then the API answers 403 exactly as it does for a label today
  And the form shows its existing save-failure message and keeps the Box in place
```

### Functional requirements

- **FR-001** `OverlayLayerList` replaces its "Add label" `Button` with a `DropdownMenu` whose trigger is a `Button`
  named "Add" and whose entries are "Text", "Box", "Ellipse". `onAddLabel()` becomes `onAdd(kind)`. At
  `!canAdd` or `readOnly` the trigger is `unavailable` and opens nothing; "Up to 8 elements" shows at `!canAdd`.
- **FR-002** Adding any kind appends at the front, selects it, focuses its chip, and announces
  "<Label|Box|Ellipse> added, position 1 of n" (decision 3). Defaults per decision 2.
- **FR-003** Each chip renders an `aria-hidden` inline-SVG kind icon before its name; the accessible name is
  `layerName(...)` unchanged (decision 4).
- **FR-004** `OverlayEditor.value` accepts `OverlayLabel | OverlayShape`. A Text value renders byte-identically to
  today. A shape value renders per decision 5. `onChange` emits the same kind it received, with `color` untouched.
- **FR-005** `useOverlayEditHistory` accepts the same union; its echo/no-op comparison compares `kind` and geometry
  for shapes and is unchanged for labels.
- **FR-006** `OverlayDraftForm` mounts `OverlayEditor` for whichever element is selected, of any kind; both shape
  notices are removed (decision 7); camera choices are no longer skipped (decision 8); the label-error alert stays
  Text-only; a Zod error on a shape selects it (spec 441 FR-009, unchanged).
- **FR-007** Overlap warning, move up/down, remove, `MAX_ELEMENTS`, submit shape (whole array, ordinal order, no
  client id) are unchanged from spec 441.
- **FR-008** No backend, contract, `apps/kiosk-web` or `CameraViewer.tsx` file is in the diff. `overlays.schema.ts`
  is not modified.

## Locked tech choices

| Concern | Choice | Authority |
|---|---|---|
| Add menu | existing `DropdownMenu` primitive (Radix `react-dropdown-menu@2.2.1`, already a dependency) | owner decision 1; ADR-0077 |
| Kind icon | inline SVG, `aria-hidden`, no icon package | owner decision 2; ADR-0077 |
| Inspector | one `OverlayEditor`, branching on `value.kind` | owner decision 3; spec 441 plan slice 2 |
| Shape painting | wall's `overlayShapeStyle` | #2339 "one renderer"; ADR-0165 |
| Disabled controls | `unavailable` | ADR-0151 |
| Form state | RHF `useFieldArray.append` + unchanged Zod schema | ADR-0079 |
| Save | POST on create, one `PATCH` + `If-Match` on edit | ADR-0113; ADR-0112 §2 |
| Contract | none changed | ADR-0073 (not exercised) |

## Latency budget impact (constitution §IV)

**N/A for every leg.** No file on the event-to-overlay path changes (FR-008). Per-tile composite cost is bounded
by the domain cap of 8 elements of any kind (ADR-0164 §4), which the "Add" menu cannot raise; shapes were already
authorable through the API and painted by the wall (spec 300 FR-017 measured full-cardinality mixed kinds).

## Independent end-to-end test procedure

1. Boot the stack; sign in to management-web as an operator with `sse.overlays.write`/`read`.
2. `/overlays/new`: name it; open **Add** → it lists Text, Box, Ellipse; choose **Box**. "Box 1" is on top, pressed,
   focused, with a square icon; the inspector shows X/Y/Width/Height and no text field. Drag the box; **Save**.
3. `GET /overlays/{id}` (token from the proxied Keycloak endpoint): `elements` = `[Text, Box]`, the Box at the
   dragged geometry, `color` `#FFFFFFD9`.
4. Branch a draft, open its edit page; **Add → Ellipse**; ArrowRight on the canvas ellipse; Ctrl+Z restores it;
   ArrowRight again; **Move Ellipse 1 down**; **Save**. `GET`: the Ellipse is at the chosen ordinal and nudged x.
5. Publish; a wall tile bound to it paints the label, the box and the ellipse in order.
6. Fill to 8 elements: the **Add** trigger is aria-disabled and "Up to 8 elements" shows.

## Out of scope

Colour authoring (slice 3; spec 300 US4), fill, stroke width, rotation, list-level undo, drag reorder,
multi-select, snapping, copy between overlays.
