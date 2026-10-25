# Plan 457: An operator adds a box or an ellipse

**Spec:** [spec.md](spec.md) · **Issue:** #3107 · **Base:** `develop` at `677b8054`
**Arc:** slice 2 of spec 441 plan §"Slices". Slice 3 (colour authoring) stays unfiled until this merges.

## Bounded context and layers

Overlay Designer, **frontend only**. No Domain/Application/Infrastructure/Api, no `Shared.Contracts`, no migration,
no AppHost resource; NetArchTest and `PrimitiveBoundaryTests` are untouched. No value object or entity is
introduced. The invariants that matter — kind ∈ {Text, Box, Ellipse}, 1..8 elements, geometry in `[0,1]` with
positive size, strokes with no alpha floor — are enforced server-side (`OverlayElement`, ADR-0165) and mirrored by
`overlays.schema.ts`, which is **not modified**.

## Messaging

None. `OverlayRevisionPublishedV3` already carries shapes; nothing changes on publish.

## Boundary rules

No cross-context reference is possible. `apps/kiosk-web`, `CameraViewer.tsx`, `overlayLabelStyle.ts`,
`overlays.schema.ts` and `overlays.api.ts` must be absent from the diff (spec FR-008). The reviewer checks
`git diff --stat origin/develop`.

## Components

### C1. `overlayLayers.ts` — two additions, nothing changed

- `newElement(kind): OverlayElement` — Text = today's `newLabel()` template (moved here so form and tests share
  it); Box/Ellipse = `{ kind, color: DEFAULT_OVERLAY_COLOR, normalizedX: 0.1, normalizedY: 0.1,
  normalizedWidth: 0.3, normalizedHeight: 0.3 }` (spec decision 2).
- `addedAnnouncement(kind, count)` → `"Label|Box|Ellipse added, position 1 of {count}"`.

`layerName`, `textOverlaps`, list/ordinal maps, `selectionAfterRemove`: unchanged.

### C2. `OverlayKindIcon.tsx` (new, `apps/shared/src/ui/composites/`)

`({ kind }) => <svg aria-hidden="true" focusable="false" data-kind={kind} …/>` — 16×16 viewBox, `currentColor`
stroke: Text = a "T" path, Box = `<rect>`, Ellipse = `<ellipse>`. No dependency, no token additions.

### C3. `OverlayLayerList.tsx`

- Prop `onAddLabel(): void` → `onAdd(kind: OverlayElement['kind']): void`.
- The "Add label" `Button` → `<DropdownMenu trigger={<Button … unavailable={readOnly || !canAdd}>Add</Button>}
  entries={[Text, Box, Ellipse]} align="start" />`. "Up to 8 elements" unchanged.
- **Unavailable trigger must open nothing.** `Button unavailable` emits `aria-disabled` but stays clickable
  (ADR-0151), and Radix opens on pointerdown/Enter regardless. The engineer either renders the bare `Button`
  (no `DropdownMenu`) while unavailable, or guards via the item handlers — the test pins "no menu opens"
  (`queryByRole('menu')` null), so whichever passes it is acceptable. Prefer the first: it touches no primitive.
- `add(kind)` keeps today's body (readOnly/canAdd guard, announcement, `pendingFocusCount`), announcing via
  `addedAnnouncement`.
- **Focus after add — known risk.** `DropdownMenu.onSelect` focuses the trigger synchronously, then Radix's
  `onCloseAutoFocus` returns focus to the trigger on close. The list's `pendingFocusCount` effect may run before
  that and lose. Requirement: the new chip ends focused (spec FR-002). If the test shows the trigger winning,
  the permitted fix is one **optional, additive** `DropdownMenu` prop (e.g. `onCloseAutoFocus` forwarded to
  `RadixDropdownMenu.Content`); absent ⇒ byte-identical, with `DropdownMenu.test.tsx` and
  `LayoutsPage.test.tsx` in the characterisation set. Do not add it pre-emptively.
- Chip children: `<OverlayKindIcon kind={element.kind} />` then the name. The chip keeps `className="max-w-full
  truncate"`; add `inline-flex items-center gap-1` only if the icon wraps.

### C4. `OverlayEditor.tsx` — kind branch

- `value: OverlayLabel` → `value: OverlayEditorValue` where `OverlayEditorValue = OverlayLabel | OverlayShape`
  (exported from this file, not `overlays.api.ts`). `onChange: (next: OverlayEditorValue) => void`.
- `const isShape = value.kind === 'Box' || value.kind === 'Ellipse'` (an `OverlayLabel` may omit `kind`;
  absent ⇒ Text, as `OverlayLabel`'s doc comment already says).
- Canvas `<Rnd>` style: `isShape ? overlayShapeStyle(value) : overlayLabelSurfaceStyle(value)`; children: the
  preview `<span>` for Text only. `aria-roledescription`: "Overlay label" / "Overlay shape". `aria-label`: today's
  `value.text || 'No text set'` / `value.kind`. Instructions `<p>`: today's text / the same sentence with "shape".
- Below the undo controls: the `Label text` input and `Font size` slider render **only for Text**;
  `OverlayGeometryFields` for both; `PlaceholderPreviewPanel` **only for Text**; `BackdropControls` for both.
- Every commit path already spreads `...value`, so `kind` and `color` round-trip. Check `emitNormalized` and
  `handleGeometryCommit` do so; if either builds a fresh object it must spread.
- **Text path byte-identical.** No reordering of JSX, no renamed testid, no wrapper element added around existing
  nodes. Conditional rendering is `{isShape ? … : existing}` / `{!isShape && existing}` around untouched subtrees.

### C5. `useOverlayEditHistory.ts`

Generic over the union: `sameOverlayLabel` → `sameOverlayValue(a, b)`: kinds differ ⇒ false; both shapes ⇒ the
four geometry fields; else today's six-field comparison verbatim. Signature types widen; no behavioural change
for labels (the suite proves it).

### C6. `OverlayDraftForm.tsx`

- Drop `selectedIsText ? <editor> : hasTextElement ? <shape notice> : <shapes-only notice>` → always the editor
  for `selectedElement` (guard `selectedElement !== undefined`). `value={field.value as OverlayEditorValue}`.
- `useListAllCameraChoicesQuery(undefined)` — remove `{ skip: !hasTextElement }` and its now-false comment.
  `hasTextElement` is deleted if it has no remaining reader.
- `addLabel()` → `addElement(kind)`: `append(newElement(kind), { shouldFocus: false })`; rest unchanged.
  `newLabel()` and its `DEFAULT_INPUT` coupling: `DEFAULT_INPUT.elements[0]` becomes `newElement('Text')`.
- Label-error alert: unchanged (already `selectedIsText`-gated).
- `OverlayCreatePage.tsx` / `OverlayEditPage.tsx`: header copy only if it says "labels" exclusively
  ("add labels" → "add labels and shapes"). No logic.

## Phase 4a — colours (declared here, ADR-0144)

**This issue is behaviour-changing → RED**, with a characterisation set for the behaviour it must not move.

### Characterisation (captured green before any change; must pass **unmodified** after)

`OverlayEditorCharacterisation`, `OverlayEditorKeyboard`, `OverlayEditorUndo`, `OverlayEditorBackdrop`,
`OverlayEditorInertElements`, `OverlayLabelParity`, `OverlayLabelCharacterisation`,
`OverlayLabelNodeCountCharacterisation`, `OverlayGeometryFields`, `useOverlayEditHistory.test.ts`,
`overlayLayers.test.ts`, `overlayLabelStyle.test.ts`, `OverlayEditorReseedRegression`, `DropdownMenu.test.tsx`,
`LayoutsPage.test.tsx`, `OverlayDraftFormChainAnnouncement`, `OverlayDraftFormChainRecovery`,
`OverlayDraftFormGetToken`, `OverlayDraftFormResolvePreview`, `OverlayDraftFormSaveGate`, `OverlayEditPage*`,
`OverlayCreatePage`, `overlays.schema.test.ts`, `e2e/overlays.spec.ts`,
`e2e/kiosk-paints-a-reordered-overlay.spec.ts` — plus every test in the files below **not** named as superseded.

### Superseded by this spec (declared now, rewritten in the red commit — not "adjusted to pass")

The owner ruled the "Add" menu and editable shapes; these assertions encode the slice-1 behaviour that replaces.

| File:line | Test | What changes |
|---|---|---|
| `OverlayDraftForm.test.tsx:572` | Shows a notice instead of the editor for a [Box]-only draft… | Notice → Box inspector. The "saves the Box unchanged" half is kept verbatim. |
| `OverlayDraftForm.test.tsx:880` | Makes no camera-choices request for a shapes-only draft | Inverted (spec decision 8). |
| `OverlayDraftFormLayers.test.tsx:218` | Shows a notice, not a label editor, when a shape is selected… | → geometry inspector, no text field; saves unchanged. |
| `OverlayDraftFormLayers.test.tsx:230` | Keeps the shapes-only notice when the set has no Text element | → first element selected, editable. |

**Activation-path-only edits** (the outcome assertions stay byte-identical; only the step that activates "add a
label" changes from clicking the `Add label` button to opening `Add` and choosing `Text`, and an
unavailable-control assertion names `Add` instead of `Add label`):
`OverlayLayerList.test.tsx:187,194,204`; `OverlayLayerListReview.test.tsx:73,90`;
`OverlayDraftFormLayers.test.tsx:150,172`; `OverlayDraftFormReview.test.tsx:60,78`;
`e2e/overlay-layer-list.spec.ts:37,85,105`. Any other line of these tests that has to change is evidence the
behaviour moved: **block, do not adjust**. The test-writer quotes the diff of each such file in its hand-back so
the reviewer can see only activation lines moved. The `OverlayLayerList` test host's `onAddLabel` prop rename is
part of the same activation edit.

### Red (observed failing on unmodified code)

- `overlayLayers.test.ts` additions → `newElement`, `addedAnnouncement` (fail on missing export — a compile red is
  acceptable only for these two pure functions; behavioural reds below must fail on assertion, so the engineer
  may need a behaviourless stub commit first, as spec 441 did at `a57f4c4d`).
- `OverlayLayerListAdd.test.tsx` (new): menu offers Text/Box/Ellipse in order; each calls `onAdd(kind)`; unavailable
  trigger opens no menu at the cap and while read-only; focus lands on the new chip; announcement per kind;
  kind icons `aria-hidden`, chip names unchanged.
- `OverlayEditorShape.test.tsx` (new): a Box/Ellipse value paints with `overlayShapeStyle`, is named by kind with
  roledescription "Overlay shape"; no text/font-size/preview; geometry fields present; drag, ArrowRight nudge,
  Ctrl+Z undo emit the same kind and colour.
- `useOverlayEditHistory.test.ts` additions: a shape echo is not read as a re-seed; a shape geometry change is a
  step.
- `OverlayDraftFormShapes.test.tsx` (new): add Box → POST body; select + drag Ellipse → PATCH body keeps colour;
  shapes-only draft opens editable with camera picker offered; 8 elements → Add unavailable; field-refused width
  never sent; 409 stale after adding a shape offers Reload and keeps it; 403 keeps the shape.
- `e2e/overlay-shapes.spec.ts` (new): spec §"Independent end-to-end test procedure" steps 2-4 and 6 against the
  real stack (reuse `e2e/support/management-overlays.ts`), GET order/geometry/colour via the API.

### Counterfactuals (each must turn a named test red; run, quote in the PR)

1. Paint the shape with `overlayLabelSurfaceStyle` → the shape-style test fails.
2. Render the text input for a shape → "no label text field for a shape" fails.
3. Build the shape edit as `{ kind: 'Box', ...geometry }` without spreading `color` → the colour round-trip test fails.
4. Restore `skip: !hasTextElement` → the shapes-only camera-picker test fails.
5. Drop `aria-hidden` from `OverlayKindIcon` → the chip-name test fails (name gains icon text) or the icon test fails.
6. Remove the unavailable guard on the Add trigger → "opens no menu at the cap" fails.
7. Make `sameOverlayValue` compare labels' fields for shapes → a shape history test fails.

## Measurement

None owed: no §IV leg is touched. The verification note records `git diff --stat origin/develop` showing no
kiosk/backend/contract/schema file.

## Risks

- **Focus after a menu add** — see C3; the bounded escape hatch is named and must be justified by a red test.
- **Radix menu in jsdom** — `apps/shared/src/test/radixJsdom.ts` already exists for this; the new tests import it
  as `DropdownMenu.test.tsx` does.
- **`OverlayEditor` is 867 lines** (ADR-0084 advisory 300 LOC/file). This slice adds a branch, not a split;
  extracting the Text inspector is a refactor for its own issue, not mixed in here (ADR-0036).
- **Shard filter** — new vitest/e2e files must be picked up by the existing globs; if a shard filter lists files
  explicitly, add the entries (memory: a missing entry fails deterministically).
