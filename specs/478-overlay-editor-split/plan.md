# Implementation Plan: `OverlayEditor` is broken up by responsibility

**Branch**: `478-overlay-editor-split` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/478-overlay-editor-split/spec.md`

## Summary

Move the eleven concerns of the 576-line `OverlayEditor` component into pure modules, small hooks and two sub-components
**next to the file**, one extraction per commit, each green on its own. **First probe, then pin, then move**: break each
concern once in the unsplit file, see whether the covering family notices, and write characterisation tests, in **new
files**, for what it does not. Behaviour-preserving, so **characterisation** (ADR-0139/0144 phase 4a). Reuses the method of
specs 476 and 477 (the latter found 30 of 59 breaks unnoticed in a 3,857-line family).

**Sizing, stated before building.** A decomposition: the file total will rise (476: component −68 %, total +42 %; 477:
component −59 %, total +26 %). The value is a component that reads as a list of what it composes; ADR-0084's 50-LOC row is
recorded as not enforced. There is a **measure-and-ask step** after the first four extractions (T-D1), as in #3190, #3198,
#3193 and #3192.

## Technical Context

- **Stack**: TypeScript, React 19.3, `react-rnd`, Vitest + Testing Library; the component lives in `apps/shared` (jsdom
  tests under `apps/shared`), its one production caller is `apps/management-web`'s `OverlayDraftForm`.
- **Baseline (2026-11-02, `develop` after #3218):** `OverlayEditor.tsx` 911 lines (component 576, 34 hook calls, 0
  `eslint-disable`); covering family 8 files / ~2,900 lines; run from `apps/shared` for the editor and `apps/management-web`
  for the caller.
- **Touches**: new files in `apps/shared/src/ui/composites/overlayEditor/`, the editor, and new test files for the gaps. No
  existing test file, no caller, no config.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | Characterisation: the family unmodified, plus gap tests written first and seen green on the unsplit file. |
| ADR-0036 — smallest change, no speculative generality | Each unit has one caller (the point of a decomposition, stated not hidden); nothing is generalised beyond the split; no new abstraction over `commit`. |
| ADR-0084 (50 LOC, not enforced) | The issue's bar for "units where the logic allows". |
| ADR-0169 — shared composites stay Redux-free | Unchanged: no unit imports a store or an RTK hook. |
| Lane may not weaken a gate | No test edited; no suppression added or needed. |
| No new ADR | Decomposition inside existing conventions. |

No violation.

## Research (decisions that shaped the plan)

### R1 — What pins what (read from the family's names, then probed)

| Concern | Visibly pinned by | Probe expectation |
|---|---|---|
| A geometry math | `OverlayEditorKeyboard` (718 lines: step sizes, bounds, quantum, floor, off-region motion) | well pinned; probe each term once |
| B announcement text | `OverlayEditorKeyboard` (edge names, axis labels) | probe each of the eight edge strings and the four axis labels |
| C canvas style, `InertElement` | `OverlayEditorCharacterisation` (checkerboard), `…Backdrop`, `…InertElements` | probe focus ring, white/black/captured |
| D emit (`emitNormalized`) | Keyboard, Characterisation (drag) | clamps pinned; probe each |
| E drag/resize and the live readout | Characterisation, `OverlayGeometryFields` (preview) | **probe**: preview cleared on stop, clamped in flight |
| F typed-entry commits | `OverlayGeometryFields.test` (unit), Colour | probe the deliberate **bypass** of the clamps (spec 151 FR-006) |
| G debounced announcer | Keyboard | **probe** delay, the repeat toggle, clear-on-new, cleanup on unmount |
| H label keyboard | Keyboard | probe Alt/Meta, non-arrow, Shift, Ctrl, keyup/blur `endRun` |
| I undo/redo | `OverlayEditorUndo` (937 lines) | probe the four strings, the `key` bump, Ctrl+Y vs Meta+Y |
| J backdrop and capture | `OverlayEditorBackdrop` (359) | **probe** camera-change cancel, empty-camera guard, captured → backdrop |
| K JSX | most of the above | probe accessible name fallback, `aria-roledescription`, `maxLength`, font range, the `getToken` gating |

### R2 — Method: break, observe, then write what is missing

For each row, apply **one deliberate break** to the unmigrated editor (scratch, not committed), run the whole family, record
which test failed and why, or "none". Planned probes (a script of named breaks, each asserted to match exactly once, restored
from git after; run **one vitest at a time** — memory on this machine is tight):

- **A** `clamp01` NaN → NaN; `clampSize` floor removed (`value <= 0` form); `quantize` removed; `quantizeBoundFloor` epsilon
  removed; `nudgePosition` bound dropped; `resizeSize` bound dropped; `resizeSize` floor dropped; `FINE_STEP`/`COARSE_STEP`
  swapped; `MIN_NORMALIZED_SIZE` changed.
- **B** each of the eight `edgeName` strings; each axis label; `arrowRunKey` mode dropped; `buildAnnouncement` without edge.
- **C** focus ring removed; captured background position; white/black swapped; `InertElement` `aria-hidden` dropped.
- **D/E** `emitNormalized` without `clamp01`; without `clampSize`; `handleDrag` not setting the preview; `handleDragStop`
  not clearing it; `handleResizeStop` not clearing it; `geometryFromPixels` unclamped.
- **F** typed commit routed through `emitNormalized`; colour commit not `'atomic'`.
- **G** delay changed; repeat toggle removed; timer not cleared on a new message; no cleanup on unmount.
- **H** Alt not ignored; Meta not ignored; non-arrow not ignored; no `preventDefault`; Shift ignored; Ctrl ignored; keyup
  `endRun` for non-arrows; blur `endRun` removed.
- **I** each of the four announcement strings; `token` not bumped; Ctrl+Y accepts Meta; Shift+Z not redo.
- **J** camera change without `cancel`; capture with an empty camera; captured not switching the backdrop; `initialCamera`
  ignored; `getToken` gating on `renderFrameGrabber` removed; `Fragment key` removed.
- **K** accessible-name fallback; `aria-roledescription`; `maxLength`; font-size range; `onPointerUp` `endRun`; instructions
  text for shapes; `readOnly` not forwarded to the colour field; live-region order swapped.

A probe that bites is recorded in `verification.md` and needs no new test. One nobody notices gets a new test **before** the
extraction touches that concern; a break masked by another term is checked with a **double break** and, if it cannot be
pinned alone, recorded as defended jointly.

### R3 — New tests go in **new files** (FR-001 forbids editing the eight)

Named by concern, created only for what the probes find unpinned (e.g. `OverlayEditorAnnouncer.test.tsx`,
`OverlayEditorCapture.test.tsx`, `OverlayEditorGestures.test.tsx`). Each is **green on the unsplit file**, then proved able to
fail by a break, with the failure **message read**, not just counted. Direct unit tests for the now-importable pure functions
are a **decision after the probe**: if the arithmetic is fully pinned through the keyboard, none is added (no speculative
tests); if a term is not, the pure function gets the test, which is cheaper than driving it through the DOM.

### R4 — The units (names are proposals; each ≤ 50 code lines where the logic allows)

New folder `apps/shared/src/ui/composites/overlayEditor/` (mirroring `tileView/`, `layoutEditor/`, `overlayDraftForm/`):

| Unit | Kind | Holds | Concern |
|---|---|---|---|
| `overlayGeometry.ts` | pure | `clamp01`, `clampSize`, `quantize`, `quantizeBoundFloor`, `nudgePosition`, `resizeSize`, the step/size constants, with their long comments | A |
| `overlayAnnouncement.ts` | pure | `AnnounceAxis`, labels, `edgeName`, `buildAnnouncement`, `arrowRunKey` | B |
| `canvasStyle.ts` | pure | `FOCUS_RING_STYLE`, `CHECKERBOARD_BACKGROUND`, `canvasBackgroundStyle` | C |
| `InertElement.tsx` | component | `InertElement` | C |
| `useGeometryAnnouncer()` | hook | `liveMessage`, `queueAnnouncement`, the timer, the repeat toggle, the unmount cleanup | G |
| `useUndoRedoControls({ undo, redo })` | hook | the token-keyed announcement, the two click handlers, `handleRootKeyDown` | I |
| `useEmitNormalized(value, commit)` | hook | `emitNormalized` | D |
| `useCanvasGestures({…})` | hook | the live `preview`, `geometryFromPixels`, `emitGeometry`, the four `Rnd` handlers | E |
| `useLabelKeyboard({…})` | hook | `handleLabelKeyDown`, `handleLabelKeyUp` | H |
| `useBackdropCapture(initialCamera)` | hook | `backdrop`, `capturedFrame`, `selectedCamera`, `useFrameCapture`, the four handlers | J |
| `LabelFields` | component | the label-text input and the font-size range | K |

`OverlayEditor` keeps the composition, the **typed-entry commits (F)** — two ten-line callbacks whose deliberate bypass of the
clamps is clearer next to the `emitNormalized` seam they avoid — the focus flag, the derived values (`shape`, `label`,
`previewText`, pixel box, accessible name) and the remaining JSX (canvas + `Rnd`, instructions, live regions, buttons,
the fields, the preview panel, `BackdropControls`, the grabber seam), its exports and its props.

### R5 — What must not change (checked per extraction)

- **One history.** `useOverlayEditHistory(value, onChange)` is called **once**, in `OverlayEditor`; `commit`, `endRun`,
  `undo`, `redo` are passed to the units. No unit calls the hook.
- **DOM order of siblings is preserved** (instructions → geometry live region → undo/redo buttons → undo live region → label
  fields → geometry fields → colour fields → preview panel → backdrop controls → grabber). `LabelFields` replaces the
  `label !== undefined &&` block **in place**.
- **No hook becomes conditional**; the relative order of stateful hooks inside a moved cluster is kept; dependency arrays are
  copied verbatim.
- **The generic stays.** Units that take `value` use the narrow shape they read; the `as T` casts stay where `T` is in scope
  (in `OverlayEditor` and `LabelFields`'s call sites), not invented in a unit.
- **The two repeat-announcement fixes stay different** (the zero-width-space toggle for the debounced announcer; the
  key-bumped `<span>` for undo); no shared announcer is introduced.
- **Each comment moves with its code once; any "above"/"below" pointer is rewritten** to name the new unit (verified by a grep
  for the words after each move). The `FR-xxx` / `phase 6` evidence comments are kept whole.
- **No `eslint-disable` appears**; the file has none and none is added.

### R6 — Not in this plan

Direct tests beyond what the probes require; `OverlayDraftForm` and any caller; `useOverlayEditHistory` / `useFrameCapture`;
changing the props; moving anything to a wider home; any behaviour change.

## Project Structure

```text
apps/shared/src/ui/composites/
  OverlayEditor.tsx                           (shrinks: composition + JSX)
  overlayEditor/                              (new folder beside the editor)
    overlayGeometry.ts
    overlayAnnouncement.ts
    canvasStyle.ts
    InertElement.tsx
    useGeometryAnnouncer.ts
    useUndoRedoControls.ts
    useEmitNormalized.ts
    useCanvasGestures.ts
    useLabelKeyboard.ts
    useBackdropCapture.ts
    LabelFields.tsx
  OverlayEditor<Concern>.test.tsx             (new: gap tests, as the probes decide)
specs/478-overlay-editor-split/
```

## Delivery: one PR, commits that each stay green

1. Gap tests (new files), **green on the unsplit file**, with their breaks proved.
2. Extractions, least coupled first: `overlayGeometry.ts`, `overlayAnnouncement.ts`, `canvasStyle.ts` + `InertElement.tsx`,
   `useGeometryAnnouncer` → **T-D1 measure-and-ask** (component length, file total, hook calls left, inputs per unit) → then
   `useUndoRedoControls`, `useEmitNormalized`, `useCanvasGestures`, `useLabelKeyboard`, `useBackdropCapture`, `LabelFields`.
3. Task ticks and the verification note.

Every commit: the eight covering files and the new gap files run (from `apps/shared`), `eslint --max-warnings 0`,
`tsc --noEmit`, `prettier --check`; the caller's tests (`OverlayDraftForm*`, `OverlayEditorReseedRegression`) re-run from
`apps/management-web` at the T-D1 gate and at the end.

## Verification (what "unchanged" means, concretely)

Recorded on the unmigrated tree, repeated after:

- **Family:** sorted `file :: full name :: status` and sorted assertion lines of the 8 files — `diff` empty; `git diff
  origin/develop...HEAD` on them empty (FR-001).
- **Probe matrix:** every R2 break, before (family) and after (family + gap files), in `verification.md`; double breaks for
  any jointly defended behaviour.
- **Gap tests:** green before and after; each failing for the right reason under its break (message quoted).
- **Per extraction:** the key break applied to the **new unit** (not the old location) fails the matching test there.
- **Lengths (SC-002):** the component, every unit (code and comment lines separately) and the **file total**.
- **Greps:** no `eslint-disable` in the editor or `overlayEditor/` (SC-003); distinctive comment phrases once; no stale
  "above"/"below" pointers.
- Whole `shared` and `management-web` suites, counts and durations; `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check`; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`).

## Lessons carried from specs 470–477 (each cost time once)

Anchor the working directory in every command (print `pwd`); write long notes and scripts with the file tool, not a heredoc;
`grep -E` with `\|` matches a literal pipe; count hook calls generic-aware; grep for **other uses** before removing an
import; format every generated file each step; add only the step's files to a commit; a counterfactual must fail for the
right reason, so read the message; **branches are cut from `origin/develop`, not the local `develop`** (a stale local one
cost a rebase in 477); run **one vitest at a time** and launch long runs as a direct background command (a trailing `&`
dies with the shell, and memory pressure kills the second job); a break applied to the file must be **restored before
anything else runs**; every spec rewrites `.specify/feature.json`, so each rebase conflicts there — keep the branch's own value.

## Risks

| Risk | Mitigation |
|---|---|
| Two `useOverlayEditHistory` instances split the undo history | R5 "one history"; the `OverlayEditorUndo` file (937 lines) covers it; probe I. |
| A moved `useCallback` loses its dependency list and `Rnd` re-renders or goes stale | Dependency arrays copied verbatim; the Characterisation and Backdrop files; probe E. |
| Sibling order changes and `OverlayEditorKeyboard.test.tsx:103` fails | R5; `LabelFields` replaces its block in place. |
| The typed-entry bypass of the clamps is "tidied" into `emitNormalized` | F stays in the editor (R4); probe F. |
| A unit needs more than ~8 inputs (`useCanvasGestures`, `useLabelKeyboard` are the widest) | Stop and ask rather than push on. |
| The decomposition is longer and no easier to read | Stated up front; T-D1 stop point. |
