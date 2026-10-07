// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockInstance } from 'vitest';

/**
 * Spec 307 (#2563). **A 4-tile wall at rest sends 246 req/min; 198 of those
 * are this path.** The samplers keep their existing cadence — the decode
 * sampler still reads every 5 s, the lag sampler still reads (and still
 * calls `onLagMeasured`) every 2 s — but the network **send** for the two
 * tile legs is thinned to at most one POST per 30 s per leg, per mounted
 * tile.
 *
 * <p>
 * <b>Lives in its own file</b> for the same reason
 * `CameraViewerSamplerWindow.test.tsx` does (its own doc, #2314): this
 * exercises the send-cadence path, not the window-after-a-throw path, and
 * `CameraViewerAlignment.test.tsx` / `CameraViewerSamplerWindow.test.tsx`
 * must pass unmodified through this change — kept physically out of both.
 * </p>
 *
 * <p>
 * <b>The fixture is a steady, un-stepped function of elapsed time</b> —
 * `CameraViewerSamplerWindow.test.tsx`'s own `videoStatAt`, minus the step:
 * both samplers share one `stats()` double, interleaved at 2 000 ms and
 * 5 000 ms, so a fixture that is a pure function of elapsed `Date.now()`
 * (never call count) gives every tick of either cadence a predictable,
 * always-positive delta — which is what makes "a report every tick after
 * the first" a fact about the code under test, not an artefact of the
 * fixture.
 * </p>
 */

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

interface WhepClientDoubleOptions {
  onConnectionStateChange?: (state: string) => void;
}

// Every session the double built, in order — so the reconnect scenario
// below can show a second session really happened, not merely that status
// flickered.
const sessions: WhepClientDoubleOptions[] = [];

let statsBehaviour: () => unknown = () => Promise.reject(new Error('statsBehaviour not set for this case'));

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // Must report `connected`, or every effect below (gated on
    // `status !== 'live'`) never runs and every assertion here would be
    // vacuously true — the same warning CameraViewerSamplerWindow.test.tsx
    // and CameraViewerAlignment.test.tsx both carry, found the hard way there.
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

// 30 frames per 2 000 ms lag tick, expressed per millisecond — the same
// rate CameraViewerSamplerWindow.test.tsx uses, so this file's counts are
// comparable to that one's.
const FRAMES_PER_MS = 0.015;
const coefficientFor = (msPerFrame: number): number => (msPerFrame * FRAMES_PER_MS) / 1000;
const PROCESSING_MS_PER_FRAME = 0.5;
const BUFFER_MS_PER_FRAME = 1.0;
const DECODE_MS_PER_FRAME = 1.0;

/** One receiver-statistics report, as a pure function of elapsed time `t` — never stepped (see file doc). */
function videoStatAt(t: number): Map<string, unknown> {
  const framesDecoded = 200 + FRAMES_PER_MS * t;
  const jitterBufferEmittedCount = 200 + FRAMES_PER_MS * t;
  const totalProcessingDelay = 1.25 + coefficientFor(PROCESSING_MS_PER_FRAME) * t;
  const jitterBufferDelay = 2.5 + coefficientFor(BUFFER_MS_PER_FRAME) * t;
  const totalDecodeTime = 0.5 + coefficientFor(DECODE_MS_PER_FRAME) * t;

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
 * A `stats()` double whose counters grow at a steady rate forever, keyed to
 * its own epoch rather than the test's — so a reconnect (a torn-down and
 * rebuilt `WhepClient`, but the same shared double underneath) sees a
 * seamless, still-growing counter rather than one that resets to zero and
 * produces a spurious "no frames yet" null.
 */
function steadyStatsDouble(): () => Promise<Map<string, unknown>> {
  const epoch = Date.now();
  return vi.fn(() => Promise.resolve(videoStatAt(Date.now() - epoch)));
}

describe('CameraViewer report cadence (#2563)', () => {
  let infoSpy: MockInstance<typeof console.info>;
  let posted: unknown[];

  /** The `[latency]` lines carrying one measurement — mirrors kioskLatency.ts's own line shape. */
  const latencyLines = (measurement: string) =>
    infoSpy.mock.calls.filter(
      (call) =>
        call[0] === '[latency]' && (call[1] as { measurement?: unknown } | undefined)?.measurement === measurement,
    );

  /** The POSTs `fetch` actually received, filtered by measurement. */
  const postedCallsFor = (measurement: string) =>
    posted.filter((body) => (body as { measurement?: string }).measurement === measurement);

  beforeEach(() => {
    vi.useFakeTimers();
    sessions.length = 0;
    posted = [];
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: string, init: { body?: unknown }) => {
        posted.push(JSON.parse(String(init.body)));
        return { ok: true, status: 202 };
      }),
    );
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
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  /**
   * Spec 307 US1, scenario 1. **The headline fact.** Before this fix: the lag
   * sampler ticked every 2 s, seeded on its first tick, and reported on every
   * one after — 59 `presentation_buffer` POSTs over 120 s. The decode
   * sampler, 5 s cadence, same shape — 23 `receive_to_decoded` POSTs. The
   * design caps each at 4: the first valid sample ships immediately, and
   * then at most once per 30 s (4, 34, 64, 94 s for the 2 s-cadence leg;
   * 10, 40, 70, 100 s for the 5 s-cadence leg).
   */
  it('Ships at most 4 presentation_buffer and 4 receive_to_decoded POSTs over 120 s', async () => {
    statsBehaviour = steadyStatsDouble();
    const onLagMeasured = vi.fn();

    const { container } = render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={() => Promise.resolve('token')}
        onLagMeasured={onLagMeasured}
      />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(120_000);
    });

    expect(onLagMeasured, 'the wall callback must actually have run').toHaveBeenCalled();
    expect(container.querySelector('video'), 'throttling the report must not cost the picture').not.toBeNull();

    // Before this fix, this ran 59 presentation_buffer and 23 receive_to_decoded
    // POSTs over the same window — see the PR body for the captured red evidence.

    // The target this change ships.
    expect(postedCallsFor('presentation_buffer')).toHaveLength(4);
    expect(postedCallsFor('receive_to_decoded')).toHaveLength(4);
  });

  /**
   * Spec 307 US1, scenario: "Console lines stay per sample in DEV". Only the
   * network send is thinned — `onLagMeasured` is never gated by anything
   * this change adds, so its call count must stay at the raw, un-throttled
   * tick count both before and after.
   */
  it('Keeps calling onLagMeasured on every 2 s lag sample, independent of the send throttle', async () => {
    statsBehaviour = steadyStatsDouble();
    const onLagMeasured = vi.fn();

    render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={() => Promise.resolve('token')}
        onLagMeasured={onLagMeasured}
      />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(120_000);
    });

    // 60 ticks over 120 s at 2 s/tick; the first seeds `previous` and reports
    // nothing, so 59 carry a delta.
    expect(onLagMeasured).toHaveBeenCalledTimes(59);
  });

  /**
   * Same scenario, the other half: the DEV `[latency]` line is emitted
   * alongside every call to `reportKioskLatency`, before the throttle is
   * ever consulted (kioskLatency.ts's own order of operations) — so it
   * stays at the raw tick count for both legs, not the thinned POST count.
   */
  it('Keeps printing a [latency] line per sample for both legs, independent of the send throttle', async () => {
    statsBehaviour = steadyStatsDouble();

    render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={vi.fn()} />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(120_000);
    });

    expect(latencyLines('presentation_buffer')).toHaveLength(59);
    expect(latencyLines('receive_to_decoded')).toHaveLength(23);
  });

  /**
   * Spec 307 US1, scenario: "A reconnecting tile does not reopen its
   * window." The throttle is owned by the component (one `useRef` per
   * mounted tile, per plan.md §2), so it must survive a session being torn
   * down and rebuilt — only the sampler's own `previous` delta-window resets
   * on a reconnect, never the 30 s send window.
   *
   * <p>
   * Before this fix, for a different reason than scenario 1: nothing
   * throttled anything, so a reconnect's fresh session produced a second
   * `presentation_buffer` POST well inside what would be the first POST's
   * 30 s window — proving the gap this case exists to close, independent of
   * the raw-cadence fact scenario 1 already established.
   * </p>
   */
  it('Sends no additional presentation_buffer POST across a live → reconnecting → live cycle inside the 30 s window', async () => {
    statsBehaviour = steadyStatsDouble();
    let streamHealth: { state: string; whepUrl: string; error: string | null } = {
      state: 'Healthy',
      whepUrl: 'http://sfu/whep/cam-42',
      error: null,
    };
    useGetStreamQueryMock.mockImplementation(() => ({
      data: streamHealth,
      currentData: streamHealth,
      isLoading: false,
      error: undefined,
    }));

    const view = render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={vi.fn()} />,
    );

    // First valid sample: ships immediately (spec 307's own "first figure
    // still ships" scenario) — one POST, at 4 s.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4_000);
    });
    expect(postedCallsFor('presentation_buffer'), 'the first sample must ship on its own').toHaveLength(1);
    expect(sessions, 'one session so far').toHaveLength(1);

    // The tile drops and comes back, well inside the 30 s window the first
    // POST opened (4 s + 30 s = 34 s).
    streamHealth = { state: 'Degraded', whepUrl: 'http://sfu/whep/cam-42', error: 'Source unreachable.' };
    view.rerender(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={vi.fn()} />,
    );

    streamHealth = { state: 'Healthy', whepUrl: 'http://sfu/whep/cam-42', error: null };
    view.rerender(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={vi.fn()} />,
    );

    expect(sessions, 'the reconnect must really have rebuilt a session').toHaveLength(2);

    // Enough time for the rebuilt session's own sampler to seed and then
    // report once more (2 s to seed, 2 more to report) — still only 8 s
    // past the first POST, nowhere near its 34 s boundary.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4_000);
    });

    expect(
      postedCallsFor('presentation_buffer'),
      'no extra report from the reconnect, this far inside the window',
    ).toHaveLength(1);
  });
});
