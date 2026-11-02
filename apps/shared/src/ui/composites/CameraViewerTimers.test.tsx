// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockInstance } from 'vitest';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import type { CameraViewerProps } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * Spec 479 (SC-006 / FR-010). **How many timers a live tile costs.**
 *
 * <p>
 * The kiosk wall runs up to 250 tiles, and every live tile owns its own
 * sampling intervals: a 5 000 ms decode sampler always, and a 2 000 ms lag
 * sampler only when the caller passed `onLagMeasured`. A refactor that
 * quietly adds a third interval, forgets to clear one, or rebuilds them on
 * every render multiplies by 250 on the latency path. This file pins the
 * figures — observed through `window.setInterval` / `window.clearInterval`
 * rather than through the source — so the split of `CameraViewer` cannot
 * change them unnoticed.
 * </p>
 *
 * <p>
 * Lives in its own file: the ten covering `CameraViewer*.test.tsx` files must
 * pass unmodified through the split, so the new cases are kept out of them.
 * </p>
 */

interface WhepClientDoubleOptions {
  onConnectionStateChange?: (state: string) => void;
}

const sessions: WhepClientDoubleOptions[] = [];

// When false, `connect()` leaves the tile on `connecting`; the case then
// reports `connected` itself, so "before live" is observable.
let autoConnect = true;

let statsBehaviour: () => unknown = () => Promise.reject(new Error('statsBehaviour not set for this case'));

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // Must report `connected`, or every effect (gated on `status !== 'live'`)
    // never runs and every assertion here is vacuous — the warning
    // CameraViewerAlignment.test.tsx carries, found the hard way there.
    constructor(private readonly options: WhepClientDoubleOptions) {
      sessions.push(options);
    }

    async connect(videoEl: HTMLVideoElement) {
      videoEl.dataset['connected'] = 'true';
      if (autoConnect) this.options.onConnectionStateChange?.('connected');
    }
    close() {}
    stats = () => statsBehaviour();
    setPlayoutTarget = () => 'applied' as const;
  },
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

const STREAM: StreamHealth = {
  cameraIdentifier: 'cam-42',
  state: 'Healthy',
  whepUrl: 'http://sfu/whep/cam-42',
  transcodeMode: 'Passthrough',
  lastSuccessAt: null,
  error: null,
};

const OTHER_STREAM: StreamHealth = {
  ...STREAM,
  cameraIdentifier: 'cam-43',
  whepUrl: 'http://sfu/whep/cam-43',
};

const DEGRADED_STREAM: StreamHealth = { ...STREAM, state: 'Degraded', error: 'Source unreachable.' };

const GET_TOKEN = () => Promise.resolve('token');

const DECODE_INTERVAL_MS = 5_000;
const LAG_INTERVAL_MS = 2_000;

// A steady, always-positive delta between any two reads — a pure function of
// elapsed `Date.now()`, never of call count (CameraViewerSamplerWindow's rule).
const FRAMES_PER_MS = 0.015;
function steadyStatsDouble(): () => Promise<Map<string, unknown>> {
  const epoch = Date.now();
  return vi.fn(() => {
    const t = Date.now() - epoch;
    return Promise.resolve(
      new Map<string, unknown>([
        [
          'v',
          {
            type: 'inbound-rtp',
            kind: 'video',
            jitterBufferDelay: 2.5 + 0.000015 * t,
            jitterBufferEmittedCount: 200 + FRAMES_PER_MS * t,
            totalProcessingDelay: 1.25 + 0.0000075 * t,
            totalDecodeTime: 0.5 + 0.000015 * t,
            framesDecoded: 200 + FRAMES_PER_MS * t,
          },
        ],
      ]),
    );
  });
}

function viewer(props: Partial<CameraViewerProps> = {}) {
  return <CameraViewer cameraIdentifier="cam-42" getToken={GET_TOKEN} stream={STREAM} {...props} />;
}

const driveFakes = () =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });

const advance = (milliseconds: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });

describe('CameraViewer sampling timers (SC-006)', () => {
  let setSpy: MockInstance<typeof window.setInterval>;
  let clearSpy: MockInstance<typeof window.clearInterval>;

  /** Every interval ever created, in creation order, with the id the timer API handed back. */
  const created = () =>
    setSpy.mock.calls.map((call, index) => ({
      id: setSpy.mock.results[index]!.value as unknown,
      delay: call[1] as number,
    }));

  /** The delays of the intervals created and not yet cleared, ascending. */
  const activeDelays = () => {
    const cleared = clearSpy.mock.calls.map((call) => call[0] as unknown);
    return created()
      .filter((interval) => !cleared.includes(interval.id))
      .map((interval) => interval.delay)
      .sort((a, b) => a - b);
  };

  beforeEach(() => {
    vi.useFakeTimers();
    sessions.length = 0;
    autoConnect = true;
    statsBehaviour = steadyStatsDouble();
    vi.spyOn(console, 'info').mockImplementation(() => {});
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: true, status: 202 })),
    );
    // Spied AFTER the fake clock is installed, so the spies wrap the fakes.
    setSpy = vi.spyOn(window, 'setInterval');
    clearSpy = vi.spyOn(window, 'clearInterval');
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('Owns exactly one interval, the 5 000 ms decode sampler, on a live tile with no lag callback', async () => {
    render(viewer());
    await driveFakes();

    expect(sessions, 'the tile must actually have opened a session').toHaveLength(1);
    expect(activeDelays()).toEqual([DECODE_INTERVAL_MS]);
    expect(created(), 'and nothing was created and thrown away on the way').toHaveLength(1);
  });

  it('Owns exactly two intervals, 2 000 ms and 5 000 ms, on a live tile with a lag callback', async () => {
    render(viewer({ onLagMeasured: vi.fn() }));
    await driveFakes();

    expect(sessions).toHaveLength(1);
    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);
    expect(created()).toHaveLength(2);
  });

  it('Starts no interval until the tile is live', async () => {
    autoConnect = false;
    render(viewer({ onLagMeasured: vi.fn() }));
    await driveFakes();

    expect(sessions, 'the tile must have a session that has not connected').toHaveLength(1);
    expect(created()).toHaveLength(0);
    expect(statsBehaviour).not.toHaveBeenCalled();

    await act(async () => {
      sessions[0]!.onConnectionStateChange?.('connected');
    });
    await advance(0);

    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);
  });

  it('Creates no new interval and clears none when the parent re-renders with a new inline onLagMeasured', async () => {
    // Issue 1889: a fresh closure each render must not rebuild the lag
    // sampler, or the interval is cleared before it takes a second sample and
    // the wall never aligns.
    const received: number[] = [];
    const { rerender } = render(viewer({ onLagMeasured: () => received.push(0) }));
    await driveFakes();
    expect(created()).toHaveLength(2);

    for (let render_ = 1; render_ <= 8; render_++) {
      await advance(500);
      rerender(viewer({ onLagMeasured: () => received.push(render_) }));
    }

    expect(created(), 'no interval may be created by a re-render').toHaveLength(2);
    expect(clearSpy, 'no interval may be cleared by a re-render').not.toHaveBeenCalled();
    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);

    // 4 000 ms elapsed: tick at 2 000 seeds, tick at 4 000 reports — which
    // only happens because the same interval survived all eight re-renders.
    expect(received.length, 'the lag callback must still fire').toBeGreaterThan(0);
  });

  it('Clears both intervals when the tile leaves live, and starts two fresh ones when it returns', async () => {
    const { rerender } = render(viewer({ onLagMeasured: vi.fn() }));
    await driveFakes();
    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);

    rerender(viewer({ stream: DEGRADED_STREAM, onLagMeasured: vi.fn() }));
    await driveFakes();

    expect(activeDelays(), 'a reconnecting tile samples nothing').toEqual([]);
    expect(clearSpy).toHaveBeenCalledTimes(2);

    const statsCallsWhileDown = (statsBehaviour as ReturnType<typeof vi.fn>).mock.calls.length;
    await advance(10_000);
    expect(
      (statsBehaviour as ReturnType<typeof vi.fn>).mock.calls.length,
      'no sampler may keep reading while the tile is not live',
    ).toBe(statsCallsWhileDown);

    rerender(viewer({ stream: STREAM, onLagMeasured: vi.fn() }));
    await driveFakes();

    expect(sessions, 'recovery rebuilds the session').toHaveLength(2);
    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);
  });

  it('Clears every interval on unmount', async () => {
    const { unmount } = render(viewer({ onLagMeasured: vi.fn() }));
    await driveFakes();
    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);

    unmount();

    expect(activeDelays()).toEqual([]);
    const statsCalls = (statsBehaviour as ReturnType<typeof vi.fn>).mock.calls.length;
    await advance(10_000);
    expect((statsBehaviour as ReturnType<typeof vi.fn>).mock.calls.length).toBe(statsCalls);
  });

  it('Recreates both intervals, and leaves exactly two, when the camera changes (current behaviour, pinned as is)', async () => {
    const { rerender } = render(viewer({ onLagMeasured: vi.fn() }));
    await driveFakes();
    expect(created()).toHaveLength(2);

    rerender(viewer({ cameraIdentifier: 'cam-43', stream: OTHER_STREAM, onLagMeasured: vi.fn() }));
    await driveFakes();

    expect(sessions, 'the swap opened a second session').toHaveLength(2);
    expect(created(), 'two old intervals, two new ones').toHaveLength(4);
    expect(clearSpy).toHaveBeenCalledTimes(2);
    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);
  });

  it('Recreates both intervals, and leaves exactly two, when getToken changes identity (current behaviour, pinned as is)', async () => {
    const { rerender } = render(viewer({ onLagMeasured: vi.fn() }));
    await driveFakes();
    expect(created()).toHaveLength(2);

    rerender(viewer({ getToken: () => Promise.resolve('another-token'), onLagMeasured: vi.fn() }));
    await driveFakes();

    expect(sessions, 'a new getToken must not renegotiate the session').toHaveLength(1);
    expect(created()).toHaveLength(4);
    expect(clearSpy).toHaveBeenCalledTimes(2);
    expect(activeDelays()).toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);
  });

  it('Starts the lag interval when onLagMeasured appears while live, and stops it when it goes away', async () => {
    const { rerender } = render(viewer());
    await driveFakes();
    expect(activeDelays()).toEqual([DECODE_INTERVAL_MS]);

    rerender(viewer({ onLagMeasured: vi.fn() }));
    await driveFakes();
    expect(activeDelays(), 'the callback arrived: sampling starts').toEqual([LAG_INTERVAL_MS, DECODE_INTERVAL_MS]);

    rerender(viewer());
    await driveFakes();
    expect(activeDelays(), 'the callback left: sampling stops, the decode sampler stays').toEqual([DECODE_INTERVAL_MS]);
  });
});
