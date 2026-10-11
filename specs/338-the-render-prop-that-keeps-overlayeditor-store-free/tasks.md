# Tasks — Spec 338 (#2847)

- [x] T001 `FrameGrabber.tsx`: drop `useGetStreamQuery`, take `stream` as a prop.
- [x] T002 `BackdropControls.tsx`: drop `useListAllCameraChoicesQuery`, take `cameras` as a prop; export `CameraChoicesResult`.
- [x] T003 `OverlayEditor.tsx`: add `cameras`/`renderFrameGrabber` props; drop the direct `FrameGrabber` render.
- [x] T004 `apps/shared/package.json`: add the `FrameGrabber` export entry.
- [x] T005 `OverlayDraftForm.tsx`: add the camera-choices query (with `skip`) and the `OverlayDraftFormFrameGrabber` wrapper; wire both into `OverlayEditor`.
- [x] T006 Re-seam `FrameCapture.test.tsx`, `OverlayEditorBackdrop.test.tsx`, `BackdropControlsCameraAmbiguity.test.tsx` — setup only, proven by diff-grep.
- [x] T007 New `CameraCaptureNoProvider.test.tsx` — two declared red tests, verified red against the pre-fix components.
- [x] T008 New call-site test in `OverlayDraftForm.test.tsx` for the `skip` wiring, verified red by breaking it.
- [x] T009 Confirm zero diff on `OverlayEditorCharacterisation.test.tsx`/`OverlayEditorKeyboard.test.tsx`/`OverlayEditorUndo.test.tsx`.
- [x] T010 Full `apps/shared` and `apps/management-web` suites green; workspace `tsc --noEmit` and `eslint` clean on every touched file.
