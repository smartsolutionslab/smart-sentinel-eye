import { useCallback, useState } from 'react';
import type { Backdrop } from '../BackdropControls';
import { useFrameCapture } from '../useFrameCapture';

export function useBackdropCapture(initialCamera: string) {
  // Neither of these is lifted into `OverlayLabel` — `onChange` fires only for
  // text, font size and geometry, exactly as today (FR-005). The preview
  // camera is authoring-session state, never persisted (spec.md §Which camera).
  const [backdrop, setBackdrop] = useState<Backdrop>('checkerboard');
  const [capturedFrame, setCapturedFrame] = useState<string | null>(null);
  const [selectedCamera, setSelectedCamera] = useState(initialCamera);
  const { state: captureState, activeCamera, capture, cancel, fail } = useFrameCapture();

  const handleCameraChange = useCallback(
    (next: string) => {
      setSelectedCamera(next);
      // FR-012: changing the camera mid-capture closes the old session; no
      // frame from it is ever applied. Called unconditionally — `cancel()` is
      // idempotent against `idle`, and this is also how picking a different
      // camera clears a stale `failed` alert instead of leaving the operator
      // staring at "try again, or pick a different camera" after doing
      // exactly that.
      cancel();
    },
    [cancel],
  );

  const handleCapture = useCallback(() => {
    if (selectedCamera === '') return;
    capture(selectedCamera);
  }, [selectedCamera, capture]);

  const handleCaptured = useCallback(
    (dataUrl: string) => {
      setCapturedFrame(dataUrl);
      setBackdrop('captured');
      cancel();
    },
    [cancel],
  );

  // One group per consumer. `controls` is already in `BackdropControls`' prop names, so the editor spreads it; `backdrop` is in
  // both `canvas` and `controls` because both read it, from the same state in the same render.
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
}
