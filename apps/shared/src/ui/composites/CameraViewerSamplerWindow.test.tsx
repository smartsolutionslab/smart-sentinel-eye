// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockInstance } from 'vitest';

/**
 * Spec 197 / issue #2314. **The window a throw leaves open.**
 *
 * <p>
 * Each sampler in `CameraViewer.tsx` advances `previous = current` only at
 * the end of a fully successful tick. Anything that throws first — the
 * wall's own `onLagMeasured`, or `stats()` itself — leaves `previous` pinned
 * to the sample it already held, so the window stays open. Under a
 * persistent throw it stays open without bound, and the first tick that
 * succeeds afterwards reports a per-frame mean over the whole outage — the
 * cumulative session average `lagBetween`'s own doc forbids
 * (`wallAlignment.ts:152-158`).
 * </p>
 *
 * <p>
 * <b>Lives in its own file.</b> `CameraViewerAlignment.test.tsx` carries
 * issue #2189's regression guard and must pass unmodified through this
 * change (plan §"Why the test goes in a new file"), so the new cases are
 * kept physically out of it.
 * </p>
 *
 * <p>
 * <b>The fixture is the point.</b> That sibling file's `advancingVideoStat()`
 * advances by <em>constant</em> increments per call, so its per-frame mean
 * is identical at every window width — a widening test built on it would be
 * green before and after any fix, and prove nothing. This file's fixture
 * steps its rate partway through, so a widened window and a fresh window
 * report different, checkable numbers.
 * </p>
 *
 * <p>
 * <b>The fixture is a function of elapsed time, not of call count.</b> Both
 * samplers share one `stats()` double and read it at 2 000 ms and 5 000 ms —
 * interleaved. A fixture that stepped its rate on the Nth call would put the
 * step in the wrong window wherever the two cadences interleave. Computing
 * absolute counter values from elapsed `Date.now()` instead means any two
 * reads at t1 < t2 yield a delta that depends only on t1 and t2, so the
 * decode sampler's interleaved reads cannot perturb the lag sampler's
 * arithmetic, or vice versa.
 * </p>
 *
 * <p>
 * <b>`Date.now()`, never `performance.now()`.</b> Vitest's default
 * `toFake` list includes `Date` and excludes `performance`
 * (`CellPage.test.tsx:503-506` carries the same finding, for the same
 * reason). A fixture reading an unfaked `performance.now()` would see real
 * wall-clock time while the component's own timers ran on fake time — every
 * delta ~0, every figure null, every assertion vacuous.
 * </p>
 */

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

// Mirrors CameraViewerAlignment.test.tsx's own double shape: one mocked
// class, and each case swaps `statsBehaviour` in rather than adding a
// second module double.
let statsBehaviour: () => unknown = () => Promise.reject(new Error('statsBehaviour not set for this case'));

interface WhepClientDoubleOptions {
  onConnectionStateChange?: (state: string) => void;
}

const sessions: WhepClientDoubleOptions[] = [];

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // **Must report `connected`, or this whole file is vacuous.** Every
    // effect in CameraViewer guards on `status !== 'live'`, and `status` is
    // driven only by this callback — never by `connect()` resolving.
    // CameraViewerAlignment.test.tsx carries the same warning, found the
    // hard way there.
    constructor(private readonly options: WhepClientDoubleOptions) {
      sessions.push(options);
    }

    async connect(videoEl: HTMLVideoElement) {
      videoEl.dataset['connected'] = 'true';
      this.options.onConnectionStateChange?.('connected');
    }
    close() {}
    stats = () => statsBehaviour();
    setPlayoutTarget = () => 'applied' as const;
  },
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

// 30 frames per 2 000 ms lag tick, expressed per millisecond.
const FRAMES_PER_MS = 0.015;

/** The per-ms accrual rate a counter needs to average a given ms/frame figure. */
const coefficientFor = (msPerFrame: number): number => (msPerFrame * FRAMES_PER_MS) / 1000;

// Processing never steps in this file — it is not under test here, and
// keeping it flat means any discrepancy traces to the counter that does.
const PROCESSING_MS_PER_FRAME = 0.5;

const BUFFER_MS_PER_FRAME_BEFORE = 1.0;
const BUFFER_MS_PER_FRAME_AFTER = 4.0;
const DECODE_MS_PER_FRAME_BEFORE = 1.0;
const DECODE_MS_PER_FRAME_AFTER = 4.0;

/**
 * One receiver-statistics report, computed from elapsed time `t` — never
 * mutated per call (see file doc). `bufferStepAtMs` / `decodeStepAtMs` each
 * default to `Infinity` (no step), so a case that only needs one counter to
 * move leaves the other flat.
 */
function videoStatAt(
  t: number,
  { bufferStepAtMs = Infinity, decodeStepAtMs = Infinity }: { bufferStepAtMs?: number; decodeStepAtMs?: number } = {},
): Map<string, unknown> {
  const framesDecoded = 200 + FRAMES_PER_MS * t;
  const jitterBufferEmittedCount = 200 + FRAMES_PER_MS * t;
  const totalProcessingDelay = 1.25 + coefficientFor(PROCESSING_MS_PER_FRAME) * t;

  const jitterBufferDelay =
    2.5 +
    coefficientFor(BUFFER_MS_PER_FRAME_BEFORE) * Math.min(t, bufferStepAtMs) +
    coefficientFor(BUFFER_MS_PER_FRAME_AFTER) * Math.max(0, t - bufferStepAtMs);

  const totalDecodeTime =
    0.5 +
    coefficientFor(DECODE_MS_PER_FRAME_BEFORE) * Math.min(t, decodeStepAtMs) +
    coefficientFor(DECODE_MS_PER_FRAME_AFTER) * Math.max(0, t - decodeStepAtMs);

  return new Map<string, unknown>([
    [
      'v',
      {
        type: 'inbound-rtp',
        kind: 'video',
        jitterBufferDelay,
        jitterBufferEmittedCount,
        totalProcessingDelay,
        totalDecodeTime,
        framesDecoded,
      },
    ],
  ]);
}

/**
 * A `stats()` double whose counters are the pure function above, with an
 * optional single throw at an exact elapsed-time boundary — never on a call
 * count, so it cannot land on the wrong sampler's tick when the two
 * cadences interleave (see file doc).
 */
function steppedStatsDouble(options: {
  bufferStepAtMs?: number;
  decodeStepAtMs?: number;
  throwAtMs?: number;
}): () => Promise<Map<string, unknown>> {
  const epoch = Date.now();
  let thrown = false;
  return vi.fn(() => {
    const t = Date.now() - epoch;
    if (options.throwAtMs !== undefined && !thrown && t === options.throwAtMs) {
      thrown = true;
      throw new Error('getStats exploded');
    }
    return Promise.resolve(
      videoStatAt(t, { bufferStepAtMs: options.bufferStepAtMs, decodeStepAtMs: options.decodeStepAtMs }),
    );
  });
}

describe('CameraViewer sampler window after a throw (#2314)', () => {
  let infoSpy: MockInstance<typeof console.info>;

  /** The `[latency]` lines carrying one measurement — mirrors kioskLatency.ts:87. */
  const latencyLines = (measurement: string) =>
    infoSpy.mock.calls.filter(
      (call) =>
        call[0] === '[latency]' && (call[1] as { measurement?: unknown } | undefined)?.measurement === measurement,
    );

  /** The `[resilience]` lines carrying one transition — mirrors the sibling file's helper. */
  const resilienceLines = (transition: string) =>
    infoSpy.mock.calls.filter(
      (call) =>
        call[0] === '[resilience]' && (call[1] as { transition?: unknown } | undefined)?.transition === transition,
    );

  beforeEach(() => {
    vi.useFakeTimers();
    sessions.length = 0;
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
    useGetStreamQueryMock.mockReturnValue({
      data: { state: 'Healthy', whepUrl: 'http://sfu/whep/cam-42', error: null },
      currentData: { state: 'Healthy', whepUrl: 'http://sfu/whep/cam-42', error: null },
      isLoading: false,
      error: undefined,
    });
  });

  afterEach(() => {
    cleanup();
    infoSpy.mockRestore();
    vi.useRealTimers();
  });

  describe('US1 — the lag sampler', () => {
    /**
     * Spec 197 §"Acceptance scenarios" — the headline case. `STEP_AT = 4 000`:
     * immediately after the lag tick that throws (`t = 4 000`), so the window
     * that would open next (today: 2 000→6 000; fixed: 6 000→8 000) sits
     * entirely on one side of the step or spans it exactly in half.
     *
     * <p>
     * Today: `previous` stays pinned at `S(2 000)`, so the tick at `t = 6 000`
     * reports a 4 000 ms window — half before the step, half after — and
     * blends to 2.5 ms. The correct, fresh-window answer (`t = 8 000`, a
     * 2 000 ms window entirely after the step) is 4.0 ms. Red today; green
     * once `previous` resets in the `.catch` (T004).
     * </p>
     */
    it('Reports the fresh-window figure, not the widened blend, after a one-off callback throw', async () => {
      statsBehaviour = steppedStatsDouble({ bufferStepAtMs: 4_000 });

      let callbackCalls = 0;
      const onLagMeasured = vi.fn((_camera: string, _lag: number, _buffer: number) => {
        callbackCalls += 1;
        if (callbackCalls === 1) throw new Error('wall callback exploded');
      });

      const { container } = render(
        <CameraViewer
          cameraIdentifier="cam-42"
          getToken={() => Promise.resolve('token')}
          onLagMeasured={onLagMeasured}
        />,
      );

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });

      // Asserted BEFORE the figures, per CameraViewerAlignment.test.tsx's own
      // convention: a callback never reached would make every assertion
      // below true of a component that measured nothing.
      expect(onLagMeasured, 'the wall callback must actually have run').toHaveBeenCalled();
      expect(callbackCalls, 'the callback must have thrown once and then succeeded').toBeGreaterThan(1);
      expect(container.querySelector('video'), 'reporting must not cost the picture').not.toBeNull();

      const buffered = latencyLines('presentation_buffer');
      expect(buffered.length, 'a presentation_buffer figure must have been reported after the throw').toBeGreaterThan(
        0,
      );
      const firstAfterThrow = buffered[0]![1] as { elapsedMilliseconds: number };
      // toBeCloseTo, not toBe: the coefficients (1.5e-5, 6.0e-5, 7.5e-6) are
      // not exactly representable in IEEE-754 binary, unlike the sibling
      // file's simple decimal deltas, so the accumulated `base + rate * t`
      // arithmetic carries ~1e-13 noise. Precision 5 is far tighter than the
      // ≥1.5 ms difference under test, so it cannot absorb the excursion.
      expect(firstAfterThrow.elapsedMilliseconds).toBeCloseTo(4.0, 5);

      const firstLagAfterThrow = onLagMeasured.mock.calls[1]!;
      expect(firstLagAfterThrow[1]).toBeCloseTo(4.5, 5);
    });

    /**
     * Spec 197 §"Acceptance scenarios" — the conflict case, and the one the
     * architect found actually matters: an unbounded pin, not a one-tick
     * widening. The callback is gated on elapsed time (`< 24 000`), not on a
     * call count — under the fix `previous` resets on every throw, so the
     * callback is only actually reached on alternating ticks, and a
     * call-count boundary would land at a different wall-clock time
     * depending on whether the fix is applied. Gating on time keeps "the
     * callback recovers at t = 24 000" true either way, which is what makes
     * this comparable before and after.
     *
     * <p>
     * Today: `previous` never advances through the ten-tick outage, so the
     * first successful tick (`t = 24 000`) reports one window spanning the
     * whole 22 000 ms since the very first sample — a blend, not the fresh
     * 4.0 ms a correctly-reset window would report.
     * </p>
     */
    it('Does not accumulate a window under a persistently throwing callback', async () => {
      statsBehaviour = steppedStatsDouble({ bufferStepAtMs: 4_000 });

      const epoch = Date.now();
      const onLagMeasured = vi.fn((_camera: string, _lag: number, _buffer: number) => {
        if (Date.now() - epoch < 24_000) throw new Error('wall callback exploded');
      });

      const { container } = render(
        <CameraViewer
          cameraIdentifier="cam-42"
          getToken={() => Promise.resolve('token')}
          onLagMeasured={onLagMeasured}
        />,
      );

      await act(async () => {
        await vi.advanceTimersByTimeAsync(24_000);
      });

      expect(onLagMeasured, 'the wall callback must actually have run').toHaveBeenCalled();
      expect(onLagMeasured.mock.calls.length, 'the callback must have thrown more than once').toBeGreaterThan(1);
      expect(container.querySelector('video'), 'reporting must not cost the picture').not.toBeNull();

      // No figure anywhere is a mean over the outage: the one report that
      // reaches the histogram by t = 24 000 must be the fresh 2 000 ms
      // window that ends it (4.0 ms) — never a blend over the run of throws.
      const buffered = latencyLines('presentation_buffer');
      expect(buffered).toHaveLength(1);
      expect((buffered[0]![1] as { elapsedMilliseconds: number }).elapsedMilliseconds).toBeCloseTo(4.0, 5);

      const lastLagCall = onLagMeasured.mock.calls[onLagMeasured.mock.calls.length - 1]!;
      expect(lastLagCall[1]).toBeCloseTo(4.5, 5);
    });

    /**
     * Spec 197 §"Acceptance scenarios" — the same defect through `stats()`,
     * which is not our code (#2189 exists because it throws). Structurally
     * identical to the headline case, but the throw is gated on elapsed time
     * (`t === 4 000`, once) rather than call count, since `stats()` is the
     * double both samplers share — a call-count throw could land on either
     * sampler's read depending on ordering.
     */
    it('Recovers a fresh window the same way when getStats — not our code — throws', async () => {
      statsBehaviour = steppedStatsDouble({ bufferStepAtMs: 4_000, throwAtMs: 4_000 });
      const onLagMeasured = vi.fn();

      const { container } = render(
        <CameraViewer
          cameraIdentifier="cam-42"
          getToken={() => Promise.resolve('token')}
          onLagMeasured={onLagMeasured}
        />,
      );

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });

      expect(onLagMeasured, 'the wall callback must actually have run').toHaveBeenCalled();
      expect(container.querySelector('video'), 'reporting must not cost the picture').not.toBeNull();

      const buffered = latencyLines('presentation_buffer');
      expect(buffered.length, 'a presentation_buffer figure must have been reported after the throw').toBeGreaterThan(
        0,
      );
      expect((buffered[0]![1] as { elapsedMilliseconds: number }).elapsedMilliseconds).toBeCloseTo(4.0, 5);
    });

    /**
     * Spec 197 DoD — must stay green before and after. The fixture still
     * carries a step (`bufferStepAtMs: 4 000`), to prove windows localise it
     * correctly rather than merely because nothing ever pins `previous` in
     * this case: [1.0, 4.0, 4.0, 4.0] is only possible if every window is
     * exactly one 2 000 ms tick wide, never wider.
     */
    it('Reports a clean 2 000 ms window every tick when nothing throws', async () => {
      statsBehaviour = steppedStatsDouble({ bufferStepAtMs: 4_000 });
      const onLagMeasured = vi.fn();

      const { container } = render(
        <CameraViewer
          cameraIdentifier="cam-42"
          getToken={() => Promise.resolve('token')}
          onLagMeasured={onLagMeasured}
        />,
      );

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });

      expect(onLagMeasured, 'the wall callback must actually have run').toHaveBeenCalled();
      expect(container.querySelector('video'), 'reporting must not cost the picture').not.toBeNull();
      expect(resilienceLines('lag-sampler-failed')).toHaveLength(0);
      expect(resilienceLines('decode-sampler-failed')).toHaveLength(0);

      const buffered = latencyLines('presentation_buffer').map(
        (call) => (call[1] as { elapsedMilliseconds: number }).elapsedMilliseconds,
      );
      expect(buffered).toEqual([
        expect.closeTo(1.0, 5),
        expect.closeTo(4.0, 5),
        expect.closeTo(4.0, 5),
        expect.closeTo(4.0, 5),
      ]);
    });
  });

  describe('US2 — the decode sampler', () => {
    /**
     * Spec 197 §"Acceptance scenarios" (US2). Identical shape to the lag
     * sampler's headline case, on the decode sampler's 5 000 ms cadence.
     * `STEP_AT = 10 000`, immediately after the decode tick that throws.
     *
     * <p>
     * <b>No wall passed</b> (`onLagMeasured` omitted): the decode sampler runs
     * independently of it (`CameraViewer.tsx:200-207`), and the lag sampler's
     * own tick coincides with the decode sampler's at `t = 10 000` (both
     * 2 000 ms and 5 000 ms divide it) — leaving the lag sampler unstarted
     * keeps this case's throw from also landing on the lag sampler's window.
     * </p>
     *
     * <p>
     * Today: `previous` stays pinned at `S(5 000)`, so the tick at
     * `t = 15 000` reports a 10 000 ms window — half before the step, half
     * after — and blends to 3.0 ms. The correct, fresh-window answer
     * (`t = 20 000`, a 5 000 ms window entirely after the step) is 4.5 ms.
     * </p>
     */
    it('Reports the fresh-window figure, not the widened blend, after a getStats throw on the decode sampler', async () => {
      statsBehaviour = steppedStatsDouble({ decodeStepAtMs: 10_000, throwAtMs: 10_000 });

      const { container } = render(
        <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} />,
      );

      await act(async () => {
        await vi.advanceTimersByTimeAsync(20_000);
      });

      // Asserted BEFORE the figures, per this file's convention.
      expect(statsBehaviour, 'the decode sampler must actually have run').toHaveBeenCalled();
      expect(container.querySelector('video'), 'reporting must not cost the picture').not.toBeNull();

      const decoded = latencyLines('receive_to_decoded');
      expect(decoded.length, 'a receive_to_decoded figure must have been reported after the throw').toBeGreaterThan(0);
      expect((decoded[0]![1] as { elapsedMilliseconds: number }).elapsedMilliseconds).toBeCloseTo(4.5, 5);

      // #2189's decade cadence, unaffected by this fix: one throw, one line.
      const failed = resilienceLines('decode-sampler-failed');
      expect(failed).toHaveLength(1);
      expect(failed[0]![1]).toMatchObject({ count: 1, reason: 'getStats exploded' });
    });
  });
});
