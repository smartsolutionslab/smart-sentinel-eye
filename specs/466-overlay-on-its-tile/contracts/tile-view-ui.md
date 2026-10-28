# Contract: Tile View (UI only)

No HTTP, message or schema contract changes. The only contract is the component boundary below.

## `TileView` props

| Prop | Type | Meaning |
|---|---|---|
| `open` | boolean | Whether the view is shown |
| `onOpenChange` | (open: boolean) => void | Close/open request |
| `grid` | `{ rows: number; cols: number }` | The designer's current grid (unsaved included) |
| `cells` | `ReadonlyArray<DesignerCell>` | The designer's current cells (unsaved included) |
| `cellIndex` | number | The populated cell being viewed |
| `onAssignOverlay` | (cellIndex: number, overlayIdentifier: string) => void | Writes the unsaved cell state in the dialog's form |
| `getToken` | `() => Promise<string \| null>` | Same token provider the editor's capture uses |
| `canEditOverlays` | boolean | The operator's existing overlay-write right |

## Behaviour

- Reading never writes anything.
- Editing writes only the overlay's own draft, through the existing overlay mutations; it never saves the layout.
- Without `canEditOverlays` the editor is mounted `readOnly` and a notice says why.
- The view always provides: a text readout of the frame (element kind, resolved text, position in percent, backdrop state) and a link into the existing overlay editor route.

## Failure contract

| Condition | Result |
|---|---|
| Camera picture cannot be captured | Neutral backdrop of the frame's shape + notice; overlay still shown and editable |
| Overlay has no Published revision | Draft shown with "the wall shows nothing" notice; if none at all, no overlay + offer to assign |
| Overlay archived after assignment | Shown as the wall shows it (no overlay) + notice |
| Neighbour capture fails/times out (10 s) | That neighbour keeps a neutral backdrop with its overlay |
| Stale conflict on overlay save | The existing stale-conflict handling and message, unchanged |
