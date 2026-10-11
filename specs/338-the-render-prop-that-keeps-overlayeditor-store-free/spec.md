# Spec 338 — the render-prop that keeps OverlayEditor store-free (#2847)

## Problem

`BackdropControls.tsx` and `FrameGrabber.tsx`, both under
`apps/shared/src/ui/composites/`, call `useListAllCameraChoicesQuery` and
`useGetStreamQuery` directly — both are RTK Query hooks, both require a
Redux `<Provider>` in the tree. ADR-0169 bans exactly this for any composite
in `apps/shared`: a shared, presentational component must render without
assuming a store exists, because it is consumed by more than one app and the
caller, not the composite, owns the store wiring.

Both composites are rendered only by `OverlayEditor.tsx`, itself a composite
under the same ban — so the query has to move up to the one real caller,
`apps/management-web/src/features/overlays/OverlayDraftForm.tsx`.

Part of #2801 (user decision 2026-10-10: widen ADR-0169's detection rather
than exempt these composites). Sibling: #2846 (`CameraViewer`). #2801 is
blocked on this issue and #2846.

## Decision

Full design settled in #2847's own issue body (quoted below) during a
dedicated architect phase 1-3 pass — this spec records it, not reopens it.

**Camera list**: `OverlayDraftForm` calls
`useListAllCameraChoicesQuery(undefined, { skip: !hasTextElement })` — the
`skip` matters, since the query previously only ever ran once `OverlayEditor`
mounted, which only happens when the draft has a Text element. The result
(`{ data, isLoading, isError }`) is passed `OverlayEditor` → `BackdropControls`
→ `CameraCaptureSection` as one prop, `cameras: CameraChoicesResult`.

**Capture stream**: `OverlayEditor` gains an optional render-prop,
`renderFrameGrabber`, called with `{ cameraIdentifier, onCaptured, onFailed }`
at the same place and under the same condition `FrameGrabber` used to be
rendered directly. `OverlayDraftForm` supplies a small wrapper component
(`OverlayDraftFormFrameGrabber`) that calls `useGetStreamQuery` and renders
the now-store-free `FrameGrabber`, which takes `stream: StreamHealth |
undefined` as a prop instead of calling the hook itself.

**`selectedCamera` and `useFrameCapture` stay in `OverlayEditor`** — rejected
alternatives (lifting them into `OverlayDraftForm`, or reporting
`activeCamera` upward through an effect) are both larger, riskier changes for
no behavioural gain; see the issue body for the full reasoning.

## Phase 4a — characterisation, with two declared red tests (ADR-0139/ADR-0144)

Behaviour-preserving everywhere except the ban itself, which is the point:

- **Call-site safety net** (`OverlayDraftForm.test.tsx`): the three existing
  frame-capture tests (failed capture, byte-identical payload submission,
  close-mid-capture) pass **unmodified** — they exercise the real wiring
  (camera select → capture → `FrameGrabber` → WHEP session) through the moved
  hooks and the new wrapper component end to end.
- **One new call-site test**, new behaviour, RED before the `skip` wiring
  existed: `Makes no camera-choices request for a shapes-only draft` —
  confirmed red by temporarily setting `skip: false` and observing the mock
  called with the wrong argument; green with `skip: !hasTextElement`.
- **Re-seamed composite tests** (`FrameCapture.test.tsx`,
  `OverlayEditorBackdrop.test.tsx`, `BackdropControlsCameraAmbiguity.test.tsx`):
  setup changed from mocking the RTK Query hooks to passing `cameras`/
  `renderFrameGrabber` props directly — zero `expect(` line changed
  (confirmed by diff-grep).
- **Unaffected suites** (`OverlayEditorCharacterisation.test.tsx`,
  `OverlayEditorKeyboard.test.tsx`, `OverlayEditorUndo.test.tsx`): zero diff,
  confirmed by `git diff --stat`.
- **Two new declared red tests** (`CameraCaptureNoProvider.test.tsx`):
  `BackdropControls` (with `getToken`) and `FrameGrabber` each render with no
  Redux `<Provider>` — confirmed red against the pre-fix components (via
  `git stash` of just the three implementation files), both throwing `could
  not find react-redux context value`; green after the fix.

## Out of scope

The decode/lag samplers, `useWhepSession`, any change to capture timing, and
the ESLint rule widening itself (#2801, lands last once this and #2846 are in).
