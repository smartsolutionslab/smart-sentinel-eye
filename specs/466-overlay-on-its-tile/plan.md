# Implementation Plan: An Overlay Seen on Its Tile

**Branch**: `2521-overlay-on-its-tile` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/466-overlay-on-its-tile/spec.md`

## Summary

Add a **tile view** to the management console's layout designer: open a populated cell and see its camera picture with its overlay over it, text resolved, in a frame shaped like the wall tile at an operator-chosen display shape, with the other tiles around it as stills; edit the overlay there. Frontend only. No backend, contract, migration, domain or ADR change.

The approach is reuse, not a second renderer: the frame mounts the same shared rendering the kiosk uses, so SC-002 holds by construction and a Playwright spec proves it. Editing embeds the existing `OverlayEditor` and the existing draft/save/conflict logic.

## Technical Context

**Language/Version**: TypeScript (React 18, Vite), the two-app monorepo (`apps/management-web`, `apps/shared`)

**Primary Dependencies**: Redux Toolkit + RTK Query (ADR-0075), Radix primitives (ADR-0077), Tailwind tokens (ADR-0078), React Hook Form + Zod (ADR-0079), `react-rnd` (already in `OverlayEditor`)

**Storage**: None. The display-shape choice is view state; at most a per-browser `localStorage` convenience (guarded, optional)

**Testing**: Vitest + Testing Library for components; Playwright e2e for SC-002 against the seeded nine-tile wall (`e2e/support/live-video-wall`)

**Target Platform**: Operator console in a desktop browser (management-web). Not the kiosk

**Project Type**: web application — frontend slice only

**Performance Goals**: an edit reflected in the frame within 1 s (SC-004); no change to the event-to-overlay path (FR-014, SC-006)

**Constraints**: no new permission (FR-012); existing editor unchanged (FR-013); view keyboard-operable for reading (FR-015); at most one WHEP capture in flight (existing `useFrameCapture` rule)

**Scale/Scope**: layouts up to the designer's cell cap; one new feature folder, ~4–6 new components, one extracted hook

## Constitution Check

*GATE: passes before Phase 0; re-checked after Phase 1 — unchanged.*

| Principle | Result |
|---|---|
| I On-prem first | No new runtime resource, no cloud dependency. Pass |
| II Value objects | Frontend only; no domain model touched. N/A |
| III Context isolation | No cross-context reference; reads existing HTTP APIs the console already uses. Pass |
| IV Latency budget | **No leg affected.** The view is operator-side, off the event-to-overlay path (FR-014). The WHEP captures it starts are the same sessions "Capture frame" opens today and are not on the 800 ms path. Recorded in the verification note, not as a budget claim |
| V Spec-driven | Spec 466 → this plan → tasks. Pass |
| VI Aspire composition root | No new resource. N/A |
| VII Observability | §VII's dashboard rule binds *implemented legs* (ADR-0117); this adds none. Pass |
| VIII Safe at trust boundaries | No new endpoint, no new scope. The client hides what the server already refuses (R8). The exact-count risk (R6) stays a client-side bound rather than a new cross-fab surface. Pass |
| IX Forward-compat interfaces | N/A |
| Testing | New behaviour starts red (ADR-0139); the one refactor (extracting the draft hook) is behaviour-preserving, characterised green first. Pass |

**ADR needed?** No. Nothing here changes a locked decision. Two choices the owner may want to see are listed under *Decisions for the owner* below; neither is architecture.

## Decisions taken by the owner (2026-10-29)

1. **A tile whose overlay has no Published revision** (research R4) shows the Draft with a "the wall shows nothing" notice, so an operator can design before publishing.
2. **The tile-count in FR-010 is a lower bound** (R6): "at least N tiles in layouts you can see". An exact count needs a server endpoint and a cross-fab review; deferred, and a separate issue if wanted.
3. **Neighbour stills cost up to N−1 short WHEP sessions per open** (R7), one at a time, each bounded at 10 s. Accepted as an operator action; to be quoted in the verification note.

## Project Structure

### Documentation (this feature)

```text
specs/466-overlay-on-its-tile/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── tile-view-ui.md  # Phase 1: the component contract (no HTTP contract changes)
└── tasks.md             # Phase 3 — not created here
```

### Source Code (repository root)

```text
apps/
  shared/src/ui/composites/
    OverlayEditor.tsx                 # unchanged
    CameraViewer.tsx                  # unchanged; its overlay layer is what the frame reuses
    useFrameCapture.ts                # unchanged; sequential captures are scheduled by the new hook
  management-web/src/features/
    overlays/OverlayDraftForm.tsx     # embedded by the view; gains optional props only (backdrop camera, layout mode); defaults keep today's behaviour
    layouts/
      GridDesigner.tsx                # one new control per populated cell: "Open tile view"
      LayoutEditorDialog.tsx          # hosts the tile view; passes cells/grid down
      tileView/
        TileView.tsx                  # the view: frame + chooser + neighbours + notices
        TileFrame.tsx                 # one tile at a given pixel size (backdrop + overlay layer)
        displayShapes.ts              # named shapes + cell-size computation
        useNeighbourStills.ts         # sequential capture queue, cache, bounds
        useOverlayUsage.ts            # at-least-N count from listLayouts + unsaved cells
        TileViewReadout.tsx           # the text equivalent of the frame (R9)

e2e/
  overlay-tile-view-matches-the-wall.spec.ts   # SC-002 against the seeded nine-tile wall
```

**Structure Decision**: all new code lives under `apps/management-web/src/features/layouts/tileView/`, with the view embedding `OverlayDraftForm` itself so the editor and the view cannot drift (owner decision 2026-10-29: embed, no `useOverlayDraft` extraction; the hook can follow when the chat editor needs the form taken apart). No backend project is touched.

## Phase plan for tasks (input to `/speckit-tasks`)

1. **Characterise, green first.** Baseline for `OverlayDraftForm` (done, T001). No extraction; the form only gains optional props in US2, and the baseline must pass unmodified.
2. **US1 view, red first.** `displayShapes` + `TileFrame` + `TileView`: frame at chosen shape, overlay with resolved text, no-overlay and no-picture notices, readout.
3. **Observe the cell fit** on the seeded wall (R1 open item) and pin it in a test before asserting frame size.
4. **US2 edit.** Embed `OverlayDraftForm` (canvas size from `displayShapes`); read-only without the overlay-write right; shared-overlay count notice; open-in-editor link.
5. **US3 neighbours.** `useNeighbourStills` queue; read-only neighbour frames; open tile marked; spanned tiles.
6. **SC-002 spec.** Playwright comparison against the wall.
7. **Phase 5 verification** with the figures SC-004 and SC-006 call for.

## Complexity Tracking

No constitution violations to justify.
