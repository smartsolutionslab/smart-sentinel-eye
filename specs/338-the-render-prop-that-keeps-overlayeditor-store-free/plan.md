# Plan — Spec 338 (#2847)

Phase 2 of ADR-0037. The design is already settled — see spec.md's Decision
section and #2847's own issue body (a dedicated architect phase 1-3 pass).
This plan records the file-level shape; it does not relitigate the design.

## Files touched

| File | Change |
|---|---|
| `apps/shared/src/ui/composites/FrameGrabber.tsx` | Drops `useGetStreamQuery`; takes `stream: StreamHealth \| undefined` as a prop. |
| `apps/shared/src/ui/composites/BackdropControls.tsx` | Drops `useListAllCameraChoicesQuery`; `CameraCaptureSection` takes `cameras: CameraChoicesResult` as a prop. New exported `CameraChoicesResult` type. |
| `apps/shared/src/ui/composites/OverlayEditor.tsx` | Gains `cameras` and `renderFrameGrabber` props; drops the direct `FrameGrabber` import/render in favour of calling `renderFrameGrabber`. |
| `apps/shared/package.json` | New `./ui/composites/FrameGrabber` export entry (management-web needs to import it for its wrapper). |
| `apps/management-web/src/features/overlays/OverlayDraftForm.tsx` | New `useListAllCameraChoicesQuery(undefined, { skip: !hasTextElement })` call; new module-scope `OverlayDraftFormFrameGrabber` wrapper (calls `useGetStreamQuery`, renders `FrameGrabber`); wires both into `<OverlayEditor>`. |

## Tests touched

| File | Kind of change |
|---|---|
| `apps/shared/src/ui/composites/FrameCapture.test.tsx` | Re-seamed: setup only (helper `renderEditor()` replaces 19 identical inline renders), zero assertion change. |
| `apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx` | Re-seamed: setup only, zero assertion change. |
| `apps/shared/src/ui/composites/BackdropControlsCameraAmbiguity.test.tsx` | Re-seamed: setup only, zero assertion change. |
| `apps/shared/src/ui/composites/CameraCaptureNoProvider.test.tsx` | New. Two declared red tests (ADR-0139), verified red against the pre-fix components via `git stash`. |
| `apps/management-web/src/features/overlays/OverlayDraftForm.test.tsx` | One new test (the `skip` wiring), verified red by temporarily breaking `skip`. Three existing frame-capture tests pass unmodified — the call-site safety net. |

## Not touched

`OverlayEditorCharacterisation.test.tsx`, `OverlayEditorKeyboard.test.tsx`,
`OverlayEditorUndo.test.tsx` — zero diff, confirmed by `git diff --stat`.
`apps/management-cameras`, `apps/kiosk-web` — neither renders `OverlayEditor`.
#2846 (`CameraViewer`) and #2801 (the ESLint rule) are siblings; neither
issue's files are touched here.
