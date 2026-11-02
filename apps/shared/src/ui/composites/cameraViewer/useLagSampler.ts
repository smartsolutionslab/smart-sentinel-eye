import { useEffect, useRef } from 'react';
import { reportKioskLatency } from '../../../observability/kioskLatency.js';
import {
  bufferDelayBetween,
  lagBetween,
  lagSampleFrom,
  missingLagFieldIn,
  type LagSample,
} from '../../../observability/wallAlignment.js';
import type { CameraViewerStatus, WhepSessionResult } from '../useWhepSession.js';
import type { SamplerReporting } from './useSamplerReporting.js';

// Spec 045: faster than the decode sampler, because alignment is a control
// loop rather than an observation — a wall that takes half a minute to
// converge has not converged. Still far enough apart that the controller is
// nowhere near the 200 ms leg it manages.
const LAG_SAMPLE_INTERVAL_MS = 2_000;

interface LagSamplerInput {
  status: CameraViewerStatus;
  stats: WhepSessionResult['stats'];
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  onLagMeasured: ((cameraIdentifier: string, lagMilliseconds: number, bufferMilliseconds: number) => void) | undefined;
  reporting: SamplerReporting;
}

export function useLagSampler({
  status,
  stats,
  cameraIdentifier,
  getToken,
  onLagMeasured,
  reporting,
}: LagSamplerInput) {
  const { reportMissingStatsField, reportSamplerFailure, lagSampleFailuresRef, reportThrottleRef } = reporting;
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
          // decode leg (reported by `useDecodeSampler` as `receive_to_decoded`), so reporting
          // the combined figure against the 200 ms presentation budget would
          // charge this leg for another's time.
          //
          // The ACHIEVED wait, never the target we asked for: jitterBufferTarget
          // is write-only in getStats, so the setpoint would report a perfect
          // score for something nobody measured (FR-007).
          if (buffered !== null) {
            reportKioskLatency('presentation_buffer', cameraIdentifier, buffered, getToken, reportThrottleRef.current);
          }
        }
        previous = current;
      })().catch((error: unknown) => {
        // A tick that threw never reached `previous = current`, so the window
        // it opened is still open. Left alone, a callback (or a `getStats`)
        // that keeps throwing pins `previous` for the whole outage, and the
        // first tick that succeeds afterwards reports a per-frame mean over
        // all of it — the cumulative session average `lagBetween` exists to
        // avoid (#2314). Side effect: a seeding tick can never throw (there is
        // no `previous` yet to compare against), so under a persistent throw
        // this halves how often the callback actually fires versus before —
        // #2189's decade-cadence failure count now advances roughly every
        // other tick, not every tick.
        previous = null;
        reportSamplerFailure(lagSampleFailuresRef, 'lag-sampler-failed', error);
      });
    }, LAG_SAMPLE_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [
    status,
    stats,
    cameraIdentifier,
    sampleLag,
    getToken,
    reportMissingStatsField,
    reportSamplerFailure,
    lagSampleFailuresRef,
    reportThrottleRef,
  ]);
}
