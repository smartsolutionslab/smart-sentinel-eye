import { useEffect, useRef } from 'react';
import type { CSSProperties } from 'react';
import { useGetStreamQuery } from '@smart-sentinel-eye/shared/api/streams.api';
import { logResilienceEvent } from '../../observability/resilienceLog.js';
import { useWhepSession } from './useWhepSession.js';

export interface FrameGrabberProps {
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  onCaptured: (dataUrl: string) => void;
  onFailed: () => void;
}

const DEFAULT_CAPTURE_WIDTH_PX = 800;
const DEFAULT_CAPTURE_HEIGHT_PX = 450;

// Present and decoding, never `display: none` (spec 147 plan R3): a video
// element layout has removed may not decode in every engine, which would
// make the capture hang on a browser-specific condition instead of
// producing a picture or failing visibly.
const HIDDEN_VIDEO_STYLE: CSSProperties = {
  position: 'absolute',
  width: 1,
  height: 1,
  opacity: 0,
  pointerEvents: 'none',
};

/**
 * Mounted only while a capture is in flight (spec 147). Owns exactly one WHEP
 * session against `cameraIdentifier` via the existing `useWhepSession` — drawing
 * a frame (`onCaptured`) or failing (`onFailed`) both end this component's
 * useful life, and the parent unmounts it in response either way. There is no
 * third, hand-written release path: FR-010 through FR-014 are one mechanism
 * (the unmount), not five.
 */
export function FrameGrabber({ cameraIdentifier, getToken, onCaptured, onFailed }: FrameGrabberProps) {
  // `currentData`, not `data` (spec 157 FR-002): behaviour-preserving here —
  // this component is mounted per capture and never re-propped, so its
  // `cameraIdentifier` cannot change on a mounted instance and the two
  // spellings are indistinguishable today. Closed anyway so a known instance
  // of the `data`-with-a-variable-argument shape does not stay live in the
  // same file family (spec § "The ESLint rule").
  const { currentData: stream } = useGetStreamQuery(cameraIdentifier);
  const { videoRef, status } = useWhepSession({
    cameraIdentifier,
    whepUrl: stream?.whepUrl,
    streamState: stream?.state,
    streamError: stream?.error ?? null,
    getToken,
  });

  // Guards against acting twice on the same session — e.g. if `onCaptured`/
  // `onFailed` identities change while `status` is still `live` between
  // renders and the parent has not yet unmounted this component in response.
  const settledRef = useRef(false);

  useEffect(() => {
    if (settledRef.current) return;

    if (status === 'live') {
      settledRef.current = true;
      const videoEl = videoRef.current;
      // Not drive-by error handling: this is a trust boundary around a
      // browser API (a `srcObject` `MediaStream` tainting the canvas — spec
      // 147's assumption 1), not a rethrow of a fault this code caused. The
      // alternative is an uncaught throw inside a render effect that would
      // take the whole dialog down; this routes into the same FR-016 message
      // a timeout produces, and swallows nothing silently — each exit's
      // cause goes to the `[resilience]` channel first (spec 234 FR-001).
      try {
        if (videoEl === null) {
          logResilienceEvent('stream', 'frame-capture-failed', {
            cameraIdentifier,
            error: 'video element unavailable',
          });
          onFailed();
          return;
        }
        const canvas = document.createElement('canvas');
        canvas.width = videoEl.videoWidth || DEFAULT_CAPTURE_WIDTH_PX;
        canvas.height = videoEl.videoHeight || DEFAULT_CAPTURE_HEIGHT_PX;
        const context = canvas.getContext('2d');
        if (context === null) {
          logResilienceEvent('stream', 'frame-capture-failed', {
            cameraIdentifier,
            error: '2d context unavailable',
          });
          onFailed();
          return;
        }
        context.drawImage(videoEl, 0, 0, canvas.width, canvas.height);
        onCaptured(canvas.toDataURL('image/png'));
      } catch (cause) {
        logResilienceEvent('stream', 'frame-capture-failed', { cameraIdentifier, error: String(cause) });
        onFailed();
      }
      return;
    }

    // FR-016, fast fail: a capture has no use for `useWhepSession`'s retry
    // ladder — that machinery is for a wall tile that must eventually recover
    // on its own. The first `reconnecting` (a refused/failed connect, a
    // stalled ICE transport, or the media watchdog finding no frames) already
    // means this attempt did not work, so this unmounts on it rather than
    // riding the ladder or waiting out the outer 10 s timeout. `'error'` is
    // not reachable — `useWhepSession`'s only transitions are `offline`,
    // `reconnecting`, `live` and `connecting` — so that arm is dropped rather
    // than kept as a false sense of coverage.
    if (status === 'reconnecting' || status === 'offline') {
      settledRef.current = true;
      onFailed();
    }
  }, [status, videoRef, onCaptured, onFailed, cameraIdentifier]);

  return <video ref={videoRef} autoPlay playsInline muted aria-hidden="true" style={HIDDEN_VIDEO_STYLE} />;
}
