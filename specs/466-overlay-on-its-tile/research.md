# Research: An Overlay Seen on Its Tile (spec 466)

Every finding below was read from the code at `develop` `659f74e7` unless marked *unverified*.

## R1 — What "the tile's shape" is on the wall

**Finding.** A wall tile is a CSS-grid cell (`LayoutGrid.tsx:237-241`: `h-screen`, `repeat(cols, minmax(0,1fr))` × `repeat(rows, ...)`, `gap-1 p-1`) holding a `flex … items-center justify-center` box (`LayoutGrid.tsx:605-608`) that centres a `CameraViewer`. The viewer root is `relative aspect-video w-full overflow-hidden … @container` (`CameraViewer.tsx:447`). Overlay elements are direct children of that root and positioned as fractions of it. So **the overlay's frame is always 16:9; the cell's shape only decides how large that 16:9 box is and whether it is letterboxed or clipped.** Whether a cell narrower in height than 16:9 clips the box or shrinks it was not established from reading alone.

**Decision.** The preview does not re-implement the geometry. It mounts the **same shared rendering** the wall uses (`CameraViewer`'s overlay layer, `overlayLabelSurfaceStyle`/`overlayShapeStyle`) inside a cell whose pixel size is computed from the chosen display shape, the grid and the span. Letterboxing, clipping and `cqw` text scaling then match by construction rather than by a second implementation.

**Rationale.** Spec 294 already proved both surfaces call `overlayLabelSurfaceStyle()` and scale type by `cqw` against their own container (`e2e/kiosk-label-scales-with-its-tile.spec.ts`, `e2e/overlay-editor-preview-scale.spec.ts`). Reuse keeps SC-002 true as the wall changes.

**Alternatives rejected.** A fresh canvas drawn from the data (second implementation, drifts — exactly the failure the two e2e specs were written against). A screenshot of the wall (needs a running wall; not an operator tool).

**Open, resolved in implementation by observation.** The clip-or-shrink behaviour of a sub-16:9 cell. Task: observe on the seeded nine-tile wall and record it before the first assertion on frame size is written.

## R2 — Display shape (Q: operator-chosen)

**Decision.** A small set of named display shapes held in the view's own state, default 16:9: 16:9, 16:10, 21:9, 4:3, 9:16 (portrait). The cell pixel size = (display width × cols-fraction, display height × rows-fraction) minus the wall's own `p-1`/`gap-1`, scaled to fit the preview area. The choice is **not persisted** (no schema change, no domain change); it resets with the view. `localStorage` may remember the last choice per browser as a convenience only.

**Alternatives rejected.** Persisting a display shape on the layout (a domain change the spec excludes). Reading it from a wall (no wall-display dimensions exist in the data — verified: `Layout` carries `gridRows`/`gridCols` and tiles only).

## R3 — Where the view lives (Q: designer's unsaved state)

**Finding.** `LayoutEditorDialog` owns a `react-hook-form` form of type `GridDesignerValue` (`name`, `grid`, `cells`). The cells hold `cameraIdentifier`, `overlayIdentifier`, `row`, `col`, `rowSpan`, `colSpan` — including unsaved edits.

**Decision.** The tile view is opened **from the designer** and receives the form's current `cells` and `grid` as props (read, via `form.watch`/`getValues`). It never reads a saved layout to decide what to show. Assigning an overlay in the view calls the form's `setValue` on that cell, so it is the same unsaved state the designer's own select changes.

**Consequence.** The view is a sibling of `GridDesigner` inside the dialog (a second dialog layer or a panel), not a new route.

## R4 — The overlay a tile shows, and resolved text

**Finding.** The wall reads the **Published** revision (`Tile` in `LayoutGrid.tsx` ~ `overlay?.revisions.find(r => r.state === 'Published')`). The editor reads a **Draft**. Placeholder resolution on the wall is `useGetOverlaySnapshotQuery({overlayIdentifier, fabId})`; in the editor it is `useResolveOverlayTextQuery` fed by the debounced draft text (`PlaceholderPreviewPanel`).

**Decision.**
- **Viewing** a tile shows the overlay as the wall would: its Published revision, text resolved through the same snapshot query in the operator's fab. If there is no Published revision (draft-only overlay), show the Draft with a visible "not published, the wall shows nothing" notice. This is a decision the spec leaves implicit; it is flagged for the owner in the plan.
- **Editing** switches the frame to the revision being edited (a draft) and resolves text through `useResolveOverlayTextQuery`, as the editor does.

## R5 — Editing in place (reuse, not rebuild)

**Finding.** `OverlayEditor` (`apps/shared/src/ui/composites/OverlayEditor.tsx`) is a controlled canvas (`react-rnd`) with `value`/`onChange`, a `canvasWidthPx/HeightPx` fit, `inertElements` (spec 441), `readOnly`, a backdrop picker, history (`useOverlayEditHistory`). `OverlayDraftForm` owns draft create/edit/save, the stale-conflict handling, and the legibility/placeholder advisories.

**Decision.** The tile view embeds `OverlayEditor` unchanged, fed a canvas size taken from R1's cell computation and a backdrop that is the captured frame for that tile's camera. Save, conflict, legibility and placeholder advisories come from `OverlayDraftForm` — the tile view hosts that form's logic, it does not duplicate it. **Revised 2026-10-29 (owner):** `OverlayDraftForm` already takes `canvasWidthPx/HeightPx`, `readOnly`, `editTarget`, `onDone`, `onCancel`, so the view embeds the form itself and no `useOverlayDraft` hook is extracted. It gains two optional props (backdrop camera, layout mode) whose defaults keep today's behaviour; the T001 baseline must pass unmodified. The hook extraction is deferred to when the chat editor needs the form taken apart.

**Chat-editor note (spec Assumptions).** Edits stay as the editor's existing value changes (`onChange(next)` with a whole element-set value) — discrete and replayable. No chat code is added.

## R6 — How many tiles use this overlay (FR-010)

**Finding.** There is no "which layouts use overlay X" endpoint. `listLayouts` returns `{chains, published}` for the caller's visible layouts; tiles carry `overlayIdentifier`. Overlays are fab-neutral (ADR-0115); layouts are fab-scoped.

**Decision.** Count client-side from `listLayouts` (both Draft and Published tiles, current layout's unsaved cells replacing its saved ones), and phrase it as "used on **at least** N tiles in layouts you can see". Honest about the bound.

**Risk recorded.** An operator who sees only some fabs undercounts. A server-side count would fix it and would be a new endpoint plus a cross-fab leak review (§VIII); not done here. If the owner wants an exact count, it is a separate issue.

## R7 — Backdrop pictures and the neighbours (Q: still frame + overlay)

**Finding.** No snapshot or thumbnail endpoint exists in `StreamDistribution` or `CameraCatalog` (searched `Api/` for snapshot/thumbnail/still). Stills exist only through `FrameGrabber` + `useFrameCapture`, which open a WHEP session, take one frame and tear down; the hook allows **one capture at a time** and bounds it at 10 s.

**Decision.**
- The open tile's backdrop uses the existing capture, started on open.
- Neighbours are captured **sequentially, one at a time**, nearest first, each bounded at 10 s, cached by camera for the life of the view. Until a neighbour's frame arrives (or if it fails) it shows a neutral backdrop with its overlay. No neighbour ever holds a session beyond its capture.
- Number of neighbours is bounded by the grid's cell count.

**Cost to state.** For an N-tile layout, opening the view causes up to N−1 short WHEP sessions on SFU/MediaMTX, one at a time. Each is the same session an operator's "Capture frame" opens today. This is operator-initiated, not on the event-to-overlay path (§IV), but it is real load; recorded for the verification note.

**Alternative rejected.** Adding a server snapshot endpoint: a new contract and a new trust-boundary surface for a preview; deferred.

## R8 — Permissions (Q: layout right opens, overlay right edits)

**Finding.** `RequireScope`/`sse.*` scopes gate the services; the console hides controls per granted scope (`Gated` in `router.tsx`). Overlay mutation endpoints already require the overlay write scope; layout endpoints their own.

**Decision.** No new scope. How the console learns an operator's overlay-write right is **not yet established**: the navigation declares read scopes only (`shellEntries.ts`). Task T010a settles it and replaces this paragraph. Either way, without the right the view mounts `OverlayEditor` with `readOnly` and shows a notice; the fallback if no write scope is readable is to offer the edit and show the existing refusal on a 403. The backend is unchanged, so the server still enforces; the UI only avoids offering what would be refused.

## R9 — Keyboard and screen readers (Q: view yes, in-view edit no)

**Decision.** The view uses existing primitives (`Dialog`, `Button`, `Select`) so focus management, labels and live regions come for free. Reading the frame is exposed as text, not only pixels: a status region lists each element (kind, resolved text, position as percent) and the backdrop state, so a screen-reader operator gets the same information as the picture. In-view dragging may remain pointer-only; the view carries a "Open in the overlay editor" link to the existing keyboard-operable route. No new a11y debt beyond what Q5 accepted.

## R10 — Proving SC-002 (the match to the wall)

**Finding.** Two Playwright specs already assert type-to-container ratios on the wall and in the editor and share a `live-video-wall` support module.

**Decision.** A third spec drives the same seeded nine-tile wall, takes a screenshot of the overlay layer on the wall tile (camera hidden by the existing support helper, if it offers one; otherwise the overlay layer element only), opens the same tile in the view at the matching display shape, and compares: element boxes within 1 % of tile width/height, text line-break positions, computed colour and opacity. Browser pixel equality on anti-aliased text is not asserted; computed style and bounding boxes are, and text wrap is asserted by line count and per-line text from `Range.getClientRects`. This is a measurable stand-in for "pixel equal" and the plan says so.

**Honest limit.** Colour/opacity equality is asserted on computed style of each overlay element, not on rasterised pixels of blended video. Stated in the verification note.
