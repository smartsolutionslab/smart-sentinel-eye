# Verification 478 — `OverlayEditor` is broken up by responsibility

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). Not observed in a browser; the evidence is the
covering family under jsdom plus the five new gap files.

## Size, as measured (it is a decomposition: the total rises)

|                                    | Before    | After                   |
| ---------------------------------- | --------- | ----------------------- |
| `OverlayEditor.tsx`                | 911       | **410**                 |
| the component                      | 576       | **290** (−50 %)         |
| hook calls in the component        | 34 (file) | **12**                  |
| **File total (editor + 12 units)** | 911       | **1,056** (+145, +16 %) |

Units (`overlayEditor/`, total / code lines): `overlayGeometry` 103 / 36, `useCanvasGestures` 100 / 80,
`useLabelKeyboard` 85 / 67, `useUndoRedoControls` 62 / 36, `useBackdropCapture` 55 / 41, `canvasStyle` 55 / 24,
`useGeometryAnnouncer` 43 / 29, `LabelFields` 43 / 41, `overlayAnnouncement` 35 / 22, `InertElement` 33 / 32,
`useEmitNormalized` 32 / 24. Over the issue's 50-code-line bar: `useCanvasGestures` (80, four `Rnd` handlers whose
signatures are most of it) and `useLabelKeyboard` (67, one 60-line key handler moved whole). ADR-0084's limit is not
enforced, so no failing gate is being fixed.

**The measure-and-ask (T009) came out worse than the earlier specs.** After the first four extractions the component had
only gone 576 → 543 lines (−6 %), file total +3 %: they moved module-level code (arithmetic, text, style), not the
component. The maintainer was asked and chose to continue; the component's real reduction came from the hooks.

**Not extracted:** the typed-entry commits (`handleGeometryCommit`, `handleColorCommit`) stay in the editor on purpose —
two ten-line callbacks whose deliberate bypass of the clamps reads clearer next to the `useEmitNormalized` seam they avoid.
The canvas/`Rnd` JSX also stays (it takes the component's whole state). The derived values (`shape`, `label`,
`previewText`, pixel box, accessible name) and the focus flag stay.

## Characterisation (FR-001)

- 8 covering files: **137 test names with status identical** before and after (the only `diff` line is a missing trailing
  newline in the "before" file), **308 assertion lines identical**; `git diff origin/develop...HEAD` on them is empty. The
  only test files added are `OverlayEditor{Announcer,Capture,Fields,Gestures,UndoKeys}.test.tsx` (38 tests).
- `shared` suite: **1,346 passed** (85 files), 75 s; `management-web` `features/overlays` (the caller): 197 passed;
  `eslint --max-warnings 0`, `tsc --noEmit` (both apps), `prettier --check` clean; `Architecture.Tests` **3254 / 3254**.
  (No whole-suite "before" figure was recorded for `shared`; the family counts above are the before/after evidence.)
- `eslint-disable`: none in the editor or `overlayEditor/` (SC-003), as before.

## Probing before moving: 38 of 84 deliberate breaks went unnoticed by the family

Each break was one named, exactly-once substitution into the unsplit editor (`probe478.cjs`), the covering family run, the
file restored from git; matrix: 46 noticed, **38 not** (45 %). The 38: the NaN guard in `clamp01`, `quantize`, the
zero-delta branch of `nudgePosition`; the y-axis and height edge names, the "Top" and "Height" labels, the arrow run key;
the live drag/resize readout (set, clamped, cleared on stop), the typed-entry commit leaving the other three values
untouched, and the undo step boundaries of typed entry; the debounced announcer's repeat toggle, restart-on-new-message,
cleanup on unmount and last-announced memory; Meta held with an arrow, the arrow-only keyup `endRun`, Ctrl+Y vs Cmd+Y,
`preventDefault` on undo; camera change cancelling a capture, the empty-camera guard, a delivered frame releasing the
grabber, `initialCamera`, the grabber's `key` and `getToken` gate; `maxLength`, the font-size range, the font-size
pointer-up `endRun`, `bounds="parent"`, the minimum pixel box, the resolved preview text and error forwarding.

**38 tests in five new files** pin 35 of them. Each was green on the unsplit editor and proved able to fail with the message
read. Examples: A1 `expected { text: 'Furnace', …(5) } to match object { normalizedX: +0, normalizedY: 0.1 }`; A4 `expected
0.10500000000000001 to be 0.105`; B2 `expected 'Top 0%, at the bottom edge' to be 'Top 0%, at the top edge'`; G4 `expected 1 to
be +0` (a timer pending after unmount); H7 `expected 0.26 to be 0.25`; J5 `expected '' to be 'cam-b'`; K15 `Unable to find
an element by: [data-testid="placeholder-preview-error"]`.

After each extraction the matching break was applied to the **new unit** and the same test failed there (geometry 10/10,
announcement 9/9, canvas style and inert element 4/4, announcer 5/5, undo 8/8, emit 2/2, gestures 6/6, keyboard 9/9, label
fields 7/7, backdrop capture 4/5 with J2 below).

### Equivalent (no observable behaviour), recorded with the reason

| Break                               | Why nothing can notice it                                                                                                                                                                           |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| B9 run key without the mode segment | the axis is already unique per mode: `x`/`y` only occur when moving, `width`/`height` only when resizing                                                                                            |
| A11 `delta <= 0` → `delta < 0`      | `delta` is ±`FINE_STEP` or ±`COARSE_STEP`, both non-zero constants, so `delta === 0` cannot happen                                                                                                  |
| J7 `Fragment` without `key`         | `activeCamera` can only change camera by passing through `null` (`capture()` is a no-op while active; `cancel`, `fail` and the timeout all null it), so the grabber always unmounts between cameras |

### Defended jointly

| Break                                                                               | Result                                                                                                                         |
| ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| J2 `capture()` with an empty camera, alone                                          | green: `BackdropControls`' button is natively `disabled` while `selectedCamera === ''`, so the editor's guard is never reached |
| J2 **and** `BackdropControls`' `disabled` reduced to `captureState === 'capturing'` | "Starts no capture and mounts no grabber while no camera is chosen" fails (`expected "vi.fn()" to not be called at all`)       |

K12 (`bounds="parent"`) and K13 (the minimum pixel box) are pinned through the stubbed `react-rnd` props (`bounds`,
`size`), the contract with the library; jsdom has no layout, so nothing else could show them. K6 is pinned through the
slider's `onPointerUp` handler, J8 through a prop transition (`getToken` removed mid-capture) because in normal use the
capture control is not offered without it.

## Slips and judgements, kept visible

- **A foreign edit appeared in the working tree** (`src/ScenarioSimulator/CameraCatalog/CameraCatalogClient.cs`, a header
  initialiser refactor) while the probes ran. It is not from this work (probably the IDE or another session); it was never
  staged and is not in any commit of this branch.
- Several scripted extractions failed their first run on a comment that wrapped differently than I had quoted it, or on a
  `sed` quoting error; none reached a commit. A new, untracked unit cannot be restored by `git checkout`, so the units were
  staged before their counterfactuals.
- The comment that said `commit` replaces "every direct `onChange` call below" was rewritten to name the units (comment-only;
  the family and the 38 new tests were re-run).

## QA

Recorded in the PR (Phase 6, `/code-review medium`).

## QA (`/code-review medium`): 5 findings, 1 fixed, no behavioural regression

The reviewer compared the pre-refactor file with the units and found the clamps, run keys, announcement debounce, focus
ring, `endRun` calls, capture handlers and DOM order unchanged.

Fixed: `useUndoRedoControls`' moved comment said "twenty lines above its own fix" and pointed at `queueAnnouncement` as if it
sat in the same file; it now names `useGeometryAnnouncer` (comment-only; family and gap tests re-run, 172 passed).

Accepted, in writing:

- **The unrelated `CameraCatalogClient.cs` edit in the working tree.** Not from this work and never staged; no commit of
  this branch contains it (`git diff origin/develop...HEAD --stat` lists only `apps/shared` and `specs/478-…`).
- **`LabelFields` gets an inline `onChange` closure with `next as T`.** The cast is the original `as T`, moved to the one
  place where `T` is in scope; the payload is type-checked as `OverlayLabel` inside `LabelFields`. A generic `LabelFields` or
  a memoised callback is a change of shape, not of structure.
- **`useLabelKeyboard` takes `value: OverlayEditorValue`.** It reads only the four normalised fields and hands the result to
  `emitNormalized`, which spreads the editor's own `value`; no unit builds a payload from a different value. Tightening it to
  `T` would not change what is emitted.
- **`useBackdropCapture` returns eleven values.** It is the old block moved whole; narrowing its interface (wrapping
  `cancel`/`fail`, pairing `activeCamera` with the grabber) is a redesign for its own issue, and ADR-0084's limit is advisory.
