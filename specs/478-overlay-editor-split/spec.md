# Spec 478 — `OverlayEditor` is broken up by responsibility

**Feature Branch**: `478-overlay-editor-split`
**Created**: 2026-11-02
**Status**: Draft
**Issue:** #3191 (feature-level; found by the frontend refactoring survey; sibling of #3190, #3192, #3193, #3198).
**ADRs:** ADR-0037 (phases), ADR-0084 (frontend 50-LOC-per-function row: recorded there as **not yet enforced**),
ADR-0139 / ADR-0144 (phase 4a), ADR-0169 (the component stays Redux-free: the caller owns the queries).
**No new ADR.** A decomposition inside existing conventions; follows specs 476 and 477, whose probe-first method it reuses.
**Latency budget:** none — authoring component in the operator console; it is not on the event-to-overlay path and the kiosk
does not import it (checked: its only production caller is `management-web`'s `OverlayDraftForm`).
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the existing
`OverlayEditor{Characterisation,Keyboard,Undo,Colour,Shape,Backdrop,InertElements}.test.tsx` and
`OverlayLabelNodeCountCharacterisation.test.tsx`; they must pass with every assertion **unmodified**. An assertion that has
to be edited is evidence the behaviour moved: block, do not adjust (CLAUDE.md).

## Problem

`apps/shared/src/ui/composites/OverlayEditor.tsx` is **911 lines**; one component, `OverlayEditor`, is **576** (lines
336–911), against ADR-0084's 50 LOC per function. Measured on `develop` (2026-11-02), not only grepped:

- **34 hook calls** in the file (the issue said 31): 12 `useCallback`, 6 `useState`, 3 `useRef`, `useEffect`, `useId`, plus
  `useOverlayEditHistory` and `useFrameCapture`. The generic-aware count, which earlier specs' first counts missed.
- **No `eslint-disable`** in the file, so there are no suppressions to carry (unlike 476 and 477).
- The covering family is **8 files, ~2,900 lines** beside `OverlayGeometryFields.test` (801) and `useOverlayEditHistory.test`
  (519), which cover units that already exist: `OverlayEditorKeyboard` 718, `OverlayEditorUndo` 937, `OverlayEditorBackdrop` 359,
  `OverlayEditorCharacterisation` 242, `OverlayEditorShape` 235, `OverlayEditorColour` 184, `OverlayEditorInertElements` 138,
  `OverlayLabelNodeCountCharacterisation` 81.
- Concerns interleaved in the file, each with comments recording a real failure:

  | # | Concern | Where (lines) |
  |---|---|---|
  | A | Pure geometry math: `clamp01`, `clampSize`, `quantize`, `quantizeBoundFloor`, `nudgePosition`, `resizeSize` and their step constants | 106–209 |
  | B | Pure announcement text: `AnnounceAxis`, the axis labels, `edgeName`, `buildAnnouncement`, `arrowRunKey` | 211–242 |
  | C | Canvas styling: the focus-ring style, the checkerboard, `canvasBackgroundStyle`, and `InertElement` | 244–325 |
  | D | Emitting a change: `emitNormalized`, `emitGeometry` (the `clamp01`/`clampSize` seam over `commit`) | 374–407 |
  | E | Drag and resize: the in-flight `preview` readout, `geometryFromPixels`, the four `Rnd` handlers | 409–472 |
  | F | Typed-entry commits: `handleGeometryCommit`, `handleColorCommit` (deliberately **not** through `emitNormalized`) | 474–496 |
  | G | The debounced geometry announcer: timer, `lastAnnounced`/toggle (the zero-width-space repeat fix), cleanup | 504–537 |
  | H | The label keyboard: `handleLabelKeyDown` (move/resize, step size, refusal edge) and `handleLabelKeyUp` | 539–604 |
  | I | Undo/redo: the token-keyed announcer, `handleUndoClick`/`RedoClick`, `handleRootKeyDown` | 616–670 |
  | J | The backdrop and frame capture: `backdrop`, `capturedFrame`, `selectedCamera`, `useFrameCapture`, four handlers | 672–706 |
  | K | The JSX: canvas + `Rnd`, instructions, live regions, undo/redo buttons, label-text and font-size fields, geometry/colour fields, preview panel, backdrop controls, the frame grabber seam | 708–911 |

### What the issue does not settle (premise check, 2026-11-02)

1. **A decomposition, not a deduplication: the line total will not fall.** Expect the file total to **rise**, as in 476
   (+42 %) and 477 (+26 %). The value is a component that reads as a list of what it composes; ADR-0084's limit is not
   enforced, so no failing gate is being fixed. Stated up front, with a measure-and-ask step after the first extractions.
2. **The pure functions are not exported**, so the only way to test them is through the editor's keyboard. Moving them to
   `overlayGeometry.ts` makes them importable; whether to add unit tests for them is a plan decision (a direct test would be a
   new test beyond the covering family, not a change to it). The family's keyboard tests are the characterisation.
3. **The covering family has not been checked concern by concern, and specs 476/477 found a large family still left a third
   to a half of deliberate breaks unnoticed.** The plan must **probe first** (break each concern once in the unsplit file,
   run the family, read what failed) and write a covering test, in a **new file**, before moving any concern whose break
   nobody noticed. The arithmetic is the likeliest to be well covered (it is the subject of the Keyboard file); the two
   announcers, the typed-entry bypass of the clamps, and the capture handlers are the likeliest not to be.
4. **Two ordering facts are load-bearing.** `useOverlayEditHistory`'s `commit` and `endRun` are shared by the drag
   handlers, the keyboard handlers, the text/font fields and the blur handler; the extracted units must receive them rather
   than each calling the hook, or the undo history splits. And `DOM order` of the live regions, the undo buttons and the
   label text input is pinned by the Keyboard tests (`OverlayEditorKeyboard.test.tsx:103`): the JSX moves must not reorder
   siblings.
5. **The component is generic** (`OverlayEditor<T extends OverlayEditorValue>`), and `commit({ ...label, text } as T, …)`
   relies on that. Units that take `value` must keep the generic or use the narrower shape they need without a new cast.
6. **The props and exports stay.** `OverlayEditorProps`, `OverlayEditor` and the `OverlayEditorValue` type are imported by
   `OverlayDraftForm`, `OverlayLabelParity.test.tsx` and several tests; the props interface is documented in detail and moves
   with nothing.

## Scope

**In:** `OverlayEditor.tsx`: its concerns moved into modules **next to the file** in
`apps/shared/src/ui/composites/` (a folder `overlayEditor/` mirroring `tileView/`, `layoutEditor/` and `overlayDraftForm/`),
one extraction per commit, each green on its own (bisect).

**Out (explicitly):**
- Any behaviour change, including "obvious" fixes found on the way (two issues, per CLAUDE.md).
- `useOverlayEditHistory`, `useFrameCapture`, `OverlayGeometryFields`, `OverlayColorFields`, `BackdropControls`,
  `PlaceholderPreviewPanel`, `overlayLabelStyle` (already separate); `OverlayDraftForm` and every caller.
- The props interface and the exports of `OverlayEditor.tsx`.
- New abstractions beyond what the split needs; moving anything to a wider home than `composites/`.

## User Scenarios & Testing

### User Story 1 (P1) — The editor behaves exactly as before

As an operator I move, resize and nudge a label or shape the same way, hear the same announcements, undo and redo the same
steps, and see the same backdrop. As the maintainer I can prove it.

**Independent test:** the eight covering test files pass with no line edited.

```gherkin
Scenario: the existing tests are the safety net
  Given the OverlayEditor*.test.tsx family and OverlayLabelNodeCountCharacterisation.test.tsx
  When they are run after each extraction
  Then every test passes
  And git diff shows no line of those test files changed

Scenario: a held arrow key is one undo step and one announcement
  Given a label with focus
  When an arrow key is held down and then released
  Then one undo step is recorded and one debounced announcement is made, as before

Scenario: typed entry is not clamped
  Given a stored off-grid position
  When the operator commits another field by typing
  Then the other three values travel forward untouched, as before

Scenario: a refused press names the edge
  Given a label at the canvas edge
  When an arrow key would carry it past
  Then the announcement names the edge, as before
```

### User Story 2 (P2) — The component is short enough to read

```gherkin
Scenario: the component is short
  When the split is done
  Then OverlayEditor is well under its former 576 lines
  And each extracted unit is under 50 code lines where the logic allows (the issue's own bar)
```

### Edge cases

- **One history.** Every `commit`/`endRun` still goes through the single `useOverlayEditHistory` instance.
- **Callback identity.** The 12 `useCallback`s have dependency arrays that matter (`Rnd` handlers, `queueAnnouncement`);
  they are copied verbatim into whichever unit owns them.
- **`readOnly`** is forwarded to the colour field only; the keyboard and the drag handlers do not read it. That must not
  change — and may be unpinned.
- **The announcer's repeat fix** (trailing zero-width space toggled on a repeated message) and the undo announcer's
  `key`-bumping fix are different mechanisms for the same screen-reader problem; neither may be unified.
- **Comments are evidence, not decoration.** Each records a real failure and moves with its code, once.
- **A first extraction that does not help is a stop sign** (#3190, #3198, #3193, #3192): measure, then ask.

## Requirements

- **FR-001** Every test in the eight covering files passes after each commit; none of those files is modified.
- **FR-002** `OverlayEditor` composes extracted modules for the concerns A–K (the plan decides which become functions,
  hooks and components); its own body is the composition and the JSX that remains.
- **FR-003** No change to emitted values, `onChange` payloads, rendered text, `aria-*` attributes, `data-testid`s, class
  names, ids, keys, DOM order, or the order other hooks run in.
- **FR-004** The exports and props of `OverlayEditor.tsx` are unchanged; no caller changes.
- **FR-005** No `eslint-disable` is added (the file has none).
- **FR-006** Comments move with their code; none is dropped, none duplicated; any "above"/"below" pointer is rewritten to
  name the new unit.
- **FR-007** Each commit builds and passes the family on its own; one concern per commit.
- **FR-008** No change outside `apps/shared/src/ui/composites/` (new files and the editor), `specs/478-…` and
  `.specify/feature.json`.
- **FR-009** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `shared` and `management-web`
  (the caller), and `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** The eight covering files: same test count and names before and after, all passing, `git diff` on them empty.
- **SC-002** `OverlayEditor`'s own length and each new unit's length, reported before/after **with the file total**, code
  lines and comment lines separately.
- **SC-003** A search of the editor and its new units for `eslint-disable` finds none.
- **SC-004** The whole `shared` and `management-web` suites pass, with counts and durations before and after.
- **SC-005** For every concern (A–K), a deliberate break is noticed by a named test; where the family notices nothing, a new
  characterisation test is written first, green on the unsplit file, and proved able to fail (the message read). Where a
  behaviour is defended jointly with another and cannot be pinned alone, that is recorded with the double-break evidence.

## Assumptions

- The family is strong on the arithmetic and the keyboard map (it is the subject of the largest files); the probes decide
  where it is not.
- Extracting custom hooks preserves behaviour as long as they are called unconditionally, in the same order, and share the
  one `useOverlayEditHistory` instance.

## Dependencies

Independent of #3217/#3218 (specs 476/477). A caller (`OverlayDraftForm`) is only re-run, not edited.
