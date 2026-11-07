import { useEffect } from 'react';
import {
  decodeElapsedBetween,
  decodeSampleFrom,
  missingDecodeFieldIn,
  reportKioskLatency,
  type DecodeSample,
} from '../../../observability/kioskLatency.js';
import type { CameraViewerStatus, WhepSessionResult } from '../useWhepSession.js';
import type { SamplerReporting } from './useSamplerReporting.js';

// Spec 040: often enough to see an excursion, rare enough that the observer is
// nowhere near the budget it observes (FR-012).
const DECODE_SAMPLE_INTERVAL_MS = 5_000;

interface DecodeSamplerInput {
  status: CameraViewerStatus;
  stats: WhepSessionResult['stats'];
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  reporting: SamplerReporting;
}

export function useDecodeSampler({ status, stats, cameraIdentifier, getToken, reporting }: DecodeSamplerInput) {
  const { reportMissingStatsField, reportDecodeFailure, reportThrottleRef } = reporting;
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
            reportKioskLatency('receive_to_decoded', cameraIdentifier, elapsed, getToken, reportThrottleRef.current);
          }
        }
        previous = current;
      })().catch((error: unknown) => {
        // A tick that threw never reached `previous = current`, so the window
        // it opened is still open — see `useLagSampler`'s `.catch` for
        // why that is left unbounded otherwise (#2314).
        previous = null;
        reportDecodeFailure(error);
      });
    }, DECODE_SAMPLE_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [status, stats, cameraIdentifier, getToken, reportMissingStatsField, reportDecodeFailure, reportThrottleRef]);
}
