import clsx from 'clsx';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import { useSamplerReporting } from './cameraViewer/useSamplerReporting.js';
import { useDecodeSampler } from './cameraViewer/useDecodeSampler.js';
import { useLagSampler } from './cameraViewer/useLagSampler.js';
import { usePlayoutTarget } from './cameraViewer/usePlayoutTarget.js';
import { announcementFor, statusInfoFor } from './cameraViewer/viewerStatus.js';
import { ViewerOverlay } from './cameraViewer/ViewerOverlay.js';
import { OverlayLabel, OverlayShape } from './cameraViewer/overlayElements.js';
import type { CameraViewerOverlay } from './cameraViewer/overlayTypes.js';
import { useWhepSession } from './useWhepSession.js';

export type { CameraViewerStatus } from './useWhepSession.js';
export type {
  CameraViewerOverlay,
  CameraViewerOverlayLabel,
  CameraViewerOverlayShape,
} from './cameraViewer/overlayTypes.js';
export { OverlayLabel, OverlayShape } from './cameraViewer/overlayElements.js';

export interface CameraViewerProps {
  cameraIdentifier: string;
  /** Resolves the bearer token for the current operator (Keycloak access token). */
  getToken: () => Promise<string | null>;
  /** Told once when the stream server terminally refuses this viewer (401 or 403). */
  onRefused?: (kind: 'unauthorized' | 'forbidden') => void;
  /** Told when the stream reaches live. */
  onLive?: () => void;
  /** Told when a refusal reported through `onRefused` ends without going live. */
  onRefusalLifted?: () => void;
  /**
   * The current camera's stream health, read fresh for THIS
   * `cameraIdentifier` — never a previous camera's answer (spec 157 FR-001).
   * Callers get this from `useCameraStream` (`@smart-sentinel-eye/shared/hooks`),
   * which reads `currentData`, not `data`, from the underlying RTK Query hook
   * (ADR-0169, #2846: this composite does not call the hook itself).
   */
  stream: StreamHealth | undefined;
  /** The stream-health query's own error, read alongside `stream` from the same call. */
  queryError?: unknown;
  /**
   * Labels rendered on top of the live frame (spec 004 US2; spec 150 widens
   * this from one label to an ordered set, #2345). Absent or empty paints
   * nothing (FR-013) — every label is painted in array order, each a direct
   * child of this component's aspect-ratio box (FR-012).
   */
  overlays?: readonly CameraViewerOverlay[];
  /**
   * How far behind live this tile should hold frames, in milliseconds — the
   * wall's decision, applied here (spec 045, ADR-0128).
   *
   * <p>
   * <b>Optional, and absent means untouched.</b> `management-web` shows one
   * camera at a time with nothing to align it against, so it passes nothing and
   * the browser's own buffering is left exactly as it is.
   * </p>
   */
  playoutTargetMilliseconds?: number | null;
  /**
   * Reports this tile's measured lag so a wall can align against it
   * (spec 045 FR-007).
   *
   * <p>
   * <b>The achieved figure, never the setpoint.</b> `jitterBufferTarget` is
   * write-only, so what was asked for and what happened are different numbers,
   * and only this one is a measurement.
   * </p>
   *
   * <p>
   * Absent means no sampling happens at all — the lag interval (`useLagSampler`) never starts.
   * A single-camera page pays nothing for a feature about walls (FR-004).
   * </p>
   */
  onLagMeasured?: (cameraIdentifier: string, lagMilliseconds: number, bufferMilliseconds: number) => void;
  className?: string;
  /**
   * Names the video for assistive technology (spec 228 US2, FR-007). Absent
   * on the kiosk's `CellPage`, which holds no camera name for its tiles; the
   * `<video>` then gets a generic name rather than none at all.
   */
  cameraName?: string;
}

/**
 * Generic WebRTC viewer composite (spec 002 FR-016). Accepts a
 * cameraIdentifier and renders the live stream. Designed to be embedded
 * unchanged by spec 003 (Layout Composition) — no layout concerns leak in.
 */
export function CameraViewer({
  cameraIdentifier,
  getToken,
  onRefused,
  onLive,
  onRefusalLifted,
  stream,
  queryError,
  overlays,
  playoutTargetMilliseconds,
  onLagMeasured,
  className,
  cameraName,
}: CameraViewerProps) {
  const { videoRef, status, errorMessage, stats, setPlayoutTarget } = useWhepSession({
    cameraIdentifier,
    whepUrl: stream?.whepUrl,
    streamState: stream?.state,
    streamError: stream?.error ?? null,
    getToken,
    onRefused,
    onLive,
    onRefusalLifted,
  });

  const reporting = useSamplerReporting(cameraIdentifier, getToken);

  useDecodeSampler({ status, stats, cameraIdentifier, getToken, reporting });

  useLagSampler({ status, stats, cameraIdentifier, getToken, onLagMeasured, reporting });

  usePlayoutTarget({ status, playoutTargetMilliseconds, setPlayoutTarget, cameraIdentifier });

  // Computed once per render and shared by the status region and
  // `ViewerOverlay` below — the same inputs otherwise produced the same
  // result twice on every render of every non-live tile (5s stream poll,
  // decode/lag sampler effects — across up to 250 wall tiles).
  const { failedRead, label } = statusInfoFor(status, stream, queryError);

  return (
    <div className={clsx('relative aspect-video w-full overflow-hidden rounded-md bg-bg-video @container', className)}>
      <video
        ref={videoRef}
        autoPlay
        playsInline
        muted
        className="h-full w-full object-contain"
        aria-label={!cameraName ? 'Live camera video' : `Live video: ${cameraName}`}
      />
      {overlays?.map((overlay, ordinal) =>
        // `ordinal` — the element's position in the ordered set, not its
        // content (FR-004 permits duplicate text across labels) — is the
        // key. Both kinds stay direct children of this aspect-ratio box,
        // with no wrapper (spec 150 FR-012, guarded by
        // OverlayLabelNodeCountCharacterisation).
        overlay.kind === 'Text' ? (
          <OverlayLabel key={ordinal} overlay={overlay} />
        ) : (
          <OverlayShape key={ordinal} overlay={overlay} />
        ),
      )}
      {/* Always mounted, never inserted with its content (#2346): a live
          region a screen reader has not yet seen does not announce the text
          it is born holding, so this has to exist — empty — before the first
          state change, and go on existing across every later one. Text
          mirrors what `ViewerOverlay` paints, so the two cannot drift
          (spec 228 US2, FR-004/FR-005). */}
      <p role="status" data-testid="camera-viewer-status" className="sr-only">
        {announcementFor(status, errorMessage, queryError, failedRead, label)}
      </p>
      {status !== 'live' && (
        <ViewerOverlay
          status={status}
          message={errorMessage}
          queryError={queryError}
          failedRead={failedRead}
          label={label}
        />
      )}
    </div>
  );
}
