# Implementation Plan: `useBackdropCapture` returns three groups, not eleven values

**Branch**: `484-backdrop-capture-surface` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/484-backdrop-capture-surface/spec.md`

## Summary

`useBackdropCapture` returns `{ canvas, controls, grabber }` instead of eleven loose values, and `OverlayEditor.tsx` destructures those
three. **First probe, then pin, then move**: break each concern of the capture behaviour once, both inside the hook and at the editor
lines the change rewrites (the canvas background, `hasCapturedFrame`, the grabber key and its callbacks), see whether
`OverlayEditorCapture.test.tsx` and the `OverlayEditor` family notice, and write characterisation tests in **new files** for what they
do not. Then change the hook and its one consumer **in one commit**: the hook has a single importer and an inferred return type, so the
two halves cannot build apart. Behaviour-preserving, so **characterisation** (ADR-0139/0144 phase 4a). Same method as specs 476–483.

**Sizing, stated before building.** About neutral, perhaps a few lines up: the hook's `return` gains three group literals while the
editor loses a nine-line destructuring and the four hand renames (`setBackdrop`→`onBackdropChange` and three more). Reported as measured
(SC-004). The gain is that the mapping from a hook value to the consumer that needs it is written once, in the hook.

## Technical Context

- **Stack**: TypeScript, React 19.3, Vitest + Testing Library (jsdom). `apps/shared` only (`management-web` consumes `OverlayEditor`
  and is re-run at verify).
- **Baseline (2026-11-08, `develop` @ `e9b973a3`+)**: eleven returned values; `OverlayEditor.tsx` destructures all eleven; covering
  tests `OverlayEditorCapture.test.tsx` (six cases) and the `OverlayEditor*.test.tsx` family.
- **Touches**: `useBackdropCapture.ts`, `OverlayEditor.tsx`; new test files for gaps. No existing test file, no config, no
  `BackdropControls.tsx`.
- **Latency budget (§IV)**: **not on the path** (operator authoring canvas; only `management-web` imports it). Stated in the PR.

## Constitution Check

| Principle                                             | Verdict                                                                                                          |
| ----------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| §Testing — refactors stay green                       | Characterisation: existing suites unmodified, plus gap tests written first and seen green on today's code.       |
| §IV latency budget is sacred                          | Not touched. No kiosk, SFU, overlay-on-wall or render-leg code is in the diff.                                   |
| ADR-0036 — smallest change, no speculative generality | Three groups for the three consumers that exist; no memoising, no wrapper type, no change to `BackdropControls`. |
| ADR-0169 — the caller renders the frame grabber       | Unchanged: the editor still calls `renderFrameGrabber`; only where it reads its three arguments from moves.      |

## Decisions

**D1. Three groups, matched to the three consumers.**

```ts
return {
  canvas: { backdrop, capturedFrame },
  controls: {
    backdrop,
    onBackdropChange: setBackdrop,
    hasCapturedFrame: capturedFrame !== null,
    selectedCamera,
    onCameraChange: handleCameraChange,
    captureState,
    onCapture: handleCapture,
    onCancelCapture: cancel,
  },
  grabber: { activeCamera, onCaptured: handleCaptured, onFailed: fail },
};
```

`controls` is returned **already in `BackdropControls`' prop names**, so the editor writes
`<BackdropControls {...controls} getToken={…} cameras={cameras} />` and the four hand renames disappear. `backdrop` appears in both
`canvas` and `controls` because two consumers read it; both read the same state value in the same render, so they cannot disagree.
Alternatives: (a) two groups, the canvas reading `controls.backdrop`: `capturedFrame` is not in `controls` (only the derived
`hasCapturedFrame` is), so a third place would be needed anyway; (b) one object with the eleven values grouped by comment: moves the
problem, does not remove it; (c) split into two hooks (backdrop state, capture session): a larger change, and `handleCaptured`
writes both. Chosen: (a)-with-a-third, i.e. three groups.

**D2. No memoising.** The group objects are built per render, as the eleven values were read per render. Nothing downstream keys on
their identity: the editor reads fields in render and passes the stable `useCallback` handlers onward, and `Fragment key` takes the
string `activeCamera`. The plan does not rest on this: the probe runs the identity breaks (a handler that is a new function per
render) to learn what the suite notices, and a test pins what it must. If `BackdropControls` turns out to be `React.memo`'d
the decision would be revisited with evidence.

**D3. Types.** The return type stays inferred (as today, and unexported). `{...controls}` is checked by `tsc` against
`BackdropControlsProps`: a missing or mistyped prop fails the build. Extra props in a JSX spread are not an error, so the probe also
asserts the names match by test, not by the compiler alone.

**D4. One commit for the switch.** The hook and its single importer change together: a hook that returns groups with an editor still
reading eleven names does not build, and an additive "return both" step would add and then remove ten lines for no gain. Every commit
builds and passes (FR-006).

**D5. The `getToken` gating comment stays.** `OverlayEditor.tsx:380-388` explains why `getToken` is gated on `renderFrameGrabber` and
mentions "reshaping the props into one object (out of scope for this issue)". That is about `BackdropControls`' props, which this
change does not reshape; the comment keeps its place above `getToken`.

## Phases and commits

| Step    | Commit                                                                                                                                                                                                           | §IV legs |
| ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| T-probe | Break each concern in the hook and at the editor's lines (named, exactly-once substitutions, `probe484.cjs`); run `OverlayEditorCapture` and the `OverlayEditor*` family; record the matrix in `verification.md` | none     |
| T-gap   | Gap tests in **new** files for what nothing noticed, green on today's code, proved able to fail (message read)                                                                                                   | none     |
| 1       | `useBackdropCapture` returns the three groups; `OverlayEditor.tsx` destructures and spreads them                                                                                                                 | none     |
| 2       | Verification note, tick tasks, QA dispositions                                                                                                                                                                   | none     |

After the switch, the matching breaks are applied to the **new** code (the hook's handlers, the group literals) and the same tests must
fail there (counterfactual, SC-003). New untracked files are staged before counterfactuals.

## Risks and how they are covered

- **A rename slips in the groups** (`onCancelCapture` wired to the wrong handler; `hasCapturedFrame` inverted): `tsc` catches a
  missing prop but not a swapped handler of the same type; probed (`onCancelCapture` → no-op, `hasCapturedFrame` → constant).
- **The canvas paints the wrong frame** (the `canvas` group carrying `null` for `capturedFrame`): probed at the editor line.
- **The grabber key stops following the camera** (`Fragment key` constant): probed; the grabber would not be released on a change.
- **`cancel`/`fail` swapped:** both release the grabber, so a swap may be invisible; probed, and if unnoticed the equivalence is
  recorded or a distinguishing test written (a `failed` alert versus an `idle` state).
- **A handler loses its `useCallback`:** probed individually; the suite may not notice; a test through `BackdropControls` or the
  grabber's props would pin it only if something depends on the identity, which D2 says it does not (recorded as equivalent if so).

## Project Structure

```text
apps/shared/src/ui/composites/overlayEditor/useBackdropCapture.ts   (return value regrouped)
apps/shared/src/ui/composites/OverlayEditor.tsx                     (destructure + spread)
  new: gap tests beside OverlayEditorCapture.test.tsx, only where the probe finds an unnoticed break
```
