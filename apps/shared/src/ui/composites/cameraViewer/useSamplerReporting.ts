import { useCallback, useRef } from 'react';
import { createReportThrottle, reportKioskLatency, type ReportThrottle } from '../../../observability/kioskLatency.js';
import { logResilienceEvent } from '../../../observability/resilienceLog.js';
import { countReportableFailure, reasonFrom } from './samplerFailure.js';

export interface SamplerReporting {
  reportMissingStatsField: (field: string | null) => void;
  reportSamplerFailure: (
    counter: { current: number },
    transition: 'decode-sampler-failed' | 'lag-sampler-failed',
    error: unknown,
  ) => void;
  decodeSampleFailuresRef: { current: number };
  lagSampleFailuresRef: { current: number };
  reportThrottleRef: { current: ReportThrottle | undefined };
}

/**
 * The per-tile reporting state both samplers share: the missing-field ref, the two failure counters and the throttle,
 * each created once for the mounted tile and handed to both samplers (never one per sampler, never reset on a camera swap).
 */
export function useSamplerReporting(
  cameraIdentifier: string,
  getToken: () => Promise<string | null>,
): SamplerReporting {
  // Spec 095 FR-001/FR-003: a read that fails names the counter it could not
  // read, once per counter for the life of this mounted tile.
  //
  // ONCE PER MOUNTED TILE, NOT ONCE PER CAMERA — and the tile is a grid
  // position. `CellPage` keys its tiles on the origin `row:col` key
  // `buildGridItems` assigns (`wallGrid.ts`), so a layout revision that puts
  // a different camera at (0,0) leaves this instance mounted
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

  // Spec 307 (#2563). One throttle per mounted tile, created once and never
  // recreated — a reconnect rebuilds the WHEP session but must not reopen
  // the send window, so this lives at component scope rather than inside
  // either sampler effect below. Shared by both tile legs deliberately:
  // `createReportThrottle` keys its decision by `KioskMeasurement` name, so
  // `receive_to_decoded` and `presentation_buffer` already get independent
  // windows from this one instance.
  const reportThrottleRef = useRef<ReportThrottle | undefined>(undefined);
  if (reportThrottleRef.current === undefined) {
    reportThrottleRef.current = createReportThrottle();
  }
  const reportSamplerFailure = useCallback(
    (counter: { current: number }, transition: 'decode-sampler-failed' | 'lag-sampler-failed', error: unknown) => {
      const count = countReportableFailure(counter);
      if (count === null) return;
      logResilienceEvent('stream', transition, { cameraIdentifier, count, reason: reasonFrom(error) });
      // ADR-0180: counted at the sink as well, at the same reportable counts. The
      // shared per-tile throttle still applies, so a count inside its window is
      // dropped here but not from the console line above.
      reportKioskLatency('sampler_failed', cameraIdentifier, count, getToken, reportThrottleRef.current);
    },
    [cameraIdentifier, getToken],
  );

  return {
    reportMissingStatsField,
    reportSamplerFailure,
    decodeSampleFailuresRef,
    lagSampleFailuresRef,
    reportThrottleRef,
  };
}
