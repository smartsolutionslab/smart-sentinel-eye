# Data Model: An Overlay Seen on Its Tile (spec 466)

No persisted entity is added or changed. Everything below is client-side view state derived from existing data.

## Existing, read as they are

| Entity | Source | Fields the view reads |
|---|---|---|
| Designer cell | `GridDesignerValue.cells` (`gridDesignerModel.ts`) | `cameraIdentifier`, `overlayIdentifier` (`''` = none), `row`, `col`, `rowSpan`, `colSpan` — **including unsaved edits** |
| Grid | `GridDesignerValue.grid` | `rows`, `cols` |
| Overlay | `overlays.api` `Overlay`, `OverlayRevision` | revisions by state; the element set (`OverlayTextElement` \| `OverlayShape`), each with normalised position/size, colour, text |
| Resolved text | SystemVariables snapshot / `resolveOverlayText` | raw template → resolved text, in the operator's fab |
| Layouts | `listLayouts` (`chains`, `published`) | tiles' `overlayIdentifier`, for the usage count |

## New, client-only

### DisplayShape
A named wall-display shape the operator picks.
- `key`: one of `16:9`, `16:10`, `21:9`, `4:3`, `9:16`
- `widthUnits`, `heightUnits`: the ratio
- default `16:9`; not persisted in any service; at most remembered per browser

### TileGeometry (derived, not stored)
Given `DisplayShape`, `grid`, and one cell's `row/col/rowSpan/colSpan`, plus the preview area size:
- `cellWidthPx`, `cellHeightPx`: the cell's size on a display of that shape, scaled to fit the preview
- `viewerWidthPx`, `viewerHeightPx`: the 16:9 viewer box inside the cell (R1), after the wall's own letterboxing/clipping rule
- Invariants: spans never extend past the grid; a covered cell has no frame of its own (it is part of its covering cell)

### NeighbourStill
A cached capture for a camera while the view is open.
- `cameraIdentifier`
- `state`: `pending` | `captured` | `failed`
- `image`: a data URL or object URL, **released when the view closes**
- At most one capture in flight across all of them (existing `useFrameCapture` rule)

### OverlayUsage (derived)
- `overlayIdentifier`
- `tileCountAtLeast`: tiles across `listLayouts` chains (Draft and Published) plus the open layout's current unsaved cells (which replace that layout's saved ones)
- Reported as a lower bound (research R6)

## State transitions

- **Tile view**: closed → open(cell) → [editing(cell, overlay draft)] → closed. Closing never writes the layout; only the dialog's own Save does.
- **Overlay assigned/replaced in the view**: sets `overlayIdentifier` on that cell in the dialog's form (unsaved), same as the designer's select.
- **Overlay edit**: creates or reuses a draft of the overlay's chain through the existing mutations; the layout form is untouched.
