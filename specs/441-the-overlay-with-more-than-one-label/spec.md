# Spec 441: The overlay with more than one label

**Issue:** #3072 (successor to #2343) · **Branch:** `feat/3072-overlay-multi-element`
**ADRs:** ADR-0164 + ADR-0165 (the element set, its cap of 8, the element model), ADR-0112 §2 (an element has no
identity), ADR-0113 (`If-Match` wholesale replace), ADR-0146 (design language; full-surface page), ADR-0151
(`unavailable`, not `disabled`), ADR-0077 (Radix headless + own primitives; no new dependency), ADR-0079 (RHF + Zod),
ADR-0073 (contract versioning, cited only to say it is not exercised), ADR-0144 (lane). Constitution §II, §III, §IV.
**Specs built on:** 150 (ordered element set), 300 (Box/Ellipse, colour, FR-019), 301 (pairing, decisions 1-3),
305 (`OverlayEditPage` / `OverlayCreatePage`), 149/151/154 (keyboard, geometry fields, undo).
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** two colours, split per artefact (plan §"Phase 4a").
**Security-sensitive:** no. No endpoint, scope, contract or server validation changes.
**Written 2026-10-23 against `develop` at `29968a0d`.**

---

## Premise, checked: what specs 150/300/301/305 already deliver

The issue says "what remains: more than one label per overlay", and the owner's decision (1) of 2026-10-22 reads
that as an additive contract change. **The contract, domain, persistence and wall already carry more than one
label per overlay. Only the console editor does not.**

| Layer | State on `29968a0d` | Evidence |
|---|---|---|
| Domain | A revision holds an ordered set of 1..8 elements of kind Text/Box/Ellipse | `OverlayElement.cs:43` `MaxElements = 8`; `Overlay.cs:50` |
| HTTP | `POST /overlays` and the draft `PATCH` take and return `elements[]` | `OverlayDto.cs:47` `OverlayElementDto` |
| Contract | `OverlayRevisionPublishedV3(... IReadOnlyList<OverlayElementV3> Elements ...)` | `Shared.Contracts/OverlayDesigner/OverlayRevisionPublishedV3.cs:25,41` |
| Resolved text | `ResolvedOverlayTextChangedV3`, one `(Template, Resolved)` per distinct Text template | `Shared.Contracts/SystemVariables/ResolvedOverlayTextChangedV3.cs:36` (spec 301 US2) |
| Wall | paints every element, ordinal = DOM order, Text and shapes | `CameraViewer.tsx:456-467`; `e2e/kiosk-paints-a-reordered-overlay.spec.ts` |
| Full-surface page | create and edit are pages, not a modal | `OverlayCreatePage.tsx`, `OverlayEditPage.tsx` (spec 305, #2350) |
| Client schema | `createOverlayDraftSchema.elements` = 1..`MAX_ELEMENTS` of a `kind` union | `overlays.schema.ts:101-112` |
| **Editor** | **edits exactly one Text element** (the first by kind); holds and resubmits the rest untouched | `OverlayDraftForm.tsx:109,260,466-490`; `OverlayEditor.tsx:19-21` (`value: OverlayLabel`) |
| Colour input | **never built** (spec 300 US4 / T027, PR-B, no issue) | no `type="color"` under `apps/` |

**Consequences for the owner decisions:**
1. **Decision (1) is already satisfied, and there is nothing to version.** Spec 300 made a *clean* V3 cut
   (`OverlayRevisionPublishedV2` deleted, ADR-0073's semantic-shift rule), and it already carries the list. A
   "new V2 field" would duplicate a field that exists. **No contract change, no ADR-0073 exercise, no new ADR.**
2. **Decision (2) is half delivered.** The page exists (spec 305); the canvas on it edits one label.
3. **Decision (3) is the remainder** — together with #2343's recorded calls (layer list with move up/down,
   Text-over-Text warning only, reorder = client permutation + wholesale `PATCH`) and spec 300's follow-on item 3
   (the editor "must replace FR-019's first-text-element rule").

The remainder is **frontend-only** (`apps/shared` + `apps/management-web`). It is split into ordered slices
(plan §"Slices"). **This spec specifies slice 1 only.**

---

## Decisions this spec makes (from existing ADRs and recorded calls, none open)

1. **List order is front-most first.** The layer list shows the element with the highest ordinal at the top.
   "Move up" = bring forward = ordinal + 1. This is the convention of every layer panel an operator has met, and it
   is the reading recorded on #2343 (2026-10-05) ("'Bring forward'/'send back' become the list's move-up/move-down").
2. **Elements have no identity on the wire (ADR-0112 §2); the client gives them one for the session only.**
   React Hook Form's `useFieldArray` (already used by `GridDesigner.tsx:102`) supplies a client `id` per row; the
   selection follows that id across a reorder. The `id` is never sent (`keyName` stripped by RHF on submit).
3. **The initial selection is the first Text element by ordinal**, i.e. today's FR-019 rule becomes the *default
   selection*, not the *only* editable element. Both FR-019 guard tests (`OverlayDraftForm.test.tsx:506,572`) stay
   true unmodified.
4. **Undo/redo stays per selected element.** `useOverlayEditHistory` is scoped to one `value`; switching selection
   starts a fresh history. Add / remove / reorder are not undoable in slice 1. *(Recorded scope cut, not a guess:
   widening history to the set is a separate change with its own characterisation burden.)*
5. **Unselected elements are painted on the canvas, inert.** `aria-hidden`, `pointer-events: none`, styled by the
   wall's own `overlayLabelSurfaceStyle` / `overlayShapeStyle`, in ordinal order around the selected element, so the
   canvas stacks exactly as the wall does. The list is the selector (#2343: an element underneath cannot be clicked).
6. **The overlap warning is advisory.** Two Text elements whose rectangles intersect with positive area produce one
   `role="status"` line naming both. Touching edges do not count. Box/Ellipse never participate. Save is never blocked.

---

## User story 1 (P1): An operator authors an overlay with several labels and orders them

*As an operator on the overlay page, I want to add, select, remove and reorder labels, so that one camera tile can
carry a reading beside each machine without dropping to the API.*

Independently shippable and observable: on `/overlays/new` add a second label, move it, save; `GET /overlays/{id}`
returns both in the chosen order; publish and the wall shows both (the wall path is already built and guarded).

### Acceptance scenarios

```gherkin
Scenario: Two labels are created and saved in list order
  Given an operator holding sse.overlays.write on /overlays/new
  When they type the name "Line 1 readings" and the first label's text "T1 {{t1}}"
  And they choose "Add label" and type "T2 {{t2}}" into the new label
  And they save
  Then exactly one POST /overlays is sent whose elements are [Text "T1 {{t1}}", Text "T2 {{t2}}"]
  And the layer list showed "T2 {{t2}}" above "T1 {{t1}}" before saving

Scenario: Move up brings a label forward and the whole set is PATCHed once
  Given a draft [Text "A", Text "B", Box] open on its edit page
  When the operator selects "A" in the layer list and chooses "Move A up"
  And saves
  Then exactly one PATCH is sent with If-Match equal to the chain version
  And its elements are [Text "B", Text "A", Box], the Box byte-identical
  And focus stays on the "Move A up" control after the move
  And the live region announced "A moved to position 2 of 3"

Scenario: Selecting a label edits that label only
  Given a draft [Text "A", Text "B"]
  When the operator selects "B" and changes its text to "B2" and its font size
  Then element 0 is unchanged and element 1 carries "B2"
  And the canvas paints "A" inert (aria-hidden) and "B2" as the focusable label

Scenario: The opened draft selects the first Text element, as before
  Given a draft [Box, Text "Line 1"]
  When the page opens
  Then the label editor shows "Line 1" and the Box is listed but not selected

Scenario: Selecting a shape does not coerce it into a label
  Given a draft [Box, Text "Zone A"]
  When the operator selects the Box in the layer list
  Then no label text field is shown and a notice says shapes are edited in a later release
  And saving sends the Box byte-identical

Scenario: Overlapping labels warn, shapes never do
  Given a draft [Text "A" at (0.1,0.1,0.3,0.1), Text "B" at (0.2,0.15,0.3,0.1), Box at (0,0,1,1)]
  Then one status message says "A" and "B" overlap
  And no message names the Box
  And Save is available
  When "B" is moved so the rectangles only touch at an edge
  Then the overlap message is gone
```

**Conflict**

```gherkin
Scenario: A stale version loses a reorder, entire
  Given two operators editing the same draft at version 4
  When the first saves a reorder
  And the second saves a different reorder with If-Match 4
  Then the second sees the existing stale-conflict notice with Reload, never Retry
  And no merge of the two orders is ever sent
```

**Bad request (client-side bounds; the server's are unchanged)**

```gherkin
Scenario: The ninth element cannot be added
  Given a draft with 8 elements
  Then "Add label" is unavailable (aria-disabled) and says the limit is 8
  And activating it changes nothing

Scenario: The last element cannot be removed
  Given a draft with 1 element
  Then "Remove" is unavailable and activating it changes nothing

Scenario: An invalid label among several names that label
  Given a draft [Text "A", Text "B"]
  When "B"'s text is cleared and the operator saves
  Then no request is sent
  And the error names the label at list position of "B" and selects it
```

**Auth (unchanged, pinned)**

```gherkin
Scenario: Without sse.overlays.write a multi-label save is refused by the server, not the editor
  Given an operator who passes the route's existing Gated path="/overlays" check but lacks sse.overlays.write
  When they author two labels and save
  Then the API answers 403 exactly as it does for a single label today
  And the form shows its existing save-failure message and keeps both labels in place
```

### Functional requirements

- **FR-001** `OverlayDraftForm` holds the elements through `useFieldArray` and renders a **layer list**: a native
  `<ol>` in front-most-first order; each row a `ToggleChip` (`pressed` = selected) naming the element (Text: its
  text, or "Empty label"; shapes: "Box n"/"Ellipse n") plus `Button`s "Move up", "Move down", "Remove", each with an
  accessible name that includes the element's name. No drag-and-drop, no new dependency (owner decision 3).
- **FR-002** "Move up/down" call `move(i, i±1)` in ordinal space; at an end the control is `unavailable`
  (ADR-0151) and inert. Focus stays on the activated control; one polite announcement "<name> moved to position k
  of n" (k counted in list order, top = 1).
- **FR-003** "Add label" appends a Text element (`DEFAULT_OVERLAY_COLOR`, the create-default geometry and font
  size, text "Overlay text") at the front and selects it. `unavailable` at `MAX_ELEMENTS`.
- **FR-004** "Remove" deletes the element; `unavailable` when one remains. Selection moves to the row now at the same
  list position, else the one above.
- **FR-005** The selected Text element is edited by the existing `OverlayEditor`, keyed by the row's client id.
  `OverlayEditor` gains one **optional** prop that paints the other elements inert in ordinal order around the
  selected one (decision 5). With the prop absent it renders exactly as today.
- **FR-006** A selected Box/Ellipse shows a notice (`data-testid="overlay-editor-shape-selected-notice"`) instead of
  the label editor. A set with no Text element keeps today's `overlay-editor-dialog-shapes-only-notice`.
- **FR-007** Text-over-Text overlap (positive-area rectangle intersection, `ordinal i < j`) yields one
  `role="status"` line per pair, `data-testid="overlay-editor-overlap-warning"`. Non-blocking.
- **Accepted behaviour (decision 5).** Inert `above` nodes paint after `<Rnd>` with no z-index, so a label dragged
  beneath one is hidden, its focus outline included; pointer events still reach `Rnd` because the inert nodes are
  `pointer-events: none`.
- **FR-008** Save sends the whole array in ordinal order (POST on create, one `PATCH` with `If-Match` on edit) —
  spec 150 FR-006 / spec 301 decision 2. No client id, no extra field.
- **FR-009** A Zod error on element *i* selects element *i* and shows its message (today's label-error alert,
  generalised from `textIndex` to the selected index).
- **FR-010** No backend, contract, kiosk or `apps/kiosk-web` file is in the diff.

## Locked tech choices

| Concern | Choice | Authority |
|---|---|---|
| Container | Existing full-surface pages (`OverlayCreatePage`/`OverlayEditPage`) | owner decision 2; spec 305; ADR-0146 |
| List | Native `<ol>` + `ToggleChip` + `Button` (`unavailable`) | owner decision 3; ADR-0077; ADR-0151 |
| Form state | RHF `useFieldArray` (`move`/`append`/`remove`) + existing Zod schema, unchanged | ADR-0079; `GridDesigner.tsx:102` precedent |
| Reorder | client permutation + wholesale `PATCH` under `If-Match` | spec 301 decision 2; ADR-0113; ADR-0112 §2 |
| Canvas painting | wall style functions `overlayLabelSurfaceStyle` / `overlayShapeStyle` | #2339 "one renderer" |
| Contract | none changed | ADR-0073 (not exercised) |

## Latency budget impact (constitution §IV)

**N/A for every leg.** No file on the event-to-overlay path changes (FR-010): not OverlayDesigner, not
SystemVariables, not LayoutComposition, not `apps/kiosk-web`, not `CameraViewer.tsx`. Composite + render cost per
tile is bounded by the domain cap (8 elements, ADR-0164/0165; 72 nodes on a 9-tile wall), which this slice cannot
raise — the editor's "Add" is capped at the same `MAX_ELEMENTS`, pinned to `OverlayElement.MaxElements` by
`overlays.schema.test.ts`. Full-cardinality mixed-kind render cost was spec 300 FR-017's measurement; an 8-element
overlay was already authorable through the API.

## Independent end-to-end test procedure

1. Boot the stack; sign in to management-web as an operator with `sse.overlays.write`/`read`.
2. `/overlays/new`: name it, type "T1", choose **Add label**, type "T2", drag T2 elsewhere, **Save**.
3. `GET /overlays/{id}` (token from the proxied Keycloak endpoint): `revisions[0].elements` = `[T1, T2]`.
4. Branch a draft, open its edit page; select T1, **Move T1 up**; Tab stays on the button; screen-reader text
   announces the move. Drag T1 onto T2: the overlap status line appears; Save is still available. **Save**.
5. `GET`: elements = `[T2, T1]`. Publish; open a wall tile bound to it: T1 paints above T2 (DOM order).
6. With 8 elements, **Add label** is aria-disabled. With 1, **Remove** is aria-disabled.

## Out of scope (later slices, plan §"Slices")

Authoring Box/Ellipse (slice 2), colour authoring (slice 3; spec 300 US4 never built), list-level undo, drag
reorder, multi-select/align/distribute, snapping. Copying elements between overlays (#2343's table) is not filed.
