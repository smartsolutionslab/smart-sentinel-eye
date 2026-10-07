import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  createReportThrottle,
  decodeElapsedBetween,
  decodeSampleFrom,
  missingDecodeFieldIn,
  PERIODIC_REPORT_INTERVAL_MS,
  reportKioskLatency,
  type DecodeSample,
} from './kioskLatency.js';

/**
 * Spec 040. The guards and the shape of a report.
 *
 * <p>
 * <b>Nothing here proves a number came from a frame.</b> This is a vitest unit
 * test running in Node: no browser, no peer connection, no stack of any kind, so
 * there is no frame here to have come from — these cover what happens to a
 * figure once it exists, and the figures themselves are read by a person against
 * the run-mode stack. A green suite standing in for an unexercised claim is the
 * same class of error that produced issue 1714.
 * </p>
 */
const token = () => Promise.resolve('a-token');

describe('reportKioskLatency — the guards', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 202 })),
    );
    vi.spyOn(console, 'info').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it('Sends a real measurement', async () => {
    reportKioskLatency('overlay_draw', 'cam-1', 18, token);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledOnce());

    const [, init] = vi.mocked(fetch).mock.calls[0]!;
    expect(JSON.parse(String(init?.body))).toEqual({
      measurement: 'overlay_draw',
      camera: 'cam-1',
      elapsedMilliseconds: 18,
    });
  });

  /**
   * Asserted as an **absence**, never as a zero. A zero would be
   * indistinguishable from a perfect journey and would read as a perfect score
   * for one nobody timed.
   */
  it('Sends nothing at all for a negative measurement', () => {
    reportKioskLatency('overlay_draw', 'cam-1', -3, token);
    expect(fetch).not.toHaveBeenCalled();
  });

  it('Sends nothing for a figure that describes a suspended tab', () => {
    reportKioskLatency('receive_to_decoded', 'cam-1', 120_000, token);
    expect(fetch).not.toHaveBeenCalled();
  });

  it('Sends nothing for a figure that is not a number', () => {
    reportKioskLatency('overlay_draw', 'cam-1', Number.NaN, token);
    expect(fetch).not.toHaveBeenCalled();
  });

  /**
   * FR-011. An observer that can break the thing it observes is worse than no
   * observer — a kiosk whose telemetry endpoint is down must carry on showing
   * video.
   */
  it('Never throws when reporting fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new Error('gateway down');
      }),
    );

    expect(() => reportKioskLatency('overlay_draw', 'cam-1', 18, token)).not.toThrow();
    await vi.waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it('Carries the camera, so one bad tile is visible among four', async () => {
    reportKioskLatency('receive_to_decoded', 'cam-frozen', 42, token);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledOnce());

    const [, init] = vi.mocked(fetch).mock.calls[0]!;
    expect(JSON.parse(String(init?.body)).camera).toBe('cam-frozen');
  });

  it('Emits a structured line alongside the report', () => {
    reportKioskLatency('overlay_draw', 'cam-1', 18, token);
    expect(console.info).toHaveBeenCalledWith('[latency]', {
      measurement: 'overlay_draw',
      camera: 'cam-1',
      elapsedMilliseconds: 18,
    });
  });

  /**
   * US3 (spec 228 item 4). A production wall is never restarted, so an
   * unconditional line per sample is retained console buffer forever — the
   * line is for manual verification and the e2e harvest, both of which run
   * under `vite dev` (`import.meta.env.DEV === true`). The POST is
   * unconditional either way: production observability must not go dark
   * along with the console line.
   */
  it('Writes no console line in a production build, but still sends the sample', async () => {
    vi.stubEnv('DEV', false);

    reportKioskLatency('overlay_draw', 'cam-1', 18, token);

    expect(console.info).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledOnce());
  });
});

/**
 * Spec 307 (#2563). A 4-tile wall at rest POSTs 246 req/min, 198 of them this
 * path — more than the gateway's shared rate-limit bucket gives it. The
 * samplers keep their cadence; only the network **send** is thinned, at most
 * once per {@link PERIODIC_REPORT_INTERVAL_MS} per measurement.
 *
 * <p>
 * <b>Keyed by measurement, not by camera.</b> `createReportThrottle`'s
 * caller decides the scope — one instance per mounted tile for the two tile
 * legs, one per wall for `wall_skew` — by how many instances it creates;
 * the throttle itself just answers per {@link KioskMeasurement} name, so
 * `presentation_buffer` and `receive_to_decoded` on the same tile never
 * block each other.
 * </p>
 *
 * <p>
 * `performance.now()`, never `Date.now()` — the same PTP-stepped-clock
 * reasoning `reportKioskLatency`'s own doc states for every timestamp on
 * this path.
 * </p>
 */
describe('createReportThrottle', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Admits the first sample for a measurement', () => {
    const throttle = createReportThrottle();
    expect(throttle('wall_skew')).toBe(true);
  });

  /**
   * Boundary-exact: refused one millisecond short of the window, admitted
   * at exactly the interval.
   */
  it('Refuses the same measurement one millisecond short of the window, and admits it at the boundary', () => {
    const throttle = createReportThrottle();
    expect(throttle('wall_skew')).toBe(true);

    vi.advanceTimersByTime(PERIODIC_REPORT_INTERVAL_MS - 1);
    expect(throttle('wall_skew')).toBe(false);

    vi.advanceTimersByTime(1);
    expect(throttle('wall_skew')).toBe(true);
  });

  it('Throttles each measurement independently, so one does not block another', () => {
    const throttle = createReportThrottle();
    expect(throttle('wall_skew')).toBe(true);
    expect(throttle('wall_skew')).toBe(false);

    // A different measurement's window has not been touched by the call above.
    expect(throttle('presentation_buffer')).toBe(true);
  });

  it('Accepts a narrower interval than the default', () => {
    const throttle = createReportThrottle(1_000);
    expect(throttle('wall_skew')).toBe(true);

    vi.advanceTimersByTime(999);
    expect(throttle('wall_skew')).toBe(false);

    vi.advanceTimersByTime(1);
    expect(throttle('wall_skew')).toBe(true);
  });
});

/**
 * Spec 307 (#2563). `reportKioskLatency`'s new, optional 5th argument.
 * Order of operations: guards → DEV console line → throttle → send — so a
 * figure the guards already reject can never spend a window a later, valid
 * figure needs, and a refused figure still shows up in the per-sample DEV
 * line (spec 108's e2e harvest, ADR-0122 "alongside, not instead of").
 */
describe('reportKioskLatency — a caller-supplied throttle', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 202 })),
    );
    vi.spyOn(console, 'info').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it('Sends the sample when the throttle admits it', async () => {
    reportKioskLatency('wall_skew', 'cam-1', 18, token, () => true);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledOnce());
  });

  /**
   * US1's "conflict" and "bad request" scenarios both rest on this: a
   * refused sample must cost nothing on the network path, while the DEV
   * line stays per sample regardless of whether the network send happened.
   */
  it('Sends nothing when the throttle refuses, but still prints the DEV console line', async () => {
    reportKioskLatency('wall_skew', 'cam-1', 18, token, () => false);

    expect(console.info).toHaveBeenCalledWith('[latency]', {
      measurement: 'wall_skew',
      camera: 'cam-1',
      elapsedMilliseconds: 18,
    });

    // `send` resolves `getToken` before it ever calls `fetch`, so the network
    // call — were one going to happen — would not yet have landed on the very
    // next synchronous line. Give it every chance to, before asserting it
    // never does.
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(fetch).not.toHaveBeenCalled();
  });

  /**
   * Proves the network path itself is skipped, not merely the POST body —
   * a throttled call must not even resolve a token.
   */
  it('Never resolves the token when the throttle refuses the sample', () => {
    const getToken = vi.fn(token);
    reportKioskLatency('wall_skew', 'cam-1', 18, getToken, () => false);
    expect(getToken).not.toHaveBeenCalled();
  });

  /**
   * Guards run before the throttle is ever consulted, so an invalid figure
   * cannot spend a window a later, valid figure needs.
   */
  it('Never consults the throttle for a figure the guards already reject', () => {
    const throttle = vi.fn();
    reportKioskLatency('wall_skew', 'cam-1', -3, token, throttle);
    reportKioskLatency('wall_skew', 'cam-1', Number.NaN, token, throttle);
    reportKioskLatency('wall_skew', 'cam-1', 60_001, token, throttle);

    expect(throttle).not.toHaveBeenCalled();
  });

  it('Still sends nothing for an invalid figure even when a throttle would admit it', () => {
    reportKioskLatency('wall_skew', 'cam-1', -3, token, () => true);
    expect(fetch).not.toHaveBeenCalled();
  });

  /** The Gherkin "bad request" scenario end to end, against a real throttle. */
  it('Ships the next valid figure in the window after an invalid figure was rejected by the guards', async () => {
    const throttle = createReportThrottle();

    reportKioskLatency('wall_skew', 'cam-1', -3, token, throttle);
    reportKioskLatency('wall_skew', 'cam-1', 42, token, throttle);

    await vi.waitFor(() => expect(fetch).toHaveBeenCalledOnce());
    const [, init] = vi.mocked(fetch).mock.calls[0]!;
    expect(JSON.parse(String(init?.body)).elapsedMilliseconds).toBe(42);
  });
});

describe('the decode fragment', () => {
  const sample = (framesDecoded: number, processing: number, decode: number): DecodeSample => ({
    framesDecoded,
    processingDelaySeconds: processing,
    decodeTimeSeconds: decode,
  });

  it('Reads the video receiver statistics', () => {
    const report = new Map<string, unknown>([
      ['a', { type: 'inbound-rtp', kind: 'audio', framesDecoded: 1 }],
      ['v', { type: 'inbound-rtp', kind: 'video', framesDecoded: 100, totalProcessingDelay: 2, totalDecodeTime: 0.5 }],
    ]);

    expect(decodeSampleFrom(report)).toEqual({
      framesDecoded: 100,
      processingDelaySeconds: 2,
      decodeTimeSeconds: 0.5,
    });
  });

  /**
   * Deltas, not the cumulative ratio. The statistics are monotonic counters
   * over the session's life, so a raw ratio reports the session average and
   * flattens exactly the excursion a budget is about.
   */
  it('Measures the interval, not the session', () => {
    const elapsed = decodeElapsedBetween(sample(100, 2, 0.5), sample(110, 2.3, 0.55));

    // 0.35s of work over 10 frames = 35 ms per frame.
    expect(elapsed).toBeCloseTo(35, 1);
  });

  /** Null rather than zero: no frames means no journey to time. */
  it('Reports nothing when no frames were decoded', () => {
    expect(decodeElapsedBetween(sample(100, 2, 0.5), sample(100, 2, 0.5))).toBeNull();
  });

  /** A counter that went backwards is a restarted session, not a fast one. */
  it('Reports nothing when the counters went backwards', () => {
    expect(decodeElapsedBetween(sample(100, 2, 0.5), sample(110, 1, 0.2))).toBeNull();
  });
});

/**
 * Spec 095 T003 / FR-001, FR-007. **The decode twin of `missingLagFieldIn`.**
 *
 * <p>
 * `decodeSampleFrom` reads the same `totalProcessingDelay` as
 * `lagSampleFrom`, with the same bare null, on a different leg of §IV —
 * SFU → kiosk decode. Issue #2109 lists `wallAlignment.ts` and stops; fixing
 * one and not the other would leave the identical silence one file over, on the
 * leg whose instrument has already gone quiet once (#1889).
 * </p>
 *
 * <p>
 * The key is deleted, never set to null, for the reason
 * `missingLagFieldIn`'s suite gives.
 * </p>
 */
describe('missingDecodeFieldIn', () => {
  const complete = (): Record<string, unknown> => ({
    framesDecoded: 100,
    totalProcessingDelay: 2,
    totalDecodeTime: 0.5,
  });

  const report = (stat: Record<string, unknown>): Map<string, unknown> =>
    new Map<string, unknown>([['v', { type: 'inbound-rtp', kind: 'video', ...stat }]]);

  it('Names the counter an inbound video stat leaves out', () => {
    const stat = complete();
    delete stat['totalProcessingDelay'];

    expect(missingDecodeFieldIn(report(stat))).toBe('totalProcessingDelay');
  });

  it('Names nothing when every counter reads', () => {
    expect(missingDecodeFieldIn(report(complete()))).toBeNull();
  });

  /** Not-yet-producing is not absent, and it happens on every mount (FR-002). */
  it('Names nothing when the report carries no inbound video stat at all', () => {
    const audioOnly = new Map<string, unknown>([['a', { type: 'inbound-rtp', kind: 'audio', framesDecoded: 100 }]]);

    expect(missingDecodeFieldIn(audioOnly)).toBeNull();
  });
});
