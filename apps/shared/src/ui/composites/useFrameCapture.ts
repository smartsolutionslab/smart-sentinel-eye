import { useCallback, useEffect, useRef, useState } from 'react';

export type CaptureState = 'idle' | 'capturing' | 'failed';

export interface FrameCaptureResult {
  state: CaptureState;
  /** The camera a capture is in flight against, or null (spec 147 FR-014). */
  activeCamera: string | null;
  /** Starts a capture against this camera. No-op while already capturing. */
  capture: (cameraIdentifier: string) => void;
  /** Tears down a capture in flight and returns to idle. Also the unmount path. */
  cancel: () => void;
  /** Tears down a capture in flight and reports it as failed (FR-016). */
  fail: () => void;
}

// FR-011: over three times spec 002 FR-013's 3 s p95 click-to-first-frame
// budget — generous enough that a slow but working camera succeeds, short
// enough that an unreachable one does not hold a session. Chosen, not
// measured.
const CAPTURE_TIMEOUT_MS = 10_000;

/**
 * Owns the bounded lifetime of a frame capture (spec 147): the state machine,
 * the camera a capture is against, and the 10 s outer bound. It does **not**
 * own a WHEP session — that lives in the `FrameGrabber` subtree this hook's
 * `activeCamera` gates, which is what makes teardown structural (an unmount)
 * rather than a hand-written release path repeated per exit (FR-010–FR-014).
 */
export function useFrameCapture(): FrameCaptureResult {
  const [state, setState] = useState<CaptureState>('idle');
  const [activeCamera, setActiveCamera] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const capture = useCallback(
    (cameraIdentifier: string) => {
      if (activeCamera !== null) return; // no-op while already capturing
      clearTimer();
      setActiveCamera(cameraIdentifier);
      setState('capturing');
      timerRef.current = setTimeout(() => {
        timerRef.current = null;
        setActiveCamera(null);
        setState('failed');
      }, CAPTURE_TIMEOUT_MS);
    },
    [activeCamera, clearTimer],
  );

  const cancel = useCallback(() => {
    clearTimer();
    setActiveCamera(null);
    setState('idle');
  }, [clearTimer]);

  const fail = useCallback(() => {
    clearTimer();
    setActiveCamera(null);
    setState('failed');
  }, [clearTimer]);

  // Unmount safety net: clears a pending timer if the owning component is
  // torn down mid-capture. Session teardown itself is FrameGrabber's own
  // unmount (structural, FR-013) — this only stops a timer from firing into
  // state nobody will read.
  useEffect(() => clearTimer, [clearTimer]);

  return { state, activeCamera, capture, cancel, fail };
}
