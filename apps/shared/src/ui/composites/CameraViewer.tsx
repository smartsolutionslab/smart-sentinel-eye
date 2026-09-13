import clsx from 'clsx';
import { useGetStreamQuery } from '@smart-sentinel-eye/shared/api/streams.api';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import { useCallback, useEffect, useRef } from 'react';
import {
  decodeElapsedBetween,
  decodeSampleFrom,
  missingDecodeFieldIn,
  reportKioskLatency,
  type DecodeSample,
} from '../../observability/kioskLatency.js';
import { logResilienceEvent } from '../../observability/resilienceLog.js';
import {
  bufferDelayBetween,
  lagBetween,
  lagSampleFrom,
  missingLagFieldIn,
  type LagSample,
} from '../../observability/wallAlignment.js';
import { useWhepSession } from './useWhepSession.js';
import type { CameraViewerStatus } from './useWhepSession.js';
import type { PlayoutTargetOutcome } from '../../streaming/WhepClient.js';
import { overlayLabelSurfaceStyle } from './overlayLabelStyle.js';

export type { CameraViewerStatus } from './useWhepSession.js';

// Spec 040: often enough to see an excursion, rare enough that the observer is
// nowhere near the budget it observes (FR-012).
const DECODE_SAMPLE_INTERVAL_MS = 5_000;

/**
 * Optional label drawn over the live video. Coordinates are normalized
 * to [0,1] so the overlay scales with the viewer regardless of viewport
 * size (spec 004 FR-005 / FR-013).
 */
export interface CameraViewerOverlay {
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
}

// Spec 045: faster than the decode sampler, because alignment is a control
// loop rather than an observation — a wall that takes half a minute to
// converge has not converged. Still far enough apart that the controller is
// nowhere near the 200 ms leg it manages.
const LAG_SAMPLE_INTERVAL_MS = 2_000;

export interface CameraViewerProps {
  cameraIdentifier: string;
  /** Resolves the bearer token for the current operator (Keycloak access token). */
  getToken: () => Promise<string | null>;
  /** Optional overlay rendered on top of the live frame (spec 004 US2). */
  overlay?: CameraViewerOverlay;
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
}

/**
 * Generic WebRTC viewer composite (spec 002 FR-016). Accepts a
 * cameraIdentifier and renders the live stream. Designed to be embedded
 * unchanged by spec 003 (Layout Composition) — no layout concerns leak in.
 */
export function CameraViewer({
  cameraIdentifier,
  getToken,
  overlay,
  playoutTargetMilliseconds,
  onLagMeasured,
  className,
}: CameraViewerProps) {
  const { data: stream, error: queryError } = useGetStreamQuery(cameraIdentifier, {
    pollingInterval: 5000,
  });
  const { videoRef, status, errorMessage, stats, setPlayoutTarget } = useWhepSession({
    cameraIdentifier,
    whepUrl: stream?.whepUrl,
    streamState: stream?.state,
    streamError: stream?.error ?? null,
    getToken,
  });

  // Spec 095 FR-001/FR-003: a read that fails names the counter it could not
  // read, once per counter for the life of this mounted tile.
  //
  // ONCE PER MOUNTED TILE, NOT ONCE PER CAMERA — and the tile is a grid
  // position. `CellPage` keys its tiles on `positionKey(row, col)`, so a layout
  // revision that puts a different camera at (0,0) leaves this instance mounted
  // and changes only the `cameraIdentifier` prop; neither ref resets, and the
  // second camera's equally unreadable instrument says nothing. That is the
  // intended scope, not an oversight: a missing `totalProcessingDelay` — like an
  // absent `jitterBufferTarget` below — is a property of the browser engine and
  // not of the camera, so one line per kiosk is the whole of the information.
  // Resetting per camera would multiply an engine-level fact by however many
  // cameras pass through the slot overnight. `cameraIdentifier` in the detail
  // therefore names the tile that noticed, and does not scope the claim — which
  // is why the callback's `[cameraIdentifier]` dependency, which reads as
  // per-camera, does not reset anything.
  //
  // ONE ref shared by both samplers, keyed by field name. They read the same
  // inbound video stat at different cadences, so a receiver omitting
  // `totalProcessingDelay` would otherwise say so twice — once from each — for
  // a single fact about the engine. A browser does not grow a statistics field
  // halfway through a session.
  //
  // A ref rather than state (a write here would re-render a wall of live video
  // to record something nobody displays — the reason `useWallAlignment` holds
  // lags in a ref), and at component scope rather than inside either effect: the
  // effects are keyed on `status`, so a tile flapping through the night would
  // report the same permanent fact on every recovery.
  const reportedMissingFieldsRef = useRef<Set<string>>(new Set());
  const reportMissingStatsField = useCallback(
    (field: string | null) => {
      if (field === null || reportedMissingFieldsRef.current.has(field)) return;
      reportedMissingFieldsRef.current.add(field);
      logResilienceEvent('stream', 'stats-field-missing', { cameraIdentifier, field });
    },
    [cameraIdentifier],
  );

  // Issue #2189: a sampler that throws says so, instead of discarding the throw and
  // every one after it.
  //
  // TWO COUNTERS, NOT ONE — the opposite of `reportedMissingFieldsRef` above, and for
  // the reason #2084 gives for its own two: "one counter would hide it". A missing
  // stats field is one fact about the browser engine that both samplers read, so it is
  // shared and keyed by field. A throw is not. The lag sampler runs the wall's own
  // `onLagMeasured` callback, which the decode sampler never touches, and the two
  // report different §IV legs — so a lag failure must not consume the decode sampler's
  // first line about an unrelated fault of its own.
  //
  // Same scope and same ref-not-state reasoning as the block above: both effects are
  // keyed on `status`, so a counter inside either one resets on every reconnect, and a
  // tile flapping through the night would report the same permanent fault hundreds of
  // times.
  //
  // ONE THING THE REASONING ABOVE DOES NOT TRANSFER: unlike a missing field, a throw
  // is a property of *this* camera's connection, not of the browser engine, so a
  // camera swap (a layout revision that puts a different camera at this tile) does
  // not reset either counter. A new camera whose sampler starts failing inherits
  // whatever decade its predecessor left behind, so its first report can be up to a
  // decade away — accepted, because resetting on `cameraIdentifier` reintroduces the
  // per-swap firehose these counters exist to avoid.
  const decodeSampleFailuresRef = useRef(0);
  const lagSampleFailuresRef = useRef(0);
  const reportSamplerFailure = useCallback(
    (counter: { current: number }, transition: 'decode-sampler-failed' | 'lag-sampler-failed', error: unknown) => {
      const count = countReportableFailure(counter);
      if (count === null) return;
      logResilienceEvent('stream', transition, { cameraIdentifier, count, reason: reasonFrom(error) });
    },
    [cameraIdentifier],
  );

  // Spec 040: the receive-to-decoded fragment of the SFU → kiosk decode leg.
  //
  // A FRAGMENT, not the leg — the budget spans SFU-sends → kiosk-decoded, and a
  // browser cannot see the sending end. A clock shared with the SFU now exists
  // (ADR-0128), but Chromium exposes no per-frame send-to-arrival mapping, so
  // the far end can only be estimated. The server-side segment carries
  // isWholeLeg: false so no dashboard reads this as the leg passing (ADR-0122).
  //
  // Deltas between reads, never the cumulative ratio: these are monotonic
  // counters over the session's life, so a raw ratio reports the session average
  // and flattens exactly the excursion a budget is about.
  useEffect(() => {
    if (status !== 'live') {
      return;
    }

    let previous: DecodeSample | null = null;
    const timer = window.setInterval(() => {
      void (async () => {
        const report = await stats();
        if (report === null) return;

        const current = decodeSampleFrom(report as unknown as Map<string, unknown>);
        if (current === null) {
          // The sample is still dropped — only the silence changes (FR-006).
          reportMissingStatsField(missingDecodeFieldIn(report as unknown as Map<string, unknown>));
          return;
        }

        if (previous !== null) {
          const elapsed = decodeElapsedBetween(previous, current);
          // Null rather than zero when no frames arrived: a zero would read as
          // a perfect score for a journey nobody timed.
          if (elapsed !== null) {
            reportKioskLatency('receive_to_decoded', cameraIdentifier, elapsed, getToken);
          }
        }
        previous = current;
      })().catch((error: unknown) => reportSamplerFailure(decodeSampleFailuresRef, 'decode-sampler-failed', error));
    }, DECODE_SAMPLE_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [status, stats, cameraIdentifier, getToken, reportMissingStatsField, reportSamplerFailure]);

  // Spec 045: this tile's lag, so the wall can align against it.
  //
  // Held behind a ref rather than named in the dependency array: callers pass
  // an inline closure, so a fresh identity each render would rebuild this
  // effect, clear the interval before it took a second sample, and reset the
  // previous sample every time. There would then be no delta ever, no lag ever
  // reported, and a wall that silently never aligns — the failure that already
  // happened once to the decode sampler (issue 1889).
  const onLagMeasuredRef = useRef(onLagMeasured);
  useEffect(() => {
    onLagMeasuredRef.current = onLagMeasured;
  });

  // Nothing is sampled when nobody asked for it. management-web mounts this
  // composite and passes no callback, so a single-camera page starts no
  // interval at all (FR-004).
  const sampleLag = onLagMeasured !== undefined;
  useEffect(() => {
    if (status !== 'live' || !sampleLag) {
      return;
    }

    let previous: LagSample | null = null;
    const timer = window.setInterval(() => {
      void (async () => {
        const report = await stats();
        if (report === null) return;

        const current = lagSampleFrom(report as unknown as Map<string, unknown>);
        if (current === null) {
          // The sample is still dropped — only the silence changes (FR-006).
          reportMissingStatsField(missingLagFieldIn(report as unknown as Map<string, unknown>));
          return;
        }

        if (previous !== null) {
          // Two figures from one pair of samples, and deliberately not the
          // same number.
          //
          // The controller needs the whole of what makes this tile late, so it
          // gets buffer + processing.
          const lag = lagBetween(previous, current);
          const buffered = bufferDelayBetween(previous, current);
          // Null rather than zero: no frames since the last sample, or a
          // session that restarted and reset its counters.
          //
          // Both figures go to the wall, because it needs both: the lag is
          // what has to be equalised, and the buffer is the part this leg's
          // budget bounds. Sending only the lag is what made the controller
          // release every tile on a real wall (T026).
          if (lag !== null && buffered !== null) {
            onLagMeasuredRef.current?.(cameraIdentifier, lag, buffered);
          }

          // The leg gets the buffer alone. Processing delay is already the
          // decode leg (reported above as `receive_to_decoded`), so reporting
          // the combined figure against the 200 ms presentation budget would
          // charge this leg for another's time.
          //
          // The ACHIEVED wait, never the target we asked for: jitterBufferTarget
          // is write-only in getStats, so the setpoint would report a perfect
          // score for something nobody measured (FR-007).
          if (buffered !== null) {
            reportKioskLatency('presentation_buffer', cameraIdentifier, buffered, getToken);
          }
        }
        previous = current;
      })().catch((error: unknown) => reportSamplerFailure(lagSampleFailuresRef, 'lag-sampler-failed', error));
    }, LAG_SAMPLE_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [status, stats, cameraIdentifier, sampleLag, getToken, reportMissingStatsField, reportSamplerFailure]);

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
    let outcome: PlayoutTargetOutcome = 'not-connected';
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

  return (
    <div className={clsx('relative aspect-video w-full overflow-hidden rounded-md bg-black', className)}>
      <video ref={videoRef} autoPlay playsInline muted className="h-full w-full object-contain" />
      {overlay !== undefined && <OverlayLabel overlay={overlay} />}
      {status !== 'live' && (
        <ViewerOverlay status={status} message={errorMessage} stream={stream} queryError={queryError} />
      )}
    </div>
  );
}

function ViewerOverlay({
  status,
  message,
  stream,
  queryError,
}: {
  status: CameraViewerStatus;
  message: string | null;
  stream: StreamHealth | undefined;
  queryError: unknown;
}) {
  const label = labelFor(status, stream);
  const tone =
    status === 'error' || status === 'offline'
      ? 'text-accent-fault'
      : status === 'reconnecting'
        ? 'text-accent-warning'
        : 'text-fg-muted';

  const hint = message ?? (queryError !== undefined ? 'Could not reach the streaming service.' : null);

  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-black/60 text-center text-sm">
      <span className={clsx('font-medium', tone)}>{label}</span>
      {hint !== null && <span className="px-4 text-xs text-fg-muted">{hint}</span>}
    </div>
  );
}

function OverlayLabel({ overlay }: { overlay: CameraViewerOverlay }) {
  return (
    <span
      data-testid="camera-viewer-overlay-label"
      style={{
        position: 'absolute',
        left: `${overlay.normalizedX * 100}%`,
        top: `${overlay.normalizedY * 100}%`,
        width: `${overlay.normalizedWidth * 100}%`,
        height: `${overlay.normalizedHeight * 100}%`,
        pointerEvents: 'none',
        ...overlayLabelSurfaceStyle(overlay),
      }}
    >
      {overlay.text}
    </span>
  );
}

/**
 * Counts a sampler failure and answers the running count when this one is worth a
 * line, or null.
 *
 * The decade cadence #2084 landed for the fab-less frame (`CellPage.tsx`
 * `countReportableSkew`), minus that case's fab predicate. A wall runs for weeks: a
 * line per tick evicts the first — the diagnostically valuable — occurrence from any
 * console buffer, and a line per session is emitted before anyone is looking.
 *
 * Duplicated rather than shared with CellPage deliberately; see spec 140
 * §"The cadence is #2084's decade curve". Extract it at the third site.
 */
function countReportableFailure(counter: { current: number }): number | null {
  counter.current += 1;
  const count = counter.current;
  let decade = 1;
  while (decade < count) decade *= 10;
  return decade === count ? count : null;
}

/** The message of a thrown value, or its string form when it is not an Error. */
function reasonFrom(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function labelFor(status: CameraViewerStatus, stream: StreamHealth | undefined): string {
  if (status === 'live') return 'Live';
  if (status === 'connecting') return 'Connecting…';
  if (status === 'reconnecting') return 'Reconnecting…';
  if (status === 'offline') return 'Stream is offline';
  if (status === 'error') return 'Viewer error';
  if (stream?.state === 'Provisioning') return 'Provisioning stream…';
  return 'Idle';
}
