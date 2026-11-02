import clsx from 'clsx';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import { useEffect, useRef } from 'react';
import { logResilienceEvent } from '../../observability/resilienceLog.js';
import { useSamplerReporting } from './cameraViewer/useSamplerReporting.js';
import { useDecodeSampler } from './cameraViewer/useDecodeSampler.js';
import { useLagSampler } from './cameraViewer/useLagSampler.js';
import { announcementFor, statusInfoFor } from './cameraViewer/viewerStatus.js';
import { ViewerOverlay } from './cameraViewer/ViewerOverlay.js';
import { OverlayLabel, OverlayShape } from './cameraViewer/overlayElements.js';
import type { CameraViewerOverlay } from './cameraViewer/overlayTypes.js';
import { useWhepSession } from './useWhepSession.js';
import type { PlayoutTargetOutcome } from '../../streaming/WhepClient.js';

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
   * Absent means no sampling happens at all — the interval below never starts.
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

  // Spec 095 FR-004: an engine that cannot hold a playout target says so once.
  //
  // At component scope for the same reason as the field ref above — this effect
  // is keyed on `status` too, so a flapping tile would otherwise report on every
  // recovery. And on the same scope for the same reason: once per mounted tile,
  // because an engine that carries no `jitterBufferTarget` carries none for any
  // camera the wall later puts in this slot.
  const reportedNoPlayoutRef = useRef(false);

  // Apply the wall's decision. Undefined and null both mean "leave this tile
  // alone", which is what a single-camera page and an unconverged wall both
  // want — and neither is the same as a target of zero.
  useEffect(() => {
    if (status !== 'live' || playoutTargetMilliseconds === undefined || playoutTargetMilliseconds === null) {
      return;
    }
    // Wrapped, not merely ignored. `setPlayoutTarget` guards the assignment
    // itself, but the call can still throw before it gets there — an engine
    // without `getReceivers`, a torn-down connection — and an exception here
    // would take the render effect with it. A tile that cannot be aligned must
    // carry on showing video (FR-013). Mapped to 'refused', one frame out: the
    // engine refused, and it is still reported below, exactly as today's
    // `applied = false` was.
    let outcome: PlayoutTargetOutcome;
    try {
      outcome = setPlayoutTarget(playoutTargetMilliseconds);
    } catch {
      outcome = 'refused';
    }

    // The answer is no longer collapsed into one boolean (#2198 item 2, spec
    // 142). `setPlayoutTarget` now answers which of four things happened, and
    // this effect reports two of them: 'unsupported' (no video receiver ever
    // carries `jitterBufferTarget` — Firefox, Safari, pre-115 Chromium) and
    // 'refused' (a receiver carries it and every assignment threw). Both are
    // permanent facts about this browser engine, and mean the same thing to an
    // operator reading the line: this tile will not align.
    //
    // 'not-connected' is no longer reported. It is the transient every tile
    // passes through on its way up — no peer connection yet, or one with no
    // video receiver attached yet — and treating it the same as the permanent
    // causes was spec 095's own recorded residual: a null `clientRef.current`
    // (or a session mid-connect) answered the same falsy value as an engine
    // that will never carry the property, so a tile could latch this line on
    // its first render regardless of health. The `status === 'live'` guard
    // above already screens out most of that window; closing the residual here
    // makes it a second line of defence rather than the only one.
    if ((outcome === 'unsupported' || outcome === 'refused') && !reportedNoPlayoutRef.current) {
      reportedNoPlayoutRef.current = true;
      logResilienceEvent('stream', 'playout-target-unsupported', { cameraIdentifier });
    }
  }, [status, playoutTargetMilliseconds, setPlayoutTarget, cameraIdentifier]);

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
